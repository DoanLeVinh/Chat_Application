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
            PresenceService presenceService,
            ResumeService resumeService,
            PresenceResumeManager presenceManager)
        {
            var buffer = new byte[1024 * 4];
            string? connectionId = null;
            string? userId = null;

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (connectionId != null)
                        {
                            manager.RemoveConnection(connectionId);
                        }

                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closed by client",
                            CancellationToken.None
                        );
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"📨 Received: {message}");

                    var wsMessage = JsonSerializer.Deserialize<WsMessage>(
                        message,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (wsMessage == null)
                        continue;

                    WsResponse response;

                    switch (wsMessage.Type)
                    {
                        // ================= AUTH =================
                        case "auth":
                        {
                            var authPayload = JsonSerializer.Deserialize<AuthEvent>(
                                wsMessage.Payload.ToString() ?? "{}",
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );

                            if (authPayload != null && !string.IsNullOrEmpty(authPayload.UserId))
                            {
                                userId = authPayload.UserId;
                                connectionId = manager.AddConnection(webSocket, userId);

                                response = new WsResponse
                                {
                                    Type = "auth_ok",
                                    RequestId = wsMessage.RequestId,
                                    Payload = new
                                    {
                                        userId,
                                        connectionId,
                                        displayName = userId
                                    }
                                };

                                Console.WriteLine($"✅ Authenticated: {userId}");
                            }
                            else
                            {
                                response = Error(wsMessage, "Invalid auth payload");
                            }
                            break;
                        }

                        // ================= CHAT =================
                        case "send_message":
                        {
                            if (userId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await MessageHandlers.HandleSendMessageAsync(
                                    wsMessage,
                                    userId,
                                    conversationService,
                                    messageService,
                                    manager
                                );
                            }
                            break;
                        }

                        // ================= CONVERSATION =================
                        case "create_group":
                        {
                            if (userId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await ConversationHandlers.HandleCreateGroupAsync(
                                    wsMessage,
                                    userId,
                                    conversationService,
                                    manager
                                );
                            }
                            break;
                        }

                        case "add_member":
                        {
                            if (userId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await ConversationHandlers.HandleAddMemberAsync(
                                    wsMessage,
                                    userId,
                                    conversationService,
                                    manager
                                );
                            }
                            break;
                        }

                        case "remove_member":
                        {
                            if (userId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await ConversationHandlers.HandleRemoveMemberAsync(
                                    wsMessage,
                                    userId,
                                    conversationService,
                                    manager
                                );
                            }
                            break;
                        }

                        case "get_conversations":
                        {
                            if (userId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await ConversationHandlers.HandleGetConversationsAsync(
                                    wsMessage,
                                    userId,
                                    conversationService
                                );
                            }
                            break;
                        }

                        case "get_messages":
                        {
                            if (userId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await MessageHandlers.HandleGetMessagesAsync(
                                    wsMessage,
                                    userId,
                                    conversationService,
                                    messageService
                                );
                            }
                            break;
                        }

                        // ================= PRESENCE / RESUME =================
                        case "heartbeat":
                        {
                            if (userId == null || connectionId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await PresenceResumeHandlers.HandleHeartbeatAsync(
                                    wsMessage,
                                    userId,
                                    connectionId,
                                    presenceService,
                                    presenceManager
                                );
                            }
                            break;
                        }

                        case "resume":
                        {
                            if (userId == null || connectionId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await PresenceResumeHandlers.HandleResumeAsync(
                                    wsMessage,
                                    userId,
                                    connectionId,
                                    resumeService,
                                    presenceService,
                                    presenceManager,
                                    conversationService
                                );
                            }
                            break;
                        }

                        case "presence_subscribe":
                        {
                            if (userId == null || connectionId == null)
                            {
                                response = Error(wsMessage, "Not authenticated");
                            }
                            else
                            {
                                response = await PresenceResumeHandlers.HandlePresenceSubscribeAsync(
                                    wsMessage,
                                    userId,
                                    connectionId,
                                    presenceManager
                                );
                            }
                            break;
                        }

                        // ================= DEFAULT =================
                        default:
                            response = Error(wsMessage, "Unknown event type");
                            break;
                    }

                    await SendMessageAsync(webSocket, response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WebSocket error: {ex.Message}");

                if (connectionId != null)
                {
                    manager.RemoveConnection(connectionId);
                }
            }
        }

        // ================= HELPERS =================
        private static WsResponse Error(WsMessage msg, string error)
        {
            return new WsResponse
            {
                Type = "error",
                RequestId = msg.RequestId,
                Payload = new { error }
            };
        }

        private static async Task SendMessageAsync(WebSocket webSocket, WsResponse response)
        {
            if (webSocket.State != WebSocketState.Open)
                return;

            var json = JsonSerializer.Serialize(
                response,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }
            );

            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );

            Console.WriteLine($"📤 Sent: {response.Type}");
        }
    }

    // ================= DTO =================
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
