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

        // TODO: Người 1 - Implement real auth (Register, Login with password hash, JWT token, etc.)
    }
}

