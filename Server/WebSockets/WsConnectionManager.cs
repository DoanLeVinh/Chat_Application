using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ChatServer.WebSockets
{
    /// <summary>
    /// WebSocket Connection Manager - Người 1 sẽ implement base, Người 2/3/4 dùng
    /// </summary>
    public class WsConnectionManager
    {
        // userId -> List<connectionId>
        private readonly ConcurrentDictionary<string, List<string>> _userConnections = new();
        
        // connectionId -> WebSocket
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        
        // connectionId -> userId
        private readonly ConcurrentDictionary<string, string> _connectionUsers = new();

        public string AddConnection(WebSocket socket, string userId)
        {
            var connectionId = Guid.NewGuid().ToString();
            _connections[connectionId] = socket;
            _connectionUsers[connectionId] = userId;
            
            if (!_userConnections.ContainsKey(userId))
            {
                _userConnections[userId] = new List<string>();
            }
            _userConnections[userId].Add(connectionId);
            
            return connectionId;
        }

        public void RemoveConnection(string connectionId)
        {
            _connections.TryRemove(connectionId, out _);
            
            if (_connectionUsers.TryRemove(connectionId, out var userId))
            {
                if (_userConnections.TryGetValue(userId, out var connections))
                {
                    connections.Remove(connectionId);
                    if (connections.Count == 0)
                    {
                        _userConnections.TryRemove(userId, out _);
                    }
                }
            }
        }

        public async Task SendToConnectionAsync(string connectionId, object data)
        {
            if (_connections.TryGetValue(connectionId, out var socket))
            {
                if (socket.State == WebSocketState.Open)
                {
                    var json = JsonSerializer.Serialize(data);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None
                    );
                }
            }
        }

        public async Task BroadcastToUsersAsync(List<string> userIds, object data)
        {
            foreach (var userId in userIds)
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    continue;
                }
                if (_userConnections.TryGetValue(userId, out var connectionIds))
                {
                    foreach (var connId in connectionIds)
                    {
                        await SendToConnectionAsync(connId, data);
                    }
                }
            }
        }

        public string? GetUserIdByConnection(string connectionId)
        {
            _connectionUsers.TryGetValue(connectionId, out var userId);
            return userId;
        }

        public bool IsUserOnline(string userId)
        {
            return _userConnections.ContainsKey(userId) && _userConnections[userId].Count > 0;
        }

        /// <summary>
        /// Lấy danh sách tất cả userId đang có kết nối WebSocket
        /// </summary>
        public List<string> GetAllOnlineUserIds()
        {
            return _userConnections.Keys.ToList();
        }

        /// <summary>
        /// Broadcast đến tất cả users trừ một user cụ thể
        /// </summary>
        public async Task BroadcastToAllExceptAsync(string excludeUserId, object data)
        {
            var userIds = _userConnections.Keys.Where(id => id != excludeUserId).ToList();
            await BroadcastToUsersAsync(userIds, data);
        }

        /// <summary>
        /// Broadcast đến tất cả users đang online
        /// </summary>
        public async Task BroadcastToAllAsync(object data)
        {
            var userIds = _userConnections.Keys.ToList();
            await BroadcastToUsersAsync(userIds, data);
        }
    }
}
