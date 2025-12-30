using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ChatServer.Models.Presence
{
    public class UserSession
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("userId")]
        public string UserId { get; set; }

        [BsonElement("sessionId")]
        public string SessionId { get; set; }

        [BsonElement("connectionId")]
        public string ConnectionId { get; set; }

        [BsonElement("deviceId")]
        public string DeviceId { get; set; }

        [BsonElement("ipAddress")]
        public string IpAddress { get; set; }

        [BsonElement("connectedAt")]
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("disconnectedAt")]
        public DateTime? DisconnectedAt { get; set; }

        [BsonElement("lastActivity")]
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;

        [BsonElement("isActive")]
        public bool IsActive { get; set; } = true;

        [BsonElement("resumeToken")]
        public string ResumeToken { get; set; }

        [BsonElement("lastSequenceByConversation")]
        public Dictionary<string, long> LastSequenceByConversation { get; set; } = new();
    }
}