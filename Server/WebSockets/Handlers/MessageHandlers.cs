using System.Text.Json;
using ChatServer.Services;

namespace ChatServer.WebSockets.Handlers
{
    /// <summary>
    /// Message Event Handlers - Người 2
    /// Xử lý: send_message, get_messages
    /// </summary>
    public static class MessageHandlers
    {
        public static async Task<WsResponse> HandleSendMessageAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            MessageService messageService,
            WsConnectionManager connectionManager)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<SendMessagePayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (payload == null || string.IsNullOrEmpty(payload.ConversationId) || string.IsNullOrEmpty(payload.Content))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                // Validate: user phải là member
                var isMember = await conversationService.IsMemberAsync(payload.ConversationId, userId);
                if (!isMember)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Not a member of this conversation" } };
                }

                // Tạo message (với seq tăng dần)
                var newMessage = await messageService.CreateMessageAsync(
                    payload.ConversationId,
                    userId,
                    payload.Content,
                    payload.MessageType ?? "text",
                    payload.ClientMessageId ?? Guid.NewGuid().ToString()
                );

                var messageCreated = new
                {
                    messageId = newMessage.Id,
                    conversationId = newMessage.ConversationId,
                    senderId = newMessage.SenderId,
                    messageType = newMessage.Type,
                    content = newMessage.Content,
                    seq = newMessage.Seq,
                    createdAt = newMessage.CreatedAt
                };

                // Broadcast message_created tới tất cả members
                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var memberUserIds = members.Select(m => m.UserId).ToList();
                await connectionManager.BroadcastToUsersAsync(memberUserIds, new WsResponse
                {
                    Type = "message_created",
                    Payload = messageCreated
                });

                return new WsResponse
                {
                    Type = "send_message_ok",
                    RequestId = message.RequestId,
                    Payload = new { messageId = newMessage.Id, seq = newMessage.Seq }
                };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleGetMessagesAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            MessageService messageService,
            UserService userService)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<GetMessagesPayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (payload == null || string.IsNullOrEmpty(payload.ConversationId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                // Validate: user phải là member
                var isMember = await conversationService.IsMemberAsync(payload.ConversationId, userId);
                if (!isMember)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Not a member of this conversation" } };
                }

                // Lấy messages
                var messages = await messageService.GetMessagesAsync(payload.ConversationId, payload.Limit ?? 50, payload.BeforeSeq);

                // Lấy thông tin user cho tất cả senderIds
                var senderIds = messages.Select(m => m.SenderId).Distinct().ToList();
                var senderNames = new Dictionary<string, string>();
                foreach (var senderId in senderIds)
                {
                    var user = await userService.GetUserByIdAsync(senderId);
                    senderNames[senderId] = user?.DisplayName ?? senderId;
                }

                return new WsResponse
                {
                    Type = "messages",
                    RequestId = message.RequestId,
                    Payload = new
                    {
                        conversationId = payload.ConversationId,
                        messages = messages.Select(m => new
                        {
                            messageId = m.Id,
                            senderId = m.SenderId,
                            senderDisplayName = senderNames.GetValueOrDefault(m.SenderId, m.SenderId),
                            messageType = m.Type,
                            content = m.Content,
                            fileUrl = m.FileUrl,
                            seq = m.Seq,
                            createdAt = m.CreatedAt
                        }).ToList()
                    }
                };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }
    }

    public class SendMessagePayload
    {
        public string ConversationId { get; set; } = "";
        public string Content { get; set; } = "";
        public string? MessageType { get; set; }
        public string? ClientMessageId { get; set; }
    }

    public class GetMessagesPayload
    {
        public string ConversationId { get; set; } = "";
        public int? Limit { get; set; }
        public long? BeforeSeq { get; set; }
    }
}
