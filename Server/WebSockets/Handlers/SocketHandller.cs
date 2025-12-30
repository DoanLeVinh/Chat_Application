using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ChatServer.Models.Presence;
using ChatServer.Services;

namespace ChatServer.Handlers
{
    public class SocketHandler
    {
        private readonly TcpListener _listener;
        private readonly PresenceService _presenceService;
        private readonly ResumeService _resumeService;
        private readonly ConnectionManager _connectionManager;
        private readonly UserService _userService;
        private bool _isRunning = false;
        private bool _isShuttingDown = false;
        
        public SocketHandler(
            PresenceService presenceService,
            ResumeService resumeService,
            ConnectionManager connectionManager,
            UserService userService)
        {
            _presenceService = presenceService;
            _resumeService = resumeService;
            _connectionManager = connectionManager;
            _userService = userService;
            
            _listener = new TcpListener(IPAddress.Any, 8888);
        }
        
        public async Task StartAsync()
        {
            _isRunning = true;
            _listener.Start();
            
            Console.WriteLine("🚀 Socket server started on port 8888");
            Console.WriteLine($"📅 Server started at {DateTime.Now}");
            Console.WriteLine("=".PadRight(50, '='));
            
            while (_isRunning && !_isShuttingDown)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex) when (ex is ObjectDisposedException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error accepting client connection: {ex.Message}");
                }
            }
        }
        
        private async Task HandleClientAsync(TcpClient client)
        {
            string connectionId = Guid.NewGuid().ToString();
            var buffer = new byte[4096];
            
            try
            {
                var remoteEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
                var ipAddress = remoteEndpoint?.Address.ToString() ?? "unknown";
                
                Console.WriteLine($"🆕 New connection: {connectionId} from {ipAddress}");
                
                var stream = client.GetStream();
                
                while (client.Connected && !_isShuttingDown)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var messages = message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var msg in messages)
                    {
                        await ProcessMessageAsync(connectionId, msg, client, ipAddress);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error handling client {connectionId}: {ex.Message}");
            }
            finally
            {
                _connectionManager.RemoveConnection(connectionId);
                client.Close();
                
                Console.WriteLine($"🔚 Connection closed: {connectionId}");
            }
        }
        
        private async Task ProcessMessageAsync(string connectionId, string message, TcpClient client, string ipAddress)
        {
            try
            {
                Console.WriteLine($"📨 Received message from {connectionId}: {message.Length} chars");
                
                var jsonDoc = JsonDocument.Parse(message);
                var root = jsonDoc.RootElement;
                
                if (!root.TryGetProperty("type", out var typeElement))
                {
                    await SendErrorAsync(connectionId, "Missing message type");
                    return;
                }
                
                var type = typeElement.GetString();
                
                switch (type)
                {
                    case "auth":
                        await HandleAuthAsync(connectionId, root, client, ipAddress);
                        break;
                        
                    case "heartbeat":
                        await HandleHeartbeatAsync(connectionId, root);
                        break;
                        
                    case "resume":
                        await HandleResumeAsync(connectionId, root, client, ipAddress);
                        break;
                        
                    case "presence_update":
                        await HandlePresenceUpdateAsync(connectionId, root);
                        break;
                        
                    case "join_conversation":
                        await HandleJoinConversationAsync(connectionId, root);
                        break;
                        
                    case "get_presence":
                        await HandleGetPresenceAsync(connectionId, root);
                        break;
                        
                    default:
                        Console.WriteLine($"⚠️ Unknown message type: {type}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"❌ Invalid JSON message: {ex.Message}");
                await SendErrorAsync(connectionId, "Invalid JSON format");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error processing message: {ex.Message}");
                await SendErrorAsync(connectionId, "Internal server error");
            }
        }
        
        private async Task HandleAuthAsync(string connectionId, JsonElement root, TcpClient client, string ipAddress)
        {
            try
            {
                var data = root.GetProperty("data");
                var userId = data.GetProperty("userId").GetString();
                var deviceId = data.GetProperty("deviceId").GetString();
                
                Console.WriteLine($"🔐 Authentication attempt: {userId} on device {deviceId}");
                
                // Authenticate user (đơn giản)
                var user = await _userService.AuthenticateUserAsync(userId, "");
                
                // Thêm connection
                _connectionManager.AddConnection(connectionId, client, userId);
                
                // Tạo session và update presence
                var session = await _presenceService.UserConnectedAsync(userId, connectionId, deviceId, ipAddress);
                
                // Gửi response
                var response = new
                {
                    type = "auth_success",
                    data = new
                    {
                        userId,
                        sessionId = session.SessionId,
                        resumeToken = session.ResumeToken,
                        serverTime = DateTime.UtcNow,
                        heartbeatInterval = 15
                    }
                };
                
                await _connectionManager.SendToConnectionAsync(connectionId, response);
                
                Console.WriteLine($"✅ User authenticated: {userId} on device {deviceId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Authentication failed: {ex.Message}");
                await SendErrorAsync(connectionId, "Authentication failed");
            }
        }
        
        private async Task HandleHeartbeatAsync(string connectionId, JsonElement root)
        {
            try
            {
                var data = root.GetProperty("data");
                var userId = data.GetProperty("userId").GetString();
                
                // Update last seen
                await _presenceService.UpdateUserPresenceAsync(userId, PresenceStatus.Online);
                
                // Gửi ack
                var response = new
                {
                    type = "heartbeat_ack",
                    data = new
                    {
                        timestamp = DateTime.UtcNow
                    }
                };
                
                await _connectionManager.SendToConnectionAsync(connectionId, response);
                
                Console.WriteLine($"💓 Heartbeat from user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Heartbeat error: {ex.Message}");
            }
        }
        
        private async Task HandleResumeAsync(string connectionId, JsonElement root, TcpClient client, string ipAddress)
        {
            try
            {
                var data = root.GetProperty("data");
                var userId = data.GetProperty("userId").GetString();
                var resumeToken = data.GetProperty("resumeToken").GetString();
                var deviceId = data.GetProperty("deviceId").GetString();
                
                Console.WriteLine($"🔄 Resume attempt for user {userId}");
                
                // Parse sinceSeqByConversation
                var sinceSeqDict = new Dictionary<string, long>();
                if (data.TryGetProperty("sinceSeqByConversation", out var seqElement))
                {
                    foreach (var prop in seqElement.EnumerateObject())
                    {
                        sinceSeqDict[prop.Name] = prop.Value.GetInt64();
                    }
                }
                
                // Xử lý resume
                var result = await _connectionManager.HandleReconnectAsync(
                    connectionId, userId, resumeToken, sinceSeqDict);
                
                if (result.Success)
                {
                    // Thêm connection mới
                    _connectionManager.AddConnection(connectionId, client, userId);
                    
                    // Update presence
                    await _presenceService.UserConnectedAsync(userId, connectionId, deviceId, ipAddress);
                    
                    // Gửi success response
                    var response = new
                    {
                        type = "resume_success",
                        data = new
                        {
                            userId,
                            resumeToken = await _presenceService.GetResumeToken(userId),
                            messagesReceived = result.MessagesSent,
                            serverTime = DateTime.UtcNow
                        }
                    };
                    
                    await _connectionManager.SendToConnectionAsync(connectionId, response);
                    
                    Console.WriteLine($"✅ Resume successful for user {userId}");
                }
                else
                {
                    var errorResponse = new
                    {
                        type = "resume_error",
                        data = new
                        {
                            error = result.Error,
                            message = "Please re-authenticate"
                        }
                    };
                    
                    await _connectionManager.SendToConnectionAsync(connectionId, errorResponse);
                    
                    Console.WriteLine($"❌ Resume failed for user {userId}: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Resume error: {ex.Message}");
                await SendErrorAsync(connectionId, "Resume failed");
            }
        }
        
        private async Task HandlePresenceUpdateAsync(string connectionId, JsonElement root)
        {
            try
            {
                var data = root.GetProperty("data");
                var userId = data.GetProperty("userId").GetString();
                var statusStr = data.GetProperty("status").GetString();
                var customStatus = data.GetProperty("customStatus").GetString();
                
                if (Enum.TryParse<PresenceStatus>(statusStr, true, out var status))
                {
                    await _presenceService.UpdateUserPresenceAsync(userId, status, customStatus);
                    
                    Console.WriteLine($"👤 User {userId} updated presence to {status}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Presence update error: {ex.Message}");
            }
        }
        
        private async Task HandleJoinConversationAsync(string connectionId, JsonElement root)
        {
            try
            {
                var data = root.GetProperty("data");
                var userId = data.GetProperty("userId").GetString();
                var conversationId = data.GetProperty("conversationId").GetString();
                
                // Thêm vào conversation
                _connectionManager.AddToConversation(conversationId, connectionId);
                
                // Update user's conversations trong presence
                var presence = await _presenceService.GetUserPresenceAsync(userId);
                if (presence != null && !presence.ConversationIds.Contains(conversationId))
                {
                    var newConversations = new List<string>(presence.ConversationIds) { conversationId };
                    await _presenceService.UpdateConversationsAsync(userId, newConversations);
                }
                
                var response = new
                {
                    type = "join_success",
                    data = new { conversationId }
                };
                
                await _connectionManager.SendToConnectionAsync(connectionId, response);
                
                Console.WriteLine($"✅ User {userId} joined conversation {conversationId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Join conversation error: {ex.Message}");
            }
        }
        
        private async Task HandleGetPresenceAsync(string connectionId, JsonElement root)
        {
            try
            {
                var data = root.GetProperty("data");
                var userIds = data.GetProperty("userIds").EnumerateArray()
                    .Select(x => x.GetString())
                    .ToList();
                
                var presences = new List<object>();
                foreach (var userId in userIds)
                {
                    var presence = await _presenceService.GetUserPresenceAsync(userId);
                    if (presence != null)
                    {
                        presences.Add(new
                        {
                            userId = presence.UserId,
                            status = presence.Status.ToString(),
                            lastSeen = presence.LastSeen,
                            customStatus = presence.CustomStatus
                        });
                    }
                }
                
                var response = new
                {
                    type = "presence_batch",
                    data = new { presences }
                };
                
                await _connectionManager.SendToConnectionAsync(connectionId, response);
                
                Console.WriteLine($"📊 Sent batch presence for {userIds.Count} users");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Get presence error: {ex.Message}");
            }
        }
        
        private async Task SendErrorAsync(string connectionId, string message)
        {
            var error = new
            {
                type = "error",
                data = new { message }
            };
            
            await _connectionManager.SendToConnectionAsync(connectionId, error);
            
            Console.WriteLine($"⚠️ Sent error to {connectionId}: {message}");
        }
        
        // Graceful shutdown
        public async Task GracefulShutdownAsync(string reason = "Server maintenance")
        {
            if (_isShuttingDown) return;
            
            _isShuttingDown = true;
            Console.WriteLine($"\n🔄 Starting graceful shutdown: {reason}");
            
            try
            {
                // 1. Broadcast server going down
                await _connectionManager.BroadcastServerGoingDownAsync(reason);
                
                // 2. Cleanup presence data
                await _presenceService.CleanupAllPresence();
                
                // 3. Close all connections
                _connectionManager.CloseAllConnections(reason);
                
                // 4. Stop listener
                _isRunning = false;
                _listener.Stop();
                
                Console.WriteLine("✅ Graceful shutdown completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during graceful shutdown: {ex.Message}");
            }
        }
        
        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
            Console.WriteLine("🛑 Server stopped");
        }
    }
}
