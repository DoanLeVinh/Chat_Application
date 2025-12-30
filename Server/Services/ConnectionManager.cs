using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ChatServer.Models.Presence;

namespace ChatServer.Services
{
    public class ConnectionManager
    {
        // Lưu trữ các kết nối
        private readonly ConcurrentDictionary<string, TcpClient> _connections;
        private readonly ConcurrentDictionary<string, string> _connectionToUser;
        private readonly ConcurrentDictionary<string, List<string>> _userToConnections;
        private readonly ConcurrentDictionary<string, List<string>> _conversationConnections;
        
        private readonly PresenceService _presenceService;
        private readonly ResumeService _resumeService;
        
        public ConnectionManager(PresenceService presenceService, ResumeService resumeService)
        {
            _presenceService = presenceService;
            _resumeService = resumeService;
            
            _connections = new ConcurrentDictionary<string, TcpClient>();
            _connectionToUser = new ConcurrentDictionary<string, string>();
            _userToConnections = new ConcurrentDictionary<string, List<string>>();
            _conversationConnections = new ConcurrentDictionary<string, List<string>>();
            
            Console.WriteLine("✅ ConnectionManager initialized");
        }
        
        public void AddConnection(string connectionId, TcpClient client, string userId)
        {
            _connections[connectionId] = client;
            _connectionToUser[connectionId] = userId;
            
            _userToConnections.AddOrUpdate(userId,
                new List<string> { connectionId },
                (key, existing) =>
                {
                    if (!existing.Contains(connectionId))
                        existing.Add(connectionId);
                    return existing;
                });
            
            Console.WriteLine($"✅ Connection added: {connectionId} for user {userId}");
        }
        
