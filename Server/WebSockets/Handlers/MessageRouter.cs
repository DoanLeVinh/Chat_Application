using System.Collections.Concurrent;
using System.Text.Json;
using ChatServer.WebSockets.Models;

namespace ChatServer.WebSockets.Handlers
{
    /// <summary>
    /// Message Router - Dispatch messages đến handlers tương ứng
    /// </summary>
    public class MessageRouter
    {
        private readonly ConcurrentDictionary<string, IMessageHandler> _handlers = new();

        public MessageRouter()
        {
            // Đăng ký các handlers mặc định
            RegisterHandler(new HeartbeatHandler());
            RegisterHandler(new EchoHandler());
            RegisterHandler(new TestHandler());
        }

        /// <summary>
        /// Đăng ký handler mới
        /// </summary>
        public void RegisterHandler(IMessageHandler handler)
        {
            _handlers.TryAdd(handler.MessageType.ToLower(), handler);
            Console.WriteLine($"[MessageRouter] Registered handler for type: {handler.MessageType}");
        }

        /// <summary>
        /// Route message đến handler phù hợp
        /// </summary>
        public async Task<WebSocketResponse> RouteAsync(string messageJson, MessageContext context)
        {
            try
            {
                // Parse JSON thành WebSocketMessage
                var message = JsonSerializer.Deserialize<WebSocketMessage>(messageJson);
                
                if (message == null || string.IsNullOrWhiteSpace(message.Type))
                {
                    return WebSocketResponse.CreateError(
                        "invalid_message",
                        "Message type is required",
                        null
                    );
                }

                var messageType = message.Type.ToLower();

                // Tìm handler phù hợp
                if (_handlers.TryGetValue(messageType, out var handler))
                {
                    Console.WriteLine($"[MessageRouter] Routing '{messageType}' to {handler.GetType().Name}");
                    return await handler.HandleAsync(message, context);
                }
                else
                {
                    return WebSocketResponse.CreateError(
                        "unknown_message_type",
                        $"No handler found for message type: {message.Type}",
                        message.RequestId
                    );
                }
            }
            catch (JsonException ex)
            {
                return WebSocketResponse.CreateError(
                    "invalid_json",
                    $"Invalid JSON format: {ex.Message}",
                    null
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MessageRouter] Error processing message: {ex.Message}");
                return WebSocketResponse.CreateError(
                    "internal_error",
                    "An error occurred while processing your message",
                    null
                );
            }
        }

        /// <summary>
        /// Lấy danh sách các message types đã đăng ký
        /// </summary>
        public IEnumerable<string> GetRegisteredTypes()
        {
            return _handlers.Keys;
        }
    }
}
