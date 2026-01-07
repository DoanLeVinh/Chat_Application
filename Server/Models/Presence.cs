using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ChatServer.Models
{
    public class Presence
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty; // Giá trị mặc định
 
        [BsonElement("userId")]
        public string UserId { get; set; } = string.Empty; // Giá trị mặc định
 
        [BsonElement("status")]
        public string Status { get; set; } = "offline"; // Giá trị mặc định
 
        [BsonElement("lastSeen")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime LastSeen { get; set; } = DateTime.UtcNow; // Giá trị mặc định
 
        [BsonElement("connectionId")]
        public string ConnectionId { get; set; } = string.Empty; // Giá trị mặc định
 
        [BsonElement("updatedAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; // Giá trị mặc định
    }
 
    // Model cho presence update event
    public class PresenceUpdateEvent
    {
        public string UserId { get; set; } = string.Empty; // Giá trị mặc định
        public string Status { get; set; } = string.Empty; // Giá trị mặc định
        public DateTime Timestamp { get; set; } = DateTime.UtcNow; // Giá trị mặc định
    }
}

