using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChatServer.Models
{
    public class ConversationInvite
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("conversationId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ConversationId { get; set; } = string.Empty;

        [BsonElement("invitedUserId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string InvitedUserId { get; set; } = string.Empty;

        [BsonElement("invitedByUserId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string InvitedByUserId { get; set; } = string.Empty;

        // pending | approved | rejected
        [BsonElement("status")]
        public string Status { get; set; } = "pending";

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("decidedAt")]
        [BsonIgnoreIfNull]
        public DateTime? DecidedAt { get; set; }

        [BsonElement("decidedByUserId")]
        [BsonIgnoreIfNull]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? DecidedByUserId { get; set; }
    }
}
