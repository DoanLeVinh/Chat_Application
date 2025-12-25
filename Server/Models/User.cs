using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChatServer.Models
{
    /// <summary>
    /// User/Profile model (Người 1 sẽ làm Auth, Người 2 chỉ dùng)
    /// </summary>
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;

        [BsonElement("passwordHash")]
        public string PasswordHash { get; set; } = string.Empty; // TODO: Người 1 - Auth

        [BsonElement("avatarUrl")]
        public string? AvatarUrl { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // TODO: Người 3 - Presence (isOnline, lastSeenAt) sẽ ở bảng riêng
    }
}
