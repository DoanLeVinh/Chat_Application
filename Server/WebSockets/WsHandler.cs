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
            UserService userService)
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
                        if (connectionId != null)
                        {
                            manager.RemoveConnection(connectionId);
                            
                            // Broadcast offline status if this was the last connection
                            if (userId != null && !manager.IsUserOnline(userId))
                            {
                                await userService.SetOnlineStatusAsync(userId, false);
                                var user = await userService.GetUserByIdAsync(userId);
                                await manager.BroadcastToAllExceptAsync(userId, new WsResponse
                                {
                                    Type = "user_offline",
                                    Payload = new UserStatusChangedPayload
                                    {
                                        UserId = userId,
                                        DisplayName = user?.DisplayName ?? userId,
                                        IsOnline = false,
                                        LastSeenAt = DateTime.UtcNow
                                    }
                                });
                                Console.WriteLine($"📴 User {userId} is now offline");
                            }
                        }
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"📨 Received: {message}");

                    // Parse message (case-insensitive to read camelCase from JS)
                    var wsMessage = JsonSerializer.Deserialize<WsMessage>(
                        message,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                    if (wsMessage == null) continue;

                    // Route to handler
                    WsResponse response;

                    switch (wsMessage.Type)
                    {
                        case "auth":
                            // Simple mock auth: chấp nhận userId bất kỳ (demo)
                            var authPayload = JsonSerializer.Deserialize<AuthEvent>(
                                wsMessage.Payload.ToString() ?? "{}",
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                            if (authPayload != null && !string.IsNullOrEmpty(authPayload.UserId))
                            {
                                userId = authPayload.UserId;
                                var wasOnline = manager.IsUserOnline(userId);
                                connectionId = manager.AddConnection(webSocket, userId);
                                
                                // Get user info for display name
                                var user = await userService.GetUserByIdAsync(userId);
                                var displayName = user?.DisplayName ?? userId;
                                
                                response = new WsResponse
                                {
                                    Type = "auth_ok",
                                    RequestId = wsMessage.RequestId,
                                    Payload = new { userId, connectionId, displayName }
                                };
                                
                                // Set online status and broadcast if this is the first connection
                                if (!wasOnline)
                                {
                                    await userService.SetOnlineStatusAsync(userId, true);
                                    await manager.BroadcastToAllExceptAsync(userId, new WsResponse
                                    {
                                        Type = "user_online",
                                        Payload = new UserStatusChangedPayload
                                        {
                                            UserId = userId,
                                            DisplayName = displayName,
                                            IsOnline = true,
                                            LastSeenAt = null
                                        }
                                    });
                                    Console.WriteLine($"📶 User {userId} is now online");
                                }
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

                        case "get_online_users":
                            if (userId == null)
                            {
                                response = new WsResponse { Type = "error", RequestId = wsMessage.RequestId, Payload = new { error = "Not authenticated" } };
                            }
                            else
                            {
                                // Get all online users
                                var onlineUserIds = manager.GetAllOnlineUserIds();
                                var usersStatus = await userService.GetUsersStatusAsync(onlineUserIds);
                                var onlineUsers = usersStatus.Values.Select(u => new UserStatusChangedPayload
                                {
                                    UserId = u.UserId,
                                    DisplayName = u.DisplayName,
                                    IsOnline = true,
                                    LastSeenAt = u.LastSeenAt
                                }).ToList();
                                
                                response = new WsResponse
                                {
                                    Type = "online_users",
                                    RequestId = wsMessage.RequestId,
                                    Payload = new OnlineUsersResponsePayload { Users = onlineUsers }
                                };
                            }
                            break;

                        default:
                            response = new WsResponse
                            {
                                Type = "error",
                                RequestId = wsMessage.RequestId,
                                Payload = new { error = "Unknown event type" }
                            };
                            break;
                    }

                    // Send response
                    await SendMessageAsync(webSocket, response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WebSocket error: {ex.Message}");
                if (connectionId != null)
                {
                    manager.RemoveConnection(connectionId);
                    
                    // Broadcast offline status on error disconnect
                    if (userId != null && !manager.IsUserOnline(userId))
                    {
                        await userService.SetOnlineStatusAsync(userId, false);
                        var user = await userService.GetUserByIdAsync(userId);
                        await manager.BroadcastToAllExceptAsync(userId, new WsResponse
                        {
                            Type = "user_offline",
                            Payload = new UserStatusChangedPayload
                            {
                                UserId = userId,
                                DisplayName = user?.DisplayName ?? userId,
                                IsOnline = false,
                                LastSeenAt = DateTime.UtcNow
                            }
                        });
                    }
                }
            }
        }

        public static async Task SendMessageAsync(WebSocket webSocket, WsResponse response)
        {
            if (webSocket.State != WebSocketState.Open) return;

            // Serialize với camelCase để client JavaScript đọc được
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            var json = JsonSerializer.Serialize(response, options);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"📤 Sent: {response.Type}");
        }
    }

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
}
