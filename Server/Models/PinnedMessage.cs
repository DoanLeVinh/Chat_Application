using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChatServer.Models
{
    public class PinnedMessage
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string ConversationId { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public DateTime PinnedAt { get; set; }
    }
}
