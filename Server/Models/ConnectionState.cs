using System;
using System.Collections.Generic;

namespace ChatServer.Models
{
    public class ConnectionState
    {
        public string ConnectionId { get; set; } = string.Empty; // Giá trị mặc định
        public string UserId { get; set; } = string.Empty; // Giá trị mặc định
        public DateTime ConnectedAt { get; set; }
        public DateTime LastHeartbeat { get; set; }
 
        public Dictionary<string, long> LastSeenSeq { get; set; } = new Dictionary<string, long>(); // Giá trị mặc định
 
        public ConnectionState()
        {
            ConnectedAt = DateTime.UtcNow;
            LastHeartbeat = DateTime.UtcNow;
        }
    }

    public class ResumeRequestData
    {
        public string RequestId { get; set; } = string.Empty; // Giá trị mặc định
        public Dictionary<string, long> SinceSeqByConversation { get; set; } = new Dictionary<string, long>(); // Giá trị mặc định
    }

    public class ResumeResponseData
    {
        public string RequestId { get; set; } = string.Empty; // Giá trị mặc định
        public bool Success { get; set; }
        public List<MissedMessage> MissedMessages { get; set; } = new List<MissedMessage>(); // Giá trị mặc định
        public Dictionary<string, long> CurrentSeq { get; set; } = new Dictionary<string, long>(); // Giá trị mặc định
        public string Error { get; set; } = string.Empty; // Giá trị mặc định
    }

    public class MissedMessage
    {
        public string ConversationId { get; set; } = string.Empty; // Giá trị mặc định
        public long Seq { get; set; }
        public object Message { get; set; } = new object(); // Giá trị mặc định
        public string Type { get; set; } = "message"; // Giá trị mặc định
    }
}

