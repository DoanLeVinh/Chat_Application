using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ChatServer.Models.Presence
{
    public class ClientState
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("userId")]
        public string UserId { get; set; }

        [BsonElement("lastSequenceByConversation")]
        public Dictionary<string, long> LastSequenceByConversation { get; set; } = new();

        [BsonElement("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}