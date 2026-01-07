using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatServer.Models;
using MongoDB.Driver;

namespace ChatServer.Models
{
    public class PresenceService : IDisposable
    {
        private readonly ConnectionManager _connectionManager;
        private readonly IMongoCollection<Presence> _presenceCollection;
        private readonly IMongoCollection<User> _userCollection;
        private readonly Timer _heartbeatTimer;
        private readonly Timer _cleanupTimer;

        public PresenceService(
            ConnectionManager connectionManager,
            IMongoDatabase database)
        {
            _connectionManager = connectionManager;
            _presenceCollection = database.GetCollection<Presence>("presence");
            _userCollection = database.GetCollection<User>("users");

            // Timer cho heartbeat (mỗi 30 giây flush DB)
            _heartbeatTimer = new Timer(
                callback: FlushPresenceToDatabase,
                state: null,
                dueTime: TimeSpan.FromSeconds(10),
                period: TimeSpan.FromSeconds(30)
            );

            // Timer cho cleanup (mỗi phút)
            _cleanupTimer = new Timer(
                callback: _ => _connectionManager.CleanupInactiveConnections(),
                state: null,
                dueTime: TimeSpan.FromMinutes(1),
                period: TimeSpan.FromMinutes(1)
            );

            Console.WriteLine("[PresenceService] Đã khởi động với heartbeat mỗi 30 giây");
        }

        // 1. Cập nhật presence khi user connect
        public void UserConnected(string userId, string connectionId)
        {
            UpdateUserPresenceInDb(userId, "online", connectionId);
            BroadcastPresenceUpdate(userId, "online");
        }

        // 2. Cập nhật presence khi user disconnect
        public void UserDisconnected(string connectionId)
        {
            var state = _connectionManager.GetConnectionState(connectionId);
            if (state != null)
            {
                var userConnections = _connectionManager.GetUserConnections(state.UserId);
                var isStillOnline = userConnections.Count > 1 ||
                                    (userConnections.Count == 1 && userConnections[0] != connectionId);

                if (!isStillOnline)
                {
                    UpdateUserPresenceInDb(state.UserId, "offline", null);
                    BroadcastPresenceUpdate(state.UserId, "offline");
                }
            }
        }

        // 3. Xử lý heartbeat từ client
        public void HandleHeartbeat(string connectionId, string requestId)
        {
            if (_connectionManager.UpdateHeartbeat(connectionId))
            {
                SendHeartbeatAck(connectionId, requestId);
            }
        }

        // 4. Cập nhật status (online/away/offline)
        public void UpdateUserStatus(string userId, string status)
        {
            var connections = _connectionManager.GetUserConnections(userId);
            var connectionId = connections.FirstOrDefault();

            UpdateUserPresenceInDb(userId, status, connectionId);
            BroadcastPresenceUpdate(userId, status);
        }

        // 5. Lấy presence của user
        public async Task<Presence> GetUserPresence(string userId)
        {
            var filter = Builders<Presence>.Filter.Eq(p => p.UserId, userId);
            var presence = await _presenceCollection.Find(filter).FirstOrDefaultAsync();

            if (presence == null)
            {
                presence = new Presence
                {
                    UserId = userId,
                    Status = _connectionManager.IsUserOnline(userId) ? "online" : "offline",
                    LastSeen = DateTime.UtcNow
                };
            }
            else if (_connectionManager.IsUserOnline(userId) && presence.Status != "online")
            {
                presence.Status = "online";
                presence.LastSeen = DateTime.UtcNow;
                await _presenceCollection.ReplaceOneAsync(filter, presence);
            }

            return presence;
        }

        // PRIVATE METHODS

        private async void UpdateUserPresenceInDb(string userId, string status, string? connectionId)
        {
            try
            {
                var filter = Builders<Presence>.Filter.Eq(p => p.UserId, userId);
                var update = Builders<Presence>.Update
                    .Set(p => p.Status, status)
                    .Set(p => p.LastSeen, DateTime.UtcNow)
                    .Set(p => p.UpdatedAt, DateTime.UtcNow);

                if (connectionId != null)
                {
                    update = update.Set(p => p.ConnectionId, connectionId);
                }

                var options = new UpdateOptions { IsUpsert = true };

                await _presenceCollection.UpdateOneAsync(filter, update, options);

                Console.WriteLine($"[PresenceService] Updated {userId} status to {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PresenceService] Error updating presence: {ex.Message}");
            }
        }

        private async void FlushPresenceToDatabase(object? state)
        {
            try
            {
                var onlineUsers = _connectionManager.GetAllOnlineUsers();

                var updates = new List<WriteModel<Presence>>();
                var now = DateTime.UtcNow;

                foreach (var userId in onlineUsers)
                {
                    var filter = Builders<Presence>.Filter.Eq(p => p.UserId, userId);
                    var update = Builders<Presence>.Update
                        .Set(p => p.Status, "online")
                        .Set(p => p.LastSeen, now)
                        .Set(p => p.UpdatedAt, now);

                    updates.Add(new UpdateOneModel<Presence>(filter, update) { IsUpsert = true });
                }

                if (updates.Count > 0)
                {
                    await _presenceCollection.BulkWriteAsync(updates);
                    Console.WriteLine($"[PresenceService] Flushed {updates.Count} users to DB");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PresenceService] Error flushing to DB: {ex.Message}");
            }
        }

        private void BroadcastPresenceUpdate(string userId, string status)
        {
            var presenceEvent = new
            {
                type = "presence_updated",
                payload = new
                {
                    userId = userId,
                    status = status,
                    timestamp = DateTime.UtcNow
                }
            };

            var message = JsonSerializer.Serialize(presenceEvent);

            Console.WriteLine($"[PresenceService] Broadcasted {userId} status: {status}");
        }

        private void SendHeartbeatAck(string connectionId, string requestId)
        {
            var ack = new
            {
                type = "heartbeat_ack",
                requestId = requestId,
                timestamp = DateTime.UtcNow
            };

            var message = JsonSerializer.Serialize(ack);

            Console.WriteLine($"[PresenceService] Sent heartbeat ack to {connectionId}");
        }

        public void Dispose()
        {
            _heartbeatTimer?.Dispose();
            _cleanupTimer?.Dispose();
        }
    }
}