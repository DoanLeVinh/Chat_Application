using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChatServer.Models
{
    public class MessageReaction
    {
        [BsonElement("messageId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string MessageId { get; set; } = string.Empty;

        [BsonElement("userId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;

        [BsonElement("emoji")]
        public string Emoji { get; set; } = string.Empty;
    }
}
