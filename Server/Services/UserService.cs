using ChatServer.Database;
using ChatServer.Models;
using MongoDB.Driver;

namespace ChatServer.Services
{
    public class UserService
    {
        private readonly MongoDBContext _context;

        public UserService(MongoDBContext context)
        {
            _context = context;
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            try
            {
                var filter = Builders<User>.Filter.Eq(u => u.Id, userId);
                return await _context.Users.Find(filter).FirstOrDefaultAsync();
            }
            catch
            {
                return null;
            }
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            try
            {
                var filter = Builders<User>.Filter.Eq(u => u.Email, email);
                return await _context.Users.Find(filter).FirstOrDefaultAsync();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Cập nhật trạng thái online/offline của user
        /// </summary>
        public async Task SetOnlineStatusAsync(string userId, bool isOnline)
        {
            try
            {
                var filter = Builders<User>.Filter.Eq(u => u.Id, userId);
                var update = Builders<User>.Update
                    .Set(u => u.IsOnline, isOnline)
                    .Set(u => u.LastSeenAt, DateTime.UtcNow);
                
                await _context.Users.UpdateOneAsync(filter, update);
                Console.WriteLine($"📊 User {userId} status: {(isOnline ? "Online" : "Offline")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error updating online status: {ex.Message}");
            }
        }

        /// <summary>
        /// Lấy danh sách users online từ list userIds
        /// </summary>
        public async Task<List<User>> GetOnlineUsersAsync(List<string> userIds)
        {
            try
            {
                var filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.In(u => u.Id, userIds),
                    Builders<User>.Filter.Eq(u => u.IsOnline, true)
                );
                return await _context.Users.Find(filter).ToListAsync();
            }
            catch
            {
                return new List<User>();
            }
        }

        /// <summary>
        /// Lấy thông tin trạng thái online của nhiều users
        /// </summary>
        public async Task<Dictionary<string, UserStatusInfo>> GetUsersStatusAsync(List<string> userIds)
        {
            try
            {
                var filter = Builders<User>.Filter.In(u => u.Id, userIds);
                var users = await _context.Users.Find(filter).ToListAsync();
                
                return users.ToDictionary(
                    u => u.Id,
                    u => new UserStatusInfo
                    {
                        UserId = u.Id,
                        DisplayName = u.DisplayName,
                        IsOnline = u.IsOnline,
                        LastSeenAt = u.LastSeenAt
                    }
                );
            }
            catch
            {
                return new Dictionary<string, UserStatusInfo>();
            }
        }

        // TODO: Người 1 - Implement real auth (Register, Login with password hash, JWT token, etc.)
    }

    public class UserStatusInfo
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public DateTime? LastSeenAt { get; set; }
    }
}

