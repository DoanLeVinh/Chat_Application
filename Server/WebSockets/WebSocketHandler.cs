using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChatServer.Services;
using ChatServer.WebSockets.Handlers;
using ChatServer.WebSockets.Models;
using Microsoft.IdentityModel.Tokens;

namespace ChatServer.WebSockets
{
    public class WebSocketHandler
    {
        private readonly ConnectionManager _connectionManager;
        private readonly UserRepository _userRepository;
        private readonly MessageRouter _messageRouter;
        private readonly string _jwtSecretKey;

        public WebSocketHandler(
            ConnectionManager connectionManager, 
            UserRepository userRepository,
            MessageRouter messageRouter,
            string jwtSecretKey)
        {
            _connectionManager = connectionManager;
            _userRepository = userRepository;
            _messageRouter = messageRouter;
            _jwtSecretKey = jwtSecretKey;
        }

        public async Task HandleWebSocketAsync(HttpContext context, WebSocket webSocket)
        {
            string? userId = null;
            string? connectionId = null;

            try
            {
                // Nhận message đầu tiên chứa JWT token
                var authMessage = await ReceiveMessageAsync(webSocket);
                
                if (string.IsNullOrEmpty(authMessage))
                {
                    await CloseSocketAsync(webSocket, "AUTH_REQUIRED", "Authentication token required");
                    return;
                }

                // Xác thực JWT token
                userId = ValidateToken(authMessage);
                if (string.IsNullOrEmpty(userId))
                {
                    await CloseSocketAsync(webSocket, "AUTH_FAILED", "Invalid or expired token");
                    return;
                }

                // Thêm connection vào ConnectionManager
                _connectionManager.AddConnection(userId, webSocket);
                var connections = _connectionManager.GetUserConnections(userId);
                connectionId = connections.Last().ConnectionId;

                // Cập nhật trạng thái online
                await _userRepository.UpdateOnlineStatusAsync(userId, true);

                // Gửi message xác nhận
                await SendMessageAsync(webSocket, new
                {
                    type = "auth_ok",
                    payload = new
                    {
                        connectionId,
                        userId,
                        message = "WebSocket connected successfully"
                    }
                });

                Console.WriteLine($"[WebSocket] User {userId} authenticated. ConnectionId: {connectionId}");

                // Bắt đầu nhận messages
                await ReceiveMessagesAsync(webSocket, connectionId, userId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebSocket] Error: {ex.Message}");
            }
            finally
            {
                // Cleanup khi disconnect
                if (!string.IsNullOrEmpty(connectionId))
                {
                    _connectionManager.RemoveConnection(connectionId);
                }

                if (!string.IsNullOrEmpty(userId))
                {
                    var remainingConnections = _connectionManager.GetUserConnections(userId);
                    if (remainingConnections.Count == 0)
                    {
                        await _userRepository.UpdateOnlineStatusAsync(userId, false);
                    }
                }

                if (webSocket.State != WebSocketState.Closed)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed",
                        CancellationToken.None);
                }

                Console.WriteLine($"[WebSocket] Connection {connectionId} closed for user {userId}");
            }
        }

        private async Task ReceiveMessagesAsync(WebSocket webSocket, string connectionId, string userId)
        {
            var buffer = new byte[1024 * 4];

            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _connectionManager.UpdateActivity(connectionId);
                    
                    Console.WriteLine($"[WebSocket] Received from {userId}: {messageJson}");
                    
                    // Tạo MessageContext
                    var connection = _connectionManager.GetConnection(connectionId);
                    if (connection == null) continue;

                    var context = new MessageContext(
                        connectionId,
                        userId,
                        _connectionManager,
                        connection
                    );

                    // Route message đến handler tương ứng
                    var response = await _messageRouter.RouteAsync(messageJson, context);
                    
                    // Gửi response về client
                    await SendMessageAsync(webSocket, response);
                }
            }
        }

        private async Task<string?> ReceiveMessageAsync(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                return Encoding.UTF8.GetString(buffer, 0, result.Count);
            }

            return null;
        }

        private async Task SendMessageAsync(WebSocket webSocket, object message)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }

        private async Task CloseSocketAsync(WebSocket webSocket, string code, string reason)
        {
            var errorMessage = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "error",
                code,
                message = reason
            });

            var buffer = Encoding.UTF8.GetBytes(errorMessage);
            await webSocket.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);

            await webSocket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                reason,
                CancellationToken.None);
        }

        private string? ValidateToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_jwtSecretKey);

                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var userId = jwtToken.Claims.First(x => x.Type == "nameid").Value;

                return userId;
            }
            catch
            {
                return null;
            }
        }
    }
}
