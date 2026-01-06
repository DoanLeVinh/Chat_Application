using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Chat_Application.Server.Models;
using MongoDB.Driver;

namespace Chat_Application.Server.Services
{
    public class PresenceService : IDisposable
    {
        private readonly ConnectionManager _connectionManager;
        private readonly IMongoCollection<Presence> _presenceCollection;
        private readonly IMongoCollection<User> _userCollection; // Giả sử có User collection
        private readonly Timer _heartbeatTimer;
        private readonly Timer _cleanupTimer;
        private readonly WebSocketServer _webSocketServer; // Cần reference đến WebSocket server
        
        public PresenceService(
            ConnectionManager connectionManager,
            IMongoDatabase database,
            WebSocketServer webSocketServer = null)
        {
            _connectionManager = connectionManager;
            _presenceCollection = database.GetCollection<Presence>("presence");
            _userCollection = database.GetCollection<User>("users"); // Adjust if different
            _webSocketServer = webSocketServer;
            
            // Timer cho heartbeat (mỗi 30 giây flush DB)
            _heartbeatTimer = new Timer(
                callback: FlushPresenceToDatabase,
                state: null,
                dueTime: TimeSpan.FromSeconds(10),
                period: TimeSpan.FromSeconds(30)
            );
            
            // Timer cho cleanup (mỗi phút)
            _cleanupTimer = new Timer(
                callback: _ => _connectionManager.CleanupInactiveConnections(),
                state: null,
                dueTime: TimeSpan.FromMinutes(1),
                period: TimeSpan.FromMinutes(1)
            );
            
            Console.WriteLine("[PresenceService] Started with heartbeat every 30s");
        }
        
        // 1. Cập nhật presence khi user connect
        public void UserConnected(string userId, string connectionId)
        {
            // Cập nhật trong ConnectionManager (đã được AddConnection)
            // Cập nhật trong database
            UpdateUserPresenceInDb(userId, "online", connectionId);
            
            // Broadcast đến các conversation của user này
            BroadcastPresenceUpdate(userId, "online");
        }
        
        // 2. Cập nhật presence khi user disconnect
        public void UserDisconnected(string connectionId)
        {
            var state = _connectionManager.GetConnectionState(connectionId);
            if (state != null)
            {
                // Kiểm tra xem user còn connection nào khác không
                var userConnections = _connectionManager.GetUserConnections(state.UserId);
                var isStillOnline = userConnections.Count > 1 || 
                                   (userConnections.Count == 1 && userConnections[0] != connectionId);
                
                if (!isStillOnline)
                {
                    // User hoàn toàn offline
                    UpdateUserPresenceInDb(state.UserId, "offline", null);
                    BroadcastPresenceUpdate(state.UserId, "offline");
                }
            }
        }
        
        // 3. Xử lý heartbeat từ client
        public void HandleHeartbeat(string connectionId, string requestId)
        {
            if (_connectionManager.UpdateHeartbeat(connectionId))
            {
                // Gửi heartbeat ack
                SendHeartbeatAck(connectionId, requestId);
            }
        }
        
        // 4. Cập nhật status (online/away/offline)
        public void UpdateUserStatus(string userId, string status)
        {
            var connections = _connectionManager.GetUserConnections(userId);
            var connectionId = connections.FirstOrDefault();
            
            UpdateUserPresenceInDb(userId, status, connectionId);
            BroadcastPresenceUpdate(userId, status);
        }
        
        // 5. Lấy presence của user
        public async Task<Presence> GetUserPresence(string userId)
        {
            var filter = Builders<Presence>.Filter.Eq(p => p.UserId, userId);
            var presence = await _presenceCollection.Find(filter).FirstOrDefaultAsync();
            
            // Nếu không có trong DB, tạo mới
            if (presence == null)
            {
                presence = new Presence
                {
                    UserId = userId,
                    Status = _connectionManager.IsUserOnline(userId) ? "online" : "offline",
                    LastSeen = DateTime.UtcNow
                };
            }
            else if (_connectionManager.IsUserOnline(userId) && presence.Status != "online")
            {
                // Nếu đang online nhưng DB ghi offline
                presence.Status = "online";
                presence.LastSeen = DateTime.UtcNow;
                await _presenceCollection.ReplaceOneAsync(filter, presence);
            }
            
            return presence;
        }
        
        // PRIVATE METHODS
        
        private async void UpdateUserPresenceInDb(string userId, string status, string connectionId)
        {
            try
            {
                var filter = Builders<Presence>.Filter.Eq(p => p.UserId, userId);
                var update = Builders<Presence>.Update
                    .Set(p => p.Status, status)
                    .Set(p => p.LastSeen, DateTime.UtcNow)
                    .Set(p => p.UpdatedAt, DateTime.UtcNow)
                    .Set(p => p.ConnectionId, connectionId);
                    
                var options = new UpdateOptions { IsUpsert = true };
                
                await _presenceCollection.UpdateOneAsync(filter, update, options);
                
                Console.WriteLine($"[PresenceService] Updated {userId} status to {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PresenceService] Error updating presence: {ex.Message}");
            }
        }
        
        private async void FlushPresenceToDatabase(object state)
        {
            try
            {
                var onlineUsers = _connectionManager.GetAllOnlineUsers();
                
                var updates = new List<WriteModel<Presence>>();
                var now = DateTime.UtcNow;
                
                foreach (var userId in onlineUsers)
                {
                    var filter = Builders<Presence>.Filter.Eq(p => p.UserId, userId);
                    var update = Builders<Presence>.Update
                        .Set(p => p.Status, "online")
                        .Set(p => p.LastSeen, now)
                        .Set(p => p.UpdatedAt, now);
                        
                    updates.Add(new UpdateOneModel<Presence>(filter, update) { IsUpsert = true });
                }
                
                if (updates.Count > 0)
                {
                    await _presenceCollection.BulkWriteAsync(updates);
                    Console.WriteLine($"[PresenceService] Flushed {updates.Count} users to DB");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PresenceService] Error flushing to DB: {ex.Message}");
            }
        }
        
        private void BroadcastPresenceUpdate(string userId, string status)
        {
            // TODO: Cần lấy danh sách conversation của user
            // Hiện tại tạm thời broadcast đến tất cả connections
            
            var presenceEvent = new
            {
                type = "presence_updated",
                payload = new
                {
                    userId = userId,
                    status = status,
                    timestamp = DateTime.UtcNow
                }
            };
            
            var message = JsonSerializer.Serialize(presenceEvent);
            
            // Broadcast đến tất cả connections (tạm thời)
            // Thực tế chỉ broadcast đến members trong cùng conversation
            Console.WriteLine($"[PresenceService] Broadcasted {userId} status: {status}");
            
            // Nếu có WebSocket server reference, thực hiện broadcast
            // _webSocketServer.Broadcast(message);
        }
        
        private void SendHeartbeatAck(string connectionId, string requestId)
        {
            var ack = new
            {
                type = "heartbeat_ack",
                requestId = requestId,
                timestamp = DateTime.UtcNow
            };
            
            var message = JsonSerializer.Serialize(ack);
            
            // TODO: Gửi qua WebSocket connection
            Console.WriteLine($"[PresenceService] Sent heartbeat ack to {connectionId}");
        }
        
        public void Dispose()
        {
            _heartbeatTimer?.Dispose();
            _cleanupTimer?.Dispose();
        }
    }
}