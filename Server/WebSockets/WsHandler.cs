using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChatServer.Services;
using ChatServer.WebSockets.Handlers;

namespace ChatServer.WebSockets
{
    public static class WsHandler
    {
        public static async Task HandleWebSocketAsync(
            WebSocket webSocket,
            WsConnectionManager manager,
            ConversationService conversationService,
            MessageService messageService,
            UserService userService,
            PresenceService presenceService,      // THÊM: PresenceService
            ResumeService resumeService,          // THÊM: ResumeService
            ConnectionManager connectionManager)  // THÊM: ConnectionManager
        {
            var buffer = new byte[1024 * 4];
            string? connectionId = null;
            string? userId = null;

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (connectionId != null && userId != null)
                        {
                            // THÊM: Xử lý user disconnected
                            await presenceService.UserDisconnected(connectionId);
                            connectionManager.RemoveConnection(connectionId);
                            manager.RemoveConnection(connectionId);
                        }
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"📨 Received: {message}");

                    // Parse message
                    var wsMessage = JsonSerializer.Deserialize<WsMessage>(
                        message,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                    if (wsMessage == null) continue;

                    // Route to handler
                    WsResponse response;

                    switch (wsMessage.Type.ToLower())
                    {
                        case "auth":
                            // Simple mock auth: chấp nhận userId bất kỳ
                            var authPayload = JsonSerializer.Deserialize<AuthEvent>(
                                wsMessage.Payload.ToString() ?? "{}",
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                            if (authPayload != null && !string.IsNullOrEmpty(authPayload.UserId))
                            {
                                userId = authPayload.UserId;
                                connectionId = manager.AddConnection(webSocket, userId);
                                
                                // THÊM: Xử lý user connected
                                await presenceService.UserConnected(userId, connectionId);
                                connectionManager.AddConnection(connectionId, userId);
                                
                                response = new WsResponse
                                {
                                    Type = "auth_ok",
                                    RequestId = wsMessage.RequestId,
                                    Payload = new { userId, connectionId, displayName = authPayload.UserId }
                                };
                                Console.WriteLine($"✅ User authenticated: {userId}");
                            }
                            else
                            {
                                response = new WsResponse
                                {
                                    Type = "error",
                                    RequestId = wsMessage.RequestId,
                                    Payload = new { error = "Invalid auth payload" }
                                };
                                Console.WriteLine("❌ Auth failed: Invalid payload");
                            }
                            break;

                        // ============ CÁC EVENT CỦA NGƯỜI 3 ============
                        case "heartbeat":
                            if (userId == null || connectionId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                // Xử lý heartbeat
                                presenceService.HandleHeartbeat(connectionId);
                                
                                response = new WsResponse
                                {
                                    Type = "heartbeat_ack",
                                    RequestId = wsMessage.RequestId,
                                    Payload = new { 
                                        timestamp = DateTime.UtcNow,
                                        connectionId = connectionId
                                    }
                                };
                                Console.WriteLine($"❤️ Heartbeat from {userId}");
                            }
                            break;

                        case "presence_update":
                            if (userId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                var presencePayload = JsonSerializer.Deserialize<PresenceUpdateEvent>(
                                    wsMessage.Payload.ToString() ?? "{}",
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                                );
                                
                                if (presencePayload != null && !string.IsNullOrEmpty(presencePayload.Status))
                                {
                                    await presenceService.UpdateStatus(userId, presencePayload.Status);
                                    
                                    response = new WsResponse
                                    {
                                        Type = "presence_updated",
                                        RequestId = wsMessage.RequestId,
                                        Payload = new { 
                                            userId = userId,
                                            status = presencePayload.Status,
                                            timestamp = DateTime.UtcNow
                                        }
                                    };
                                    Console.WriteLine($"👤 Presence updated: {userId} -> {presencePayload.Status}");
                                }
                                else
                                {
                                    response = new WsResponse
                                    {
                                        Type = "error",
                                        RequestId = wsMessage.RequestId,
                                        Payload = new { error = "Invalid presence update payload" }
                                    };
                                }
                            }
                            break;

                        case "resume":
                            if (userId == null || connectionId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                var resumePayload = JsonSerializer.Deserialize<ResumeRequestEvent>(
                                    wsMessage.Payload.ToString() ?? "{}",
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                                );
                                
                                if (resumePayload != null && resumePayload.SinceSeqByConversation != null)
                                {
                                    var allMissedMessages = new List<object>();
                                    
                                    foreach (var conv in resumePayload.SinceSeqByConversation)
                                    {
                                        var missedMessages = await resumeService.GetMissedMessages(
                                            conv.Key, 
                                            conv.Value
                                        );
                                        
                                        // Convert to client-friendly format
                                        foreach (var msg in missedMessages)
                                        {
                                            allMissedMessages.Add(new
                                            {
                                                conversationId = conv.Key,
                                                seq = msg.Seq,
                                                message = new
                                                {
                                                    id = msg.Id,
                                                    senderId = msg.SenderId,
                                                    content = msg.Content,
                                                    type = msg.Type,
                                                    timestamp = msg.CreatedAt,
                                                    seq = msg.Seq
                                                }
                                            });
                                        }
                                        
                                        // Cập nhật last seen seq
                                        var currentSeq = await resumeService.GetCurrentSeq(conv.Key);
                                        connectionManager.UpdateLastSeenSeq(connectionId, conv.Key, currentSeq);
                                    }
                                    
                                    response = new WsResponse
                                    {
                                        Type = "resume_response",
                                        RequestId = wsMessage.RequestId,
                                        Payload = new { 
                                            success = true,
                                            missedMessages = allMissedMessages,
                                            timestamp = DateTime.UtcNow
                                        }
                                    };
                                    Console.WriteLine($"🔄 Resume processed for {userId}: {allMissedMessages.Count} messages");
                                }
                                else
                                {
                                    response = new WsResponse
                                    {
                                        Type = "resume_response",
                                        RequestId = wsMessage.RequestId,
                                        Payload = new { 
                                            success = false,
                                            error = "Invalid resume payload",
                                            missedMessages = new List<object>()
                                        }
                                    };
                                }
                            }
                            break;

                        case "get_presence":
    if (userId == null)
    {
        response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
    }
    else
    {
        // Đơn giản: chỉ trả về status từ ConnectionManager
        var isOnline = connectionManager.IsUserOnline(userId);
        
        response = new WsResponse
        {
            Type = "presence_info",
            RequestId = wsMessage.RequestId,
            Payload = new { 
                userId = userId,
                status = isOnline ? "online" : "offline",
                isOnline = isOnline,
                timestamp = DateTime.UtcNow
            }
        };
    }
    break;

                        // ============ CÁC EVENT CŨ ============
                        case "send_message":
                            if (userId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                response = await MessageHandlers.HandleSendMessageAsync(wsMessage, userId, conversationService, messageService, manager);
                            }
                            break;

                        case "create_group":
                            if (userId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                response = await ConversationHandlers.HandleCreateGroupAsync(wsMessage, userId, conversationService, manager);
                            }
                            break;

                        case "add_member":
                            if (userId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                response = await ConversationHandlers.HandleAddMemberAsync(wsMessage, userId, conversationService, manager);
                            }
                            break;

                        case "remove_member":
                            if (userId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                response = await ConversationHandlers.HandleRemoveMemberAsync(wsMessage, userId, conversationService, manager);
                            }
                            break;

                        case "get_conversations":
                            if (userId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                response = await ConversationHandlers.HandleGetConversationsAsync(wsMessage, userId, conversationService);
                            }
                            break;

                        case "get_messages":
                            if (userId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                response = await MessageHandlers.HandleGetMessagesAsync(wsMessage, userId, conversationService, messageService);
                            }
                            break;

                        default:
                            response = new WsResponse
                            {
                                Type = "error",
                                RequestId = wsMessage.RequestId,
                                Payload = new { error = $"Unknown event type: {wsMessage.Type}" }
                            };
                            Console.WriteLine($"❌ Unknown event type: {wsMessage.Type}");
                            break;
                    }

                    // Send response
                    await SendMessageAsync(webSocket, response);
                }
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"❌ WebSocket disconnected: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WebSocket error: {ex.Message}");
            }
            finally
            {
                // Đảm bảo cleanup khi connection đóng
                if (connectionId != null && userId != null)
                {
                    Console.WriteLine($"🔌 Cleaning up connection: {connectionId} for user: {userId}");
                    
                    // Cleanup từ tất cả managers
                    try
                    {
                        await presenceService.UserDisconnected(connectionId);
                        connectionManager.RemoveConnection(connectionId);
                        manager.RemoveConnection(connectionId);
                    }
                    catch (Exception cleanupEx)
                    {
                        Console.WriteLine($"⚠️ Cleanup error: {cleanupEx.Message}");
                    }
                }
            }
        }

        public static async Task SendMessageAsync(WebSocket webSocket, WsResponse response)
        {
            if (webSocket.State != WebSocketState.Open) return;

            try
            {
                // Serialize với camelCase để client JavaScript đọc được
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };
                var json = JsonSerializer.Serialize(response, options);
                var bytes = Encoding.UTF8.GetBytes(json);
                await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                Console.WriteLine($"📤 Sent: {response.Type} (req: {response.RequestId})");
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"❌ Failed to send message (WebSocket closed): {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to send message: {ex.Message}");
            }
        }
    }

    // ============ CÁC CLASS MODEL ============

    public class WsMessage
    {
        public string Type { get; set; } = "";
        public string? RequestId { get; set; }
        public object Payload { get; set; } = new { };
    }

    public class WsResponse
    {
        public string Type { get; set; } = "";
        public string? RequestId { get; set; }
        public object Payload { get; set; } = new { };
    }

    public class AuthEvent
    {
        public string UserId { get; set; } = "";
    }

    // ============ CLASS MỚI CHO NGƯỜI 3 ============

    public class PresenceUpdateEvent
    {
        public string Status { get; set; } = ""; // online, away, offline, busy
        public string? UserId { get; set; }
    }

    public class ResumeRequestEvent
    {
        public Dictionary<string, long> SinceSeqByConversation { get; set; } = new Dictionary<string, long>();
    }

    public class HeartbeatEvent
    {
        public long Timestamp { get; set; }
        public string? ClientId { get; set; }
    }

    public class PresenceInfoRequest
    {
        public string? UserId { get; set; } // Nếu null, lấy cho current user
    }
}