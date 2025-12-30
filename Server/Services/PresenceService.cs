using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using ChatServer.Models.Presence;

namespace ChatServer.Services
{
    public class PresenceService
    {
        private readonly IMongoCollection<UserPresence> _presenceCollection;
        private readonly IMongoCollection<UserSession> _sessionCollection;
        private readonly ConcurrentDictionary<string, Timer> _heartbeatTimers;
        private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeatTimes;
        private readonly int _heartbeatInterval = 15; // 15 giây
        private readonly ConnectionManager _connectionManager;
        private readonly ILogger<PresenceService> _logger;

        public PresenceService(
            IConfiguration configuration, 
            ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
            _logger = new Logger<PresenceService>(new LoggerFactory());
            
            // Kết nối MongoDB Atlas
            var connectionString = configuration.GetConnectionString("MongoDB");
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase("ChatAppDB");
            
            _presenceCollection = database.GetCollection<UserPresence>("user_presence");
            _sessionCollection = database.GetCollection<UserSession>("user_sessions");
            
            _heartbeatTimers = new ConcurrentDictionary<string, Timer>();
            _lastHeartbeatTimes = new ConcurrentDictionary<string, DateTime>();
            
            CreateIndexes();
            Console.WriteLine("✅ PresenceService initialized");
        }
        
        private void CreateIndexes()
        {
            try
            {
                // Tạo index cho performance
                var presenceIndex = Builders<UserPresence>.IndexKeys
                    .Ascending(p => p.UserId)
                    .Ascending(p => p.Status);
                _presenceCollection.Indexes.CreateOne(
                    new CreateIndexModel<UserPresence>(presenceIndex));
                
                var sessionIndex = Builders<UserSession>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Ascending(s => s.IsActive);
                _sessionCollection.Indexes.CreateOne(
                    new CreateIndexModel<UserSession>(sessionIndex));
                    
                Console.WriteLine("✅ Indexes created for Presence collections");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating indexes: {ex.Message}");
            }
        }
        
        public async Task<UserSession> UserConnectedAsync(
            string userId, 
            string connectionId, 
            string deviceId, 
            string ipAddress)
        {
            try
            {
                Console.WriteLine($"👤 User connecting: {userId}, Connection: {connectionId}");
                
                // Tạo session mới
                var session = new UserSession
                {
                    UserId = userId,
                    SessionId = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    DeviceId = deviceId,
                    IpAddress = ipAddress,
                    ConnectedAt = DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow,
                    IsActive = true,
                    ResumeToken = GenerateResumeToken()
                };
                
                await _sessionCollection.InsertOneAsync(session);
                Console.WriteLine($"✅ Session created: {session.SessionId}");
                
                // Cập nhật presence
                var filter = Builders<UserPresence>.Filter.Eq(p => p.UserId, userId);
                var update = Builders<UserPresence>.Update
                    .Set(p => p.Status, PresenceStatus.Online)
                    .Set(p => p.LastSeen, DateTime.UtcNow)
                    .Set(p => p.LastHeartbeat, DateTime.UtcNow)
                    .Set(p => p.ConnectionId, connectionId)
                    .Set(p => p.DeviceId, deviceId)
                    .SetOnInsert(p => p.Id, ObjectId.GenerateNewId().ToString());
                
                await _presenceCollection.UpdateOneAsync(
                    filter, 
                    update, 
                    new UpdateOptions { IsUpsert = true });
                
                Console.WriteLine($"✅ Presence updated to Online for user: {userId}");
                
                // Bắt đầu heartbeat
                StartHeartbeat(userId, connectionId);
                
                // Broadcast presence update
                await BroadcastPresenceUpdateAsync(userId, PresenceStatus.Online);
                
                return session;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in UserConnectedAsync: {ex.Message}");
                throw;
            }
        }
        