        public void RemoveConnection(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var client))
            {
                client.Close();
                
                if (_connectionToUser.TryRemove(connectionId, out var userId))
                {
                    // Xóa khỏi user's connections
                    if (_userToConnections.TryGetValue(userId, out var connections))
                    {
                        connections.Remove(connectionId);
                        if (!connections.Any())
                        {
                            _userToConnections.TryRemove(userId, out _);
                        }
                    }
                    
                    // Xóa khỏi tất cả conversations
                    foreach (var convId in _conversationConnections.Keys)
                    {
                        if (_conversationConnections.TryGetValue(convId, out var convConnections))
                        {
                            convConnections.Remove(connectionId);
                        }
                    }
                    
                    // Update presence
                    _ = _presenceService.UserDisconnectedAsync(connectionId);
                }
                
                Console.WriteLine($"✅ Connection removed: {connectionId}");
            }
        }
        
        public bool IsConnectionActive(string connectionId)
        {
            return _connections.ContainsKey(connectionId);
        }
        
        public void AddToConversation(string conversationId, string connectionId)
        {
            _conversationConnections.AddOrUpdate(conversationId,
                new List<string> { connectionId },
                (key, existing) =>
                {
                    if (!existing.Contains(connectionId))
                    {
                        existing.Add(connectionId);
                    }
                    return existing;
                });
            
            Console.WriteLine($"✅ Added connection {connectionId} to conversation {conversationId}");
        }
        
        public async Task BroadcastToConversationAsync(string conversationId, object message, string excludeUserId = null)
        {
            if (_conversationConnections.TryGetValue(conversationId, out var connectionIds))
            {
                var jsonMessage = JsonSerializer.Serialize(message);
                var data = Encoding.UTF8.GetBytes(jsonMessage + "\n");
                
                foreach (var connectionId in connectionIds)
                {
                    if (_connectionToUser.TryGetValue(connectionId, out var userId) &&
                        userId != excludeUserId &&
                        _connections.TryGetValue(connectionId, out var client))
                    {
                        try
                        {
                            var stream = client.GetStream();
                            await stream.WriteAsync(data, 0, data.Length);
                            await stream.FlushAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Error broadcasting to {connectionId}: {ex.Message}");
                            RemoveConnection(connectionId);
                        }
                    }
                }
            }
        }
        
        public async Task SendToConnectionAsync(string connectionId, object message)
        {
            if (_connections.TryGetValue(connectionId, out var client))
            {
                try
                {
                    var jsonMessage = JsonSerializer.Serialize(message);
                    var data = Encoding.UTF8.GetBytes(jsonMessage + "\n");
                    
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                    await stream.FlushAsync();
                    
                    Console.WriteLine($"✅ Sent message to connection {connectionId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error sending to {connectionId}: {ex.Message}");
                    RemoveConnection(connectionId);
                }
            }
        }
        
        public async Task<ResumeResponse> HandleReconnectAsync(
            string connectionId,
            string userId,
            string resumeToken,
            Dictionary<string, long> sinceSeqByConversation)
        {
            try
            {
                Console.WriteLine($"🔄 Handling reconnect for user {userId}");
                
                // Validate resume token
                var isValid = await _presenceService.ValidateResumeToken(userId, resumeToken);
                if (!isValid)
                {
                    Console.WriteLine($"❌ Invalid resume token for user {userId}");
                    return new ResumeResponse
                    {
                        Success = false,
                        Error = "INVALID_RESUME_TOKEN"
                    };
                }
                
                // Lấy missed messages
                var missedMessages = await _resumeService.GetMissedMessagesAsync(userId, sinceSeqByConversation);
                
                // Gửi missed messages
                foreach (var message in missedMessages)
                {
                    var msg = new
                    {
                        type = "message_created",
                        data = message
                    };
                    await SendToConnectionAsync(connectionId, msg);
                }
                
                // Gửi snapshot nếu có
                if (missedMessages.Any())
                {
                    var snapshot = await _resumeService.GetSnapshotAsync(missedMessages);
                    if (snapshot.Any())
                    {
                        var snapshotMsg = new
                        {
                            type = "resume_snapshot",
                            data = snapshot
                        };
                        await SendToConnectionAsync(connectionId, snapshotMsg);
                    }
                }
                
                // Update client state
                if (missedMessages.Any())
                {
                    var lastSeqs = missedMessages
                        .GroupBy(m => m.ConversationId)
                        .ToDictionary(g => g.Key, g => g.Max(m => m.Sequence));
                    
                    await _resumeService.SaveClientStateAsync(userId, lastSeqs);
                }
                
                Console.WriteLine($"✅ Resume successful for user {userId}, sent {missedMessages.Count} messages");
                
                return new ResumeResponse
                {
                    Success = true,
                    Error = null,
                    MessagesSent = missedMessages.Count
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Resume failed for user {userId}: {ex.Message}");
                return new ResumeResponse
                {
                    Success = false,
                    Error = "RESUME_FAILED"
                };
            }
        }
        
        public async Task BroadcastServerGoingDownAsync(string reason, int reconnectAfter = 30)
        {
            var message = new
            {
                type = "server_going_down",
                data = new
                {
                    reason,
                    reconnectAfter,
                    timestamp = DateTime.UtcNow,
                    message = $"Server is shutting down. Please reconnect after {reconnectAfter} seconds."
                }
            };
            
            try
            {
                Console.WriteLine($"📢 Broadcasting server_going_down to {_connections.Count} connections");
                
                // Broadcast đến tất cả connections
                foreach (var connectionId in _connections.Keys)
                {
                    await SendToConnectionAsync(connectionId, message);
                }
                
                Console.WriteLine("✅ Broadcast completed");
                
                // Đợi 2 giây để clients nhận message
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during server shutdown broadcast: {ex.Message}");
            }
        }
        
        public void CloseAllConnections(string reason = "Server shutdown")
        {
            var closeMessage = new
            {
                type = "connection_closed",
                data = new
                {
                    reason,
                    code = 1000, // Normal closure
                    timestamp = DateTime.UtcNow
                }
            };
            
            Console.WriteLine($"🔒 Closing all connections ({_connections.Count} total): {reason}");
            
            foreach (var kvp in _connections)
            {
                try
                {
                    // Gửi close message
                    var json = JsonSerializer.Serialize(closeMessage);
                    var data = Encoding.UTF8.GetBytes(json + "\n");
                    
                    var stream = kvp.Value.GetStream();
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                    
                    // Đóng connection
                    kvp.Value.Close();
                    
                    Console.WriteLine($"✅ Closed connection {kvp.Key}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error closing connection {kvp.Key}: {ex.Message}");
                }
            }
            
            // Clear all collections
            _connections.Clear();
            _connectionToUser.Clear();
            _userToConnections.Clear();
            _conversationConnections.Clear();
            
            Console.WriteLine("✅ All connections closed and cleaned up");
        }
        
        public int GetConnectionCount()
        {
            return _connections.Count;
        }
    }
    
    public class ResumeResponse
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int MessagesSent { get; set; }
    }
}