using System.Text.Json;
using ChatServer.Database;
using ChatServer.Models;
using MongoDB.Driver;

namespace ChatServer.WebSockets.Handlers
{
    public static class PinMessageHandlers
    {
        public static async Task<WsResponse> HandlePinAsync(
            WsMessage wsMessage,
            MongoDBContext db
        )
        {
            var payload = JsonSerializer.Deserialize<PinMessageEvent>(
                wsMessage.Payload.ToString()!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            )!;

            var existed = await db.PinnedMessages.Find(p =>
                p.ConversationId == payload.ConversationId &&
                p.MessageId == payload.MessageId
            ).FirstOrDefaultAsync();

            if (existed != null)
            {
                return new WsResponse
                {
                    Type = "error",
                    Payload = new { error = "Message already pinned" }
                };
            }

            await db.PinnedMessages.InsertOneAsync(new PinnedMessage
            {
                ConversationId = payload.ConversationId,
                MessageId = payload.MessageId,
                PinnedAt = DateTime.UtcNow
            });

            return new WsResponse
            {
                Type = "pin_updated",
                Payload = new
                {
                    messageId = payload.MessageId,
                    pinned = true
                }
            };
        }

        public static async Task<WsResponse> HandleUnpinAsync(
            WsMessage wsMessage,
            MongoDBContext db
        )
        {
            var payload = JsonSerializer.Deserialize<PinMessageEvent>(
                wsMessage.Payload.ToString()!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            )!;

            await db.PinnedMessages.DeleteOneAsync(p =>
                p.ConversationId == payload.ConversationId &&
                p.MessageId == payload.MessageId
            );

            return new WsResponse
            {
                Type = "pin_updated",
                Payload = new
                {
                    messageId = payload.MessageId,
                    pinned = false
                }
            };
        }
    }
}