        public async Task UserDisconnectedAsync(string connectionId)
        {
            try
            {
                Console.WriteLine($"🔌 User disconnecting: {connectionId}");
                
                // Tìm session theo connectionId
                var sessionFilter = Builders<UserSession>.Filter.Eq(s => s.ConnectionId, connectionId);
                var sessionUpdate = Builders<UserSession>.Update
                    .Set(s => s.IsActive, false)
                    .Set(s => s.DisconnectedAt, DateTime.UtcNow);
                
                await _sessionCollection.UpdateOneAsync(sessionFilter, sessionUpdate);
                
                // Tìm presence
                var presenceFilter = Builders<UserPresence>.Filter.Eq(p => p.ConnectionId, connectionId);
                var presence = await _presenceCollection.Find(presenceFilter).FirstOrDefaultAsync();
                
                if (presence != null)
                {
                    // Kiểm tra nếu user còn session nào active khác không
                    var activeSessionCount = await _sessionCollection
                        .CountDocumentsAsync(s => s.UserId == presence.UserId && s.IsActive == true);
                    
                    if (activeSessionCount == 0)
                    {
                        // Không còn session nào active -> chuyển sang offline
                        var update = Builders<UserPresence>.Update
                            .Set(p => p.Status, PresenceStatus.Offline)
                            .Set(p => p.LastSeen, DateTime.UtcNow);
                        
                        await _presenceCollection.UpdateOneAsync(presenceFilter, update);
                        
                        Console.WriteLine($"✅ User {presence.UserId} marked as Offline");
                        
                        // Broadcast
                        await BroadcastPresenceUpdateAsync(presence.UserId, PresenceStatus.Offline);
                    }
                    else
                    {
                        Console.WriteLine($"ℹ️ User {presence.UserId} still has other active sessions: {activeSessionCount}");
                    }
                    
                    // Dừng heartbeat
                    StopHeartbeat(presence.UserId);
                }
                
                Console.WriteLine($"✅ User disconnected cleanup completed for connection: {connectionId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in UserDisconnectedAsync: {ex.Message}");
            }
        }
        
        private void StartHeartbeat(string userId, string connectionId)
        {
            StopHeartbeat(userId); // Dừng timer cũ nếu có
            
            Console.WriteLine($"💓 Starting heartbeat for user: {userId}");
            
            var timer = new Timer(async state =>
            {
                try
                {
                    var now = DateTime.UtcNow;
                    _lastHeartbeatTimes[userId] = now;
                    
                    // Cập nhật heartbeat time
                    var filter = Builders<UserPresence>.Filter.Eq(p => p.UserId, userId);
                    var update = Builders<UserPresence>.Update
                        .Set(p => p.LastHeartbeat, now);
                    
                    var result = await _presenceCollection.UpdateOneAsync(filter, update);
                    
                    // Kiểm tra connection còn active không
                    if (!_connectionManager.IsConnectionActive(connectionId))
                    {
                        Console.WriteLine($"⚠️ Heartbeat detected inactive connection for user {userId}");
                        await UserDisconnectedAsync(connectionId);
                        StopHeartbeat(userId);
                        return;
                    }
                    
                    Console.WriteLine($"✅ Heartbeat updated for user {userId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Heartbeat error for user {userId}: {ex.Message}");
                }
            }, null, TimeSpan.FromSeconds(_heartbeatInterval), TimeSpan.FromSeconds(_heartbeatInterval));
            
            _heartbeatTimers[userId] = timer;
        }
        
        private void StopHeartbeat(string userId)
        {
            if (_heartbeatTimers.TryRemove(userId, out var timer))
            {
                timer?.Dispose();
                Console.WriteLine($"🛑 Heartbeat stopped for user: {userId}");
            }
            _lastHeartbeatTimes.TryRemove(userId, out _);
        }
        
