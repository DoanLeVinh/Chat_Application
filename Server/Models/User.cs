using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChatServer.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;

        [BsonElement("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [BsonElement("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [BsonElement("avatar_url")]
        public string? AvatarUrl { get; set; }

        [BsonElement("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("is_online")]
        public bool IsOnline { get; set; } = false;

        [BsonElement("last_seen_at")]
        public DateTime? LastSeenAt { get; set; }
    }
}
