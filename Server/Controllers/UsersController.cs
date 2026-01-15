using ChatServer.Models;
using ChatServer.Database;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;

namespace ChatServer.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IMongoCollection<User> _users;

        public UsersController(MongoDBContext db)
        {
            _users = db.Users;
        }

        // GET /api/users/search?q=...&limit=...
        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers(
            [FromQuery] string? q,
            [FromQuery] int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Ok(new List<object>());
            }

            var filter = Builders<User>.Filter.Or(
                Builders<User>.Filter.Regex(
                    u => u.DisplayName,
                    new BsonRegularExpression(q, "i")
                ),
                Builders<User>.Filter.Regex(
                    u => u.Email,
                    new BsonRegularExpression(q, "i")
                )
            );

            var users = await _users
                .Find(filter)
                .Limit(limit)
                .Project(u => new
                {
                    id = u.Id,
                    displayName = u.DisplayName,
                    avatarUrl = u.AvatarUrl,
                    isOnline = u.IsOnline
                })
                .ToListAsync();

            return Ok(users);
        }
    }
}
