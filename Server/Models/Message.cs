using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;


namespace ChatServer.Models
{
    /// <summary>
    /// Message model với seq tăng dần theo conversation (Người 2)
    /// </summary>
    public class Message
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("conversationId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ConversationId { get; set; } = string.Empty;

        [BsonElement("senderId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SenderId { get; set; } = string.Empty;

        [BsonElement("type")]
        public string Type { get; set; } = "text"; // text | sticker

        [BsonElement("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Client message ID để chống gửi trùng khi reconnect
        /// </summary>
        [BsonElement("clientMessageId")]
        public string ClientMessageId { get; set; } = string.Empty;

        /// <summary>
        /// Sequence number tăng dần theo conversation (QUAN TRỌNG cho resume - Người 2)
        /// </summary>
        [BsonElement("seq")]
        public long Seq { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // TODO: Người 4 - Reactions, Pinned sẽ ở bảng riêng
    }
}
