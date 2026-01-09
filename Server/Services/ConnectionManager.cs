using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ChatServer.Services
{
    public class ConnectionManager
    {
        private readonly ConcurrentDictionary<string, string> _connections = new();
        private readonly ConcurrentDictionary<string, Dictionary<string, long>> _lastSeenSeq = new();
        
        // 1. Thêm connection
        public void AddConnection(string connectionId, string userId)
        {
            Console.WriteLine($"[ConnectionManager] Add: {connectionId} for user: {userId}");
            _connections[connectionId] = userId;
        }
        
        // 2. Xóa connection
        public void RemoveConnection(string connectionId)
        {
            Console.WriteLine($"[ConnectionManager] Remove: {connectionId}");
            _connections.TryRemove(connectionId, out _);
            _lastSeenSeq.TryRemove(connectionId, out _);
        }
        
        // 3. Lấy user từ connection
        public string? GetUserId(string connectionId)
        {
            _connections.TryGetValue(connectionId, out var userId);
            return userId;
        }
        
        // 4. Kiểm tra user online
        public bool IsUserOnline(string userId)
        {
            foreach (var kvp in _connections)
            {
                if (kvp.Value == userId) return true;
            }
            return false;
        }
        
        // 5. Lấy tất cả connections của user
        public List<string> GetUserConnections(string userId)
        {
            var connections = new List<string>();
            foreach (var kvp in _connections)
            {
                if (kvp.Value == userId)
                    connections.Add(kvp.Key);
            }
            return connections;
        }
        
        // 6. Cập nhật last seen seq (THÊM)
        public void UpdateLastSeenSeq(string connectionId, string conversationId, long seq)
        {
            if (_lastSeenSeq.ContainsKey(connectionId))
            {
                _lastSeenSeq[connectionId][conversationId] = seq;
            }
            else
            {
                _lastSeenSeq[connectionId] = new Dictionary<string, long> { { conversationId, seq } };
            }
            Console.WriteLine($"[ConnectionManager] Updated seq: {connectionId} -> {conversationId}:{seq}");
        }
        
        // 7. Lấy last seen seq (THÊM)
        public long GetLastSeenSeq(string connectionId, string conversationId)
        {
            if (_lastSeenSeq.TryGetValue(connectionId, out var seqDict))
            {
                if (seqDict.TryGetValue(conversationId, out var seq))
                {
                    return seq;
                }
            }
            return 0;
        }
    }
}