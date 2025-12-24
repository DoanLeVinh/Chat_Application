using ChatServer.WebSockets.Models;

namespace ChatServer.WebSockets.Handlers
{
    /// <summary>
    /// Interface cho các message handlers
    /// </summary>
    public interface IMessageHandler
    {
        /// <summary>
        /// Message type mà handler này xử lý
        /// </summary>
        string MessageType { get; }

        /// <summary>
        /// Xử lý message
        /// </summary>
        Task<WebSocketResponse> HandleAsync(WebSocketMessage message, MessageContext context);
    }

    /// <summary>
    /// Base class cho message handlers
    /// </summary>
    public abstract class BaseMessageHandler : IMessageHandler
    {
        public abstract string MessageType { get; }

        public abstract Task<WebSocketResponse> HandleAsync(WebSocketMessage message, MessageContext context);

        protected WebSocketResponse Success(object? payload = null, string? requestId = null)
        {
            return WebSocketResponse.CreateSuccess($"{MessageType}_ok", payload, requestId);
        }

        protected WebSocketResponse Error(string error, string? requestId = null)
        {
            return WebSocketResponse.CreateError($"{MessageType}_error", error, requestId);
        }
    }
}
