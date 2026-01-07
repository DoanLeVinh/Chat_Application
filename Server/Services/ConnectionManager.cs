using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ChatServer.Models
{
    public class ConnectionManager
    {
        // ConcurrentDictionary để thread-safe
        private readonly ConcurrentDictionary<string, ConnectionState> _connections
            = new ConcurrentDictionary<string, ConnectionState>();

        private readonly ConcurrentDictionary<string, List<string>> _userConnections
            = new ConcurrentDictionary<string, List<string>>();

        private readonly object _cleanupLock = new object();
        private DateTime _lastCleanup = DateTime.UtcNow;

        // 1. Thêm connection mới
        public void AddConnection(string connectionId, string userId)
        {
            Console.WriteLine($"[ConnectionManager] Adding connection: {connectionId} for user: {userId}");

            var state = new ConnectionState
            {
                ConnectionId = connectionId,
                UserId = userId,
                LastHeartbeat = DateTime.UtcNow
            };

            // Thêm vào dictionary connections
            _connections[connectionId] = state;

            // Thêm vào userConnections (1 user có thể nhiều connection)
            _userConnections.AddOrUpdate(
                userId,
                new List<string> { connectionId },
                (key, existingList) =>
                {
                    if (!existingList.Contains(connectionId))
                    {
                        existingList.Add(connectionId);
                    }
                    return existingList;
                }
            );

            Console.WriteLine($"[ConnectionManager] Total connections: {_connections.Count}, Users online: {_userConnections.Count}");
        }

        // 2. Xóa connection
        public void RemoveConnection(string connectionId)
        {
            Console.WriteLine($"[ConnectionManager] Removing connection: {connectionId}");

            if (_connections.TryRemove(connectionId, out var state))
            {
                // Xóa khỏi userConnections
                if (_userConnections.TryGetValue(state.UserId, out var connections))
                {
                    connections.Remove(connectionId);

                    // Nếu user không còn connection nào
                    if (connections.Count == 0)
                    {
                        _userConnections.TryRemove(state.UserId, out _);
                    }
                }
            }
        }

        // 3. Cập nhật heartbeat
        public bool UpdateHeartbeat(string connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var state))
            {
                state.LastHeartbeat = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        // 4. Lấy connection state
        public ConnectionState GetConnectionState(string connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var state))
            {
                return state;
            }
            throw new KeyNotFoundException($"ConnectionId {connectionId} không tồn tại.");
        }

        // 5. Lấy tất cả connection của user
        public List<string> GetUserConnections(string userId)
        {
            if (_userConnections.TryGetValue(userId, out var connections))
            {
                return new List<string>(connections); // Return copy
            }
            return new List<string>();
        }

        // 6. Kiểm tra user có online không
        public bool IsUserOnline(string userId)
        {
            return _userConnections.ContainsKey(userId) &&
                   _userConnections[userId].Count > 0;
        }

        // 7. Cập nhật last seen seq
        public void UpdateLastSeenSeq(string connectionId, string conversationId, long seq)
        {
            if (_connections.TryGetValue(connectionId, out var state))
            {
                state.LastSeenSeq[conversationId] = seq;
            }
        }

        // 8. Lấy last seen seq
        public long GetLastSeenSeq(string connectionId, string conversationId)
        {
            if (_connections.TryGetValue(connectionId, out var state))
            {
                if (state.LastSeenSeq.TryGetValue(conversationId, out var seq))
                {
                    return seq;
                }
            }
            return 0;
        }

        // 9. Lấy tất cả user đang online
        public List<string> GetAllOnlineUsers()
        {
            return _userConnections.Keys.ToList();
        }

        // 10. Dọn dẹp connection không hoạt động
        public List<string> CleanupInactiveConnections(int timeoutSeconds = 30)
        {
            lock (_cleanupLock)
            {
                // Chỉ cleanup mỗi 30 giây
                if ((DateTime.UtcNow - _lastCleanup).TotalSeconds < 30)
                    return new List<string>();

                _lastCleanup = DateTime.UtcNow;

                var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);
                var inactiveConnections = new List<string>();

                foreach (var kvp in _connections)
                {
                    if (kvp.Value.LastHeartbeat < cutoff)
                    {
                        inactiveConnections.Add(kvp.Key);
                    }
                }

                // Xóa tất cả inactive connections
                foreach (var connId in inactiveConnections)
                {
                    RemoveConnection(connId);
                }

                if (inactiveConnections.Count > 0)
                {
                    Console.WriteLine($"[ConnectionManager] Cleaned up {inactiveConnections.Count} inactive connections");
                }

                return inactiveConnections;
            }
        }

        // 11. Get all connections (for debugging)
        public Dictionary<string, ConnectionState> GetAllConnections()
        {
            return _connections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}