using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChatServer.Models
{
    /// <summary>
    /// Conversation model (đổi tên từ ChatRoom cho chuẩn với spec) - Người 2
    /// </summary>
    public class Conversation
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("type")]
        public string Type { get; set; } = "direct"; // direct | group

        [BsonElement("title")]
        public string? Title { get; set; } // Chỉ dùng cho group

        [BsonElement("avatarUrl")]
        public string? AvatarUrl { get; set; }

        [BsonElement("createdBy")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// Direct key để tránh tạo trùng conversation 1-1 (QUAN TRỌNG - Người 2)
        /// Format: min(userA,userB):max(userA,userB)
        /// </summary>
        [BsonElement("directKey")]
        public string? DirectKey { get; set; } // Unique cho direct chat

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Sequence counter để tạo seq cho messages
        /// </summary>
        [BsonElement("lastSeq")]
        public long LastSeq { get; set; } = 0;
    }

    /// <summary>
    /// Thành viên của conversation - Người 2
    /// </summary>
    public class ConversationMember
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("conversationId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ConversationId { get; set; } = string.Empty;

        [BsonElement("userId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;

        [BsonElement("role")]
        public string Role { get; set; } = "member"; // owner | admin | member

        [BsonElement("joinedAt")]
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
