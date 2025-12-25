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
                                connectionId = manager.AddConnection(webSocket, userId);
                                response = new WsResponse
                                {
                                    Type = "auth_ok",
                                    RequestId = wsMessage.RequestId,
                                    Payload = new { userId, connectionId, displayName = authPayload.UserId }
                                };
                                Console.WriteLine($"✅ User authenticated (mock): {userId}");
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
