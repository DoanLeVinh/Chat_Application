using System.Text.Json;

namespace ChatServer.WebSockets
{
    /// <summary>
    /// WebSocket Event Models - Người 2
    /// </summary>
    /// 
    // Base event structure
    public class WsEvent
    {
        public string Type { get; set; } = string.Empty;
        public string? RequestId { get; set; }
        public JsonElement Payload { get; set; }
    }

    // ===== Người 2: Direct Chat & Group Events =====
    
    // Send message event
    public class SendMessagePayload
    {
        public string ConversationId { get; set; } = string.Empty;
        public string ClientMessageId { get; set; } = string.Empty;
        public string MessageType { get; set; } = "text"; // text | sticker
        public string Content { get; set; } = string.Empty;
    }

    public class MessageCreatedPayload
    {
        public string MessageId { get; set; } = string.Empty;
        public string ConversationId { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public long Seq { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // Create group event
    public class CreateGroupPayload
    {
        public string Title { get; set; } = string.Empty;
        public List<string> MemberIds { get; set; } = new();
    }

    public class GroupCreatedPayload
    {
        public string ConversationId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<MemberInfo> Members { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    // Add/Remove member events
    public class AddMemberPayload
    {
        public string ConversationId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
    }

    public class RemoveMemberPayload
    {
        public string ConversationId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
    }

    public class MemberInfo
    {
        public string UserId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime JoinedAt { get; set; }
    }

    // ===== Người 1: Auth Events (để tham khảo) =====
    // TODO: Người 1 implement
    public class AuthPayload
    {
        public string AccessToken { get; set; } = string.Empty;
    }

    // ===== Người 3: Resume/Presence Events (để tham khảo) =====
    // TODO: Người 3 implement
    public class ResumePayload
    {
        public Dictionary<string, long> SinceSeqByConversation { get; set; } = new();
    }

    // ===== Người 4: Reaction/Pin/Sticker (để tham khảo) =====
    // TODO: Người 4 implement
}
