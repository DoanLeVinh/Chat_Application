using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ChatServer.Models
{
    public class Presence
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        
        public string? UserId { get; set; }
        public string Status { get; set; } = "offline"; // online, away, offline
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public string? ConnectionId { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}