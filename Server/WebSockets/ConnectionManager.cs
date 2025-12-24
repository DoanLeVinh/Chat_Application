using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace ChatServer.WebSockets
{
    public class WebSocketConnection
    {
        public string ConnectionId { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public WebSocket Socket { get; set; }
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

        public WebSocketConnection(WebSocket socket, string userId)
        {
            Socket = socket;
            UserId = userId;
        }
    }

    public class ConnectionManager
    {
        // Dictionary: UserId -> List<WebSocketConnection> (1 user có thể có nhiều connections)
        private readonly ConcurrentDictionary<string, List<WebSocketConnection>> _userConnections = new();
        
        // Dictionary: ConnectionId -> WebSocketConnection (để truy cập nhanh)
        private readonly ConcurrentDictionary<string, WebSocketConnection> _connections = new();

        public void AddConnection(string userId, WebSocket socket)
        {
            var connection = new WebSocketConnection(socket, userId);

            // Thêm vào dictionary connectionId -> connection
            _connections.TryAdd(connection.ConnectionId, connection);

            // Thêm vào dictionary userId -> list connections
            _userConnections.AddOrUpdate(
                userId,
                new List<WebSocketConnection> { connection },
                (key, existingList) =>
                {
                    existingList.Add(connection);
                    return existingList;
                });

            Console.WriteLine($"[ConnectionManager] User {userId} connected. ConnectionId: {connection.ConnectionId}");
        }

        public void RemoveConnection(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                if (_userConnections.TryGetValue(connection.UserId, out var userConnectionsList))
                {
                    userConnectionsList.RemoveAll(c => c.ConnectionId == connectionId);
                    
                    // Nếu user không còn connection nào, xóa khỏi dictionary
                    if (userConnectionsList.Count == 0)
                    {
                        _userConnections.TryRemove(connection.UserId, out _);
                    }
                }

                Console.WriteLine($"[ConnectionManager] Removed connection {connectionId} for user {connection.UserId}");
            }
        }

        public List<WebSocketConnection> GetUserConnections(string userId)
        {
            return _userConnections.TryGetValue(userId, out var connections) 
                ? connections 
                : new List<WebSocketConnection>();
        }

        public WebSocketConnection? GetConnection(string connectionId)
        {
            return _connections.TryGetValue(connectionId, out var connection) 
                ? connection 
                : null;
        }

        public async Task SendToUserAsync(string userId, string message)
        {
            var connections = GetUserConnections(userId);
            var tasks = connections
                .Where(c => c.Socket.State == WebSocketState.Open)
                .Select(c => SendMessageAsync(c.Socket, message));

            await Task.WhenAll(tasks);
        }

        public async Task SendToConnectionAsync(string connectionId, string message)
        {
            var connection = GetConnection(connectionId);
            if (connection != null && connection.Socket.State == WebSocketState.Open)
            {
                await SendMessageAsync(connection.Socket, message);
            }
        }

        public async Task BroadcastAsync(string message, string? excludeUserId = null)
        {
            var tasks = _connections.Values
                .Where(c => c.Socket.State == WebSocketState.Open && c.UserId != excludeUserId)
                .Select(c => SendMessageAsync(c.Socket, message));

            await Task.WhenAll(tasks);
        }

        private async Task SendMessageAsync(WebSocket socket, string message)
        {
            try
            {
                var buffer = System.Text.Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConnectionManager] Error sending message: {ex.Message}");
            }
        }

        public int GetTotalConnections() => _connections.Count;
        
        public int GetTotalUsers() => _userConnections.Count;

        public void UpdateActivity(string connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                connection.LastActivityAt = DateTime.UtcNow;
            }
        }
    }
}
