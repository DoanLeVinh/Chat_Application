using System.Text.Json;
using ChatServer.Services;
using ChatServer.WebSockets;

namespace ChatServer.WebSockets.Handlers
{
    public static class PresenceResumeHandlers
    {
        // Xử lý heartbeat
        public static async Task<WsResponse> HandleHeartbeatAsync(
            WsMessage message,
            string userId,
            string connectionId,
            PresenceService presenceService,
            PresenceResumeManager presenceManager)
        {
            try
            {
                await presenceService.HandleHeartbeatAsync(userId, connectionId);
                await presenceManager.UpdateHeartbeat(connectionId);
                
                return new WsResponse
                {
                    Type = "heartbeat_ack",
                    RequestId = message.RequestId,
                    Payload = new { timestamp = DateTime.UtcNow }
                };
            }
            catch (Exception ex)
            {
                return new WsResponse
                {
                    Type = "error",
                    RequestId = message.RequestId,
                    Payload = new { error = ex.Message }
                };
            }
        }

        // Xử lý resume
        public static async Task<WsResponse> HandleResumeAsync(
            WsMessage message,
            string userId,
            string connectionId,
            ResumeService resumeService,
            PresenceService presenceService,
            PresenceResumeManager presenceManager,
            ConversationService conversationService)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ResumePayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                
                if (payload == null || string.IsNullOrEmpty(payload.SessionId))
                {
                    return new WsResponse
                    {
                        Type = "error",
                        RequestId = message.RequestId,
                        Payload = new { error = "Invalid resume payload" }
                    };
                }

                // Validate session
                if (!resumeService.ValidateResumeSession(payload.SessionId))
                {
                    return new WsResponse
                    {
                        Type = "error",
                        RequestId = message.RequestId,
                        Payload = new { error = "Invalid or expired session" }
                    };
                }

                // Kiểm tra cửa sổ reconnect
                if (!presenceManager.CanResume(connectionId))
                {
                    return new WsResponse
                    {
                        Type = "error",
                        RequestId = message.RequestId,
                        Payload = new { error = "Resume window expired" }
                    };
                }

                // Lấy danh sách conversation của user
                var conversations = await conversationService.GetUserConversationsAsync(userId);
                var conversationIds = conversations.Select(c => c.Id).ToList();
                
                // Lấy seq hiện tại của các conversation
                var currentSeqs = await resumeService.GetCurrentSeqsAsync(conversationIds);
                
                // Chuẩn bị sinceSeq map (dùng payload nếu có, không thì dùng 0)
                var sinceSeqByConversation = payload.SinceSeqByConversation ?? new Dictionary<string, long>();
                foreach (var convId in conversationIds)
                {
                    if (!sinceSeqByConversation.ContainsKey(convId))
                        sinceSeqByConversation[convId] = 0;
                }

                // Lấy tin nhắn bị miss
                var missedMessages = await resumeService.GetMissedMessagesAsync(userId, sinceSeqByConversation);
                
                // Cập nhật presence
                await presenceService.UpdatePresenceAsync(userId, "online", connectionId, payload.SessionId);
                
                return new WsResponse
                {
                    Type = "resume_ok",
                    RequestId = message.RequestId,
                    Payload = new
                    {
                        sessionId = payload.SessionId,
                        currentSeqs,
                        missedMessages,
                        resumedAt = DateTime.UtcNow
                    }
                };
            }
            catch (Exception ex)
            {
                return new WsResponse
                {
                    Type = "error",
                    RequestId = message.RequestId,
                    Payload = new { error = ex.Message }
                };
            }
        }

        // Xử lý subscribe presence
        public static async Task<WsResponse> HandlePresenceSubscribeAsync(
            WsMessage message,
            string userId,
            string connectionId,
            PresenceResumeManager presenceManager)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<PresenceSubscribePayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                
                if (payload == null || payload.ConversationIds == null)
                {
                    return new WsResponse
                    {
                        Type = "error",
                        RequestId = message.RequestId,
                        Payload = new { error = "Invalid payload" }
                    };
                }

                // Cập nhật client state
                var state = presenceManager.GetClientState(connectionId);
                if (state != null)
                {
                    state.SubscribedConversations = payload.ConversationIds;
                }
                
                return new WsResponse
                {
                    Type = "presence_subscribed",
                    RequestId = message.RequestId,
                    Payload = new
                    {
                        conversationIds = payload.ConversationIds,
                        subscribedAt = DateTime.UtcNow
                    }
                };
            }
            catch (Exception ex)
            {
                return new WsResponse
                {
                    Type = "error",
                    RequestId = message.RequestId,
                    Payload = new { error = ex.Message }
                };
            }
        }
    }

    // Model cho resume payload
    public class ResumePayload
    {
        public string SessionId { get; set; } = string.Empty;
        public Dictionary<string, long>? SinceSeqByConversation { get; set; }
    }

    // Model cho presence subscribe payload
    public class PresenceSubscribePayload
    {
        public List<string> ConversationIds { get; set; } = new();
    }
}