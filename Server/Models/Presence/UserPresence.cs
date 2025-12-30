using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ChatServer.Models.Presence
{
    public enum PresenceStatus
    {
        Offline = 0,
        Online = 1,
        Away = 2,
        Busy = 3
    }

    public class UserPresence
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("userId")]
        public string UserId { get; set; }

        [BsonElement("status")]
        public PresenceStatus Status { get; set; } = PresenceStatus.Offline;

        [BsonElement("lastSeen")]
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;

        [BsonElement("lastHeartbeat")]
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

        [BsonElement("connectionId")]
        public string ConnectionId { get; set; }

        [BsonElement("deviceId")]
        public string DeviceId { get; set; }

        [BsonElement("conversationIds")]
        public List<string> ConversationIds { get; set; } = new List<string>();

        [BsonElement("customStatus")]
        public string CustomStatus { get; set; } = "";

        [BsonIgnore]
        public bool IsOnline => Status == PresenceStatus.Online;
    }
}