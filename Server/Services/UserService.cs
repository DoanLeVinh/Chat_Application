using ChatServer.Data;
using ChatServer.Database;
using ChatServer.Models;
using MongoDB.Driver;

namespace ChatServer.Services
{
    public class UserRepository : BaseRepository<User>
    {
        public UserRepository(MongoDBContext context) 
            : base(context.GetCollection<User>("users"))
        {
            // Tạo unique index cho email
            CreateIndexes();
        }

        private void CreateIndexes()
        {
            var emailIndexKeys = Builders<User>.IndexKeys.Ascending(u => u.Email);
            var emailIndexOptions = new CreateIndexOptions { Unique = true };
            var emailIndexModel = new CreateIndexModel<User>(emailIndexKeys, emailIndexOptions);
            
            _collection.Indexes.CreateOne(emailIndexModel);
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _collection.Find(u => u.Email == email).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<User>> SearchByDisplayNameAsync(string searchTerm)
        {
            var filter = Builders<User>.Filter.Regex(u => u.DisplayName, new MongoDB.Bson.BsonRegularExpression(searchTerm, "i"));
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task UpdateOnlineStatusAsync(string userId, bool isOnline)
        {
            var update = Builders<User>.Update
                .Set(u => u.IsOnline, isOnline)
                .Set(u => u.LastSeenAt, DateTime.UtcNow);
            
            var filter = Builders<User>.Filter.Eq("_id", userId);
            await _collection.UpdateOneAsync(filter, update);
        }
    }
}