        private async Task BroadcastPresenceUpdateAsync(string userId, PresenceStatus status)
        {
            try
            {
                // Lấy danh sách conversations của user
                var presence = await GetUserPresenceAsync(userId);
                if (presence?.ConversationIds == null) 
                {
                    Console.WriteLine($"ℹ️ No conversations to broadcast for user: {userId}");
                    return;
                }
                
                var updateEvent = new
                {
                    type = "presence_update",
                    data = new
                    {
                        userId,
                        status = status.ToString().ToLower(),
                        lastSeen = DateTime.UtcNow,
                        customStatus = presence.CustomStatus
                    }
                };
                
                // Broadcast đến tất cả conversations
                foreach (var conversationId in presence.ConversationIds)
                {
                    await _connectionManager.BroadcastToConversationAsync(
                        conversationId, 
                        updateEvent, 
                        excludeUserId: userId);
                }
                
                Console.WriteLine($"📢 Broadcasted presence update for user {userId}: {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error broadcasting presence update: {ex.Message}");
            }
        }
        
        public async Task<UserPresence> GetUserPresenceAsync(string userId)
        {
            return await _presenceCollection
                .Find(p => p.UserId == userId)
                .FirstOrDefaultAsync();
        }
        
        public async Task UpdateUserPresenceAsync(string userId, PresenceStatus status, string customStatus = null)
        {
            var filter = Builders<UserPresence>.Filter.Eq(p => p.UserId, userId);
            var update = Builders<UserPresence>.Update
                .Set(p => p.Status, status)
                .Set(p => p.LastSeen, DateTime.UtcNow);
            
            if (customStatus != null)
            {
                update = update.Set(p => p.CustomStatus, customStatus);
            }
            
            await _presenceCollection.UpdateOneAsync(filter, update);
            await BroadcastPresenceUpdateAsync(userId, status);
            
            Console.WriteLine($"✅ Presence updated for user {userId}: {status}");
        }
        
        public async Task UpdateConversationsAsync(string userId, List<string> conversationIds)
        {
            var filter = Builders<UserPresence>.Filter.Eq(p => p.UserId, userId);
            var update = Builders<UserPresence>.Update
                .Set(p => p.ConversationIds, conversationIds);
            
            await _presenceCollection.UpdateOneAsync(filter, update);
            Console.WriteLine($"✅ Conversations updated for user {userId}: {conversationIds.Count} conversations");
        }
        
        private string GenerateResumeToken()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("=", "")
                .Replace("+", "-")
                .Replace("/", "_");
        }
        
        public async Task<bool> ValidateResumeToken(string userId, string resumeToken)
        {
            var session = await _sessionCollection
                .Find(s => s.UserId == userId && s.ResumeToken == resumeToken && s.IsActive)
                .FirstOrDefaultAsync();
                
            return session != null;
        }
        
        public async Task<string> GetResumeToken(string sessionId)
        {
            var session = await _sessionCollection
                .Find(s => s.SessionId == sessionId)
                .FirstOrDefaultAsync();
                
            return session?.ResumeToken;
        }
        
        public async Task CleanupAllPresence()
        {
            try
            {
                Console.WriteLine("🧹 Cleaning up all presence data...");
                
                // Dừng tất cả timers
                foreach (var timer in _heartbeatTimers.Values)
                {
                    timer?.Dispose();
                }
                _heartbeatTimers.Clear();
                
                // Đánh dấu tất cả sessions là inactive
                var filter = Builders<UserSession>.Filter.Eq(s => s.IsActive, true);
                var update = Builders<UserSession>.Update
                    .Set(s => s.IsActive, false)
                    .Set(s => s.DisconnectedAt, DateTime.UtcNow);
                
                await _sessionCollection.UpdateManyAsync(filter, update);
                
                // Set tất cả users thành offline
                var presenceFilter = Builders<UserPresence>.Filter.Eq(p => p.Status, PresenceStatus.Online);
                var presenceUpdate = Builders<UserPresence>.Update
                    .Set(p => p.Status, PresenceStatus.Offline);
                
                await _presenceCollection.UpdateManyAsync(presenceFilter, presenceUpdate);
                
                Console.WriteLine("✅ All presence data cleaned up");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during presence cleanup: {ex.Message}");
            }
        }
    }
}