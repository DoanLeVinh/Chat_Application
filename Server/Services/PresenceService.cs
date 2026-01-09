using System;
using System.Threading.Tasks;
using ChatServer.Models;
using ChatServer.Database;
using MongoDB.Driver;

namespace ChatServer.Services
{
    public class PresenceService
    {
        private readonly ConnectionManager _connectionManager;
        private readonly MongoDBContext _dbContext;
        
        public PresenceService(ConnectionManager connectionManager, MongoDBContext dbContext)
        {
            _connectionManager = connectionManager;
            _dbContext = dbContext;
            Console.WriteLine("[PresenceService] Initialized");
        }
        
        public async Task UserConnected(string userId, string connectionId)
        {
            try
            {
                _connectionManager.AddConnection(connectionId, userId);
                await UpdatePresenceInDb(userId, "online", connectionId);
                Console.WriteLine($"[Presence] User {userId} connected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presence] UserConnected error: {ex.Message}");
            }
        }
        
        public async Task UserDisconnected(string connectionId)
        {
            try
            {
                var userId = _connectionManager.GetUserId(connectionId);
                if (userId == null) return;
                
                _connectionManager.RemoveConnection(connectionId);
                
                var userConnections = _connectionManager.GetUserConnections(userId);
                if (userConnections.Count == 0)
                {
                    await UpdatePresenceInDb(userId, "offline", (string?)null);
                }
                
                Console.WriteLine($"[Presence] User {userId} disconnected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presence] UserDisconnected error: {ex.Message}");
            }
        }
        
        public void HandleHeartbeat(string connectionId)
        {
            Console.WriteLine($"[Presence] Heartbeat from {connectionId}");
        }
        
        public async Task UpdateStatus(string userId, string status)
        {
            try
            {
                await UpdatePresenceInDb(userId, status, (string?)null);
                Console.WriteLine($"[Presence] User {userId} status: {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presence] UpdateStatus error: {ex.Message}");
            }
        }
        
        public async Task<Presence> GetUserPresence(string userId)
        {
            try
            {
                var filter = Builders<Presence>.Filter.Eq(p => p.UserId, userId);
                var presence = await _dbContext.Presence.Find(filter).FirstOrDefaultAsync();
                
                if (presence == null)
                {
                    return new Presence 
                    { 
                        UserId = userId, 
                        Status = _connectionManager.IsUserOnline(userId) ? "online" : "offline",
                        LastSeen = DateTime.UtcNow 
                    };
                }
                
                return presence;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presence] GetUserPresence error: {ex.Message}");
                return new Presence { UserId = userId, Status = "unknown" };
            }
        }
        
        private async Task UpdatePresenceInDb(string userId, string status, string? connectionId)
        {
            try
            {
                var filter = Builders<Presence>.Filter.Eq(p => p.UserId, userId);
                var update = Builders<Presence>.Update
                    .Set(p => p.Status, status)
                    .Set(p => p.LastSeen, DateTime.UtcNow)
                    .Set(p => p.UpdatedAt, DateTime.UtcNow)
                    .Set(p => p.ConnectionId, connectionId);
                    
                var options = new UpdateOptions { IsUpsert = true };
                await _dbContext.Presence.UpdateOneAsync(filter, update, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presence] UpdatePresenceInDb error: {ex.Message}");
            }
        }
    }
}