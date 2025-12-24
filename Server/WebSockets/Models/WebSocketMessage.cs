using System.Text.Json.Serialization;

namespace ChatServer.WebSockets.Models
{
    /// <summary>
    /// WebSocket message từ client gửi lên server
    /// </summary>
    public class WebSocketMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("requestId")]
        public string? RequestId { get; set; }

        [JsonPropertyName("payload")]
        public object? Payload { get; set; }
    }

    /// <summary>
    /// WebSocket response từ server gửi về client
    /// </summary>
    public class WebSocketResponse
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("requestId")]
        public string? RequestId { get; set; }

        [JsonPropertyName("payload")]
        public object? Payload { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; } = true;

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        public static WebSocketResponse CreateSuccess(string type, object? payload = null, string? requestId = null)
        {
            return new WebSocketResponse
            {
                Type = type,
                RequestId = requestId,
                Payload = payload,
                Success = true
            };
        }

        public static WebSocketResponse CreateError(string type, string error, string? requestId = null)
        {
            return new WebSocketResponse
            {
                Type = type,
                RequestId = requestId,
                Success = false,
                Error = error
            };
        }
    }

    /// <summary>
    /// Context chứa thông tin về connection và user hiện tại
    /// </summary>
    public class MessageContext
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public ConnectionManager ConnectionManager { get; set; }
        public WebSocketConnection Connection { get; set; }

        public MessageContext(
            string connectionId, 
            string userId, 
            ConnectionManager connectionManager,
            WebSocketConnection connection)
        {
            ConnectionId = connectionId;
            UserId = userId;
            ConnectionManager = connectionManager;
            Connection = connection;
        }
    }
}
