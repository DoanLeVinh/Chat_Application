using System.Text.Json;
using ChatServer.Database;
using ChatServer.Models;
using ChatServer.Services;
using MongoDB.Driver;

namespace ChatServer.WebSockets.Handlers
{
    public static class ReactionHandlers
    {
        public static async Task<WsResponse> HandleAddReactionAsync(
            WsMessage message,
            string userId,
            MongoDBContext db,
            ConversationService conversationService,
            WsConnectionManager connectionManager)
        {
            var payload = JsonSerializer.Deserialize<AddReactionPayload>(
                message.Payload.ToString() ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (payload == null)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
            }

            if (string.IsNullOrWhiteSpace(payload.ConversationId) || string.IsNullOrWhiteSpace(payload.MessageId) || string.IsNullOrWhiteSpace(payload.Emoji))
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
            }

            // 1. Check user là member
            var isMember = await conversationService.IsMemberAsync(payload.ConversationId, userId);
            if (!isMember)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Not a member" } };
            }

            // 2. Insert reaction (ignore nếu trùng)
            var inserted = false;
            try
            {
                await db.MessageReactions.InsertOneAsync(new MessageReaction
                {
                    MessageId = payload.MessageId,
                    UserId = userId,
                    Emoji = payload.Emoji
                });

                inserted = true;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Duplicate: user already reacted with same emoji -> no state change
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }

            // 3. Broadcast cho các member (only if state actually changed)
            if (inserted)
            {
                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var userIds = members.Select(m => m.UserId).ToList();

                await connectionManager.BroadcastToUsersAsync(userIds, new WsResponse
                {
                    Type = "reaction_updated",
                    Payload = new
                    {
                        conversationId = payload.ConversationId,
                        messageId = payload.MessageId,
                        emoji = payload.Emoji,
                        userId
                    }
                });
            }

            return new WsResponse { Type = "add_reaction_ok", RequestId = message.RequestId };
        }

    }

    public class AddReactionPayload
    {
        public string ConversationId { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string Emoji { get; set; } = "";
    }
}
