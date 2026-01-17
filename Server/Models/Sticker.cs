using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChatServer.Models
{
    public class Sticker
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public string Code { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
    }
}
