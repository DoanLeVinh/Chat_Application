using ChatServer.WebSockets.Models;

namespace ChatServer.WebSockets.Handlers
{
    /// <summary>
    /// Handler cho heartbeat messages để duy trì kết nối
    /// </summary>
    public class HeartbeatHandler : BaseMessageHandler
    {
        public override string MessageType => "heartbeat";

        public override Task<WebSocketResponse> HandleAsync(WebSocketMessage message, MessageContext context)
        {
            // Cập nhật last activity
            context.ConnectionManager.UpdateActivity(context.ConnectionId);

            // Trả về pong
            var response = Success(new
            {
                timestamp = DateTime.UtcNow,
                message = "pong"
            }, message.RequestId);

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Handler cho echo messages (for testing)
    /// </summary>
    public class EchoHandler : BaseMessageHandler
    {
        public override string MessageType => "echo";

        public override Task<WebSocketResponse> HandleAsync(WebSocketMessage message, MessageContext context)
        {
            var response = Success(new
            {
                originalPayload = message.Payload,
                timestamp = DateTime.UtcNow,
                connectionId = context.ConnectionId
            }, message.RequestId);

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Handler cho test messages
    /// </summary>
    public class TestHandler : BaseMessageHandler
    {
        public override string MessageType => "test";

        public override Task<WebSocketResponse> HandleAsync(WebSocketMessage message, MessageContext context)
        {
            var response = Success(new
            {
                message = "Test handler received your message!",
                userId = context.UserId,
                connectionId = context.ConnectionId,
                receivedPayload = message.Payload,
                timestamp = DateTime.UtcNow
            }, message.RequestId);

            return Task.FromResult(response);
        }
    }
}
