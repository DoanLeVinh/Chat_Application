using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChatServer.Models;
using MongoDB.Driver;
using MongoDB.Bson;


namespace ChatServer.Models
{
    public class ResumeService
    {
        private readonly IMongoCollection<Message> _messagesCollection;
        private readonly ConnectionManager _connectionManager;
        private readonly IMongoDatabase _database;
        
        public ResumeService(
            IMongoDatabase database,
            ConnectionManager connectionManager)
        {
            _database = database;
            _messagesCollection = database.GetCollection<Message>("messages");
            _connectionManager = connectionManager;
        }
        
        // Main method: Lấy missed messages
        public async Task<ResumeResponseData> ProcessResumeRequest(
            string connectionId, 
            Dictionary<string, long> sinceSeqByConversation)
        {
            Console.WriteLine($"[ResumeService] Processing resume for connection {connectionId}");
            
            var response = new ResumeResponseData
            {
                RequestId = Guid.NewGuid().ToString(),
                Success = false,
                MissedMessages = new List<MissedMessage>(),
                CurrentSeq = new Dictionary<string, long>()
            };
            
            try
            {
                // 1. Validate connection
                var connectionState = _connectionManager.GetConnectionState(connectionId);
                if (connectionState == null)
                {
                    response.Error = "Connection not found";
                    return response;
                }
                
                // 2. Lấy missed messages cho từng conversation
                var allMissedMessages = new List<MissedMessage>();
                
                foreach (var kvp in sinceSeqByConversation)
                {
                    var conversationId = kvp.Key;
                    var sinceSeq = kvp.Value;
                    
                    Console.WriteLine($"[ResumeService] Querying conversation {conversationId} from seq {sinceSeq}");
                    
                    var conversationMessages = await GetMissedMessagesForConversation(
                        conversationId, 
                        sinceSeq,
                        limit: 200 // Giới hạn để tránh quá tải
                    );
                    
                    allMissedMessages.AddRange(conversationMessages);
                    
                    // Lấy seq hiện tại của conversation
                    var currentSeq = await GetCurrentSeqForConversation(conversationId);
                    response.CurrentSeq[conversationId] = currentSeq;
                    
                    // Cập nhật last seen seq cho connection
                    _connectionManager.UpdateLastSeenSeq(connectionId, conversationId, currentSeq);
                }
                
                // 3. Sắp xếp messages theo seq
                allMissedMessages = allMissedMessages
                    .OrderBy(m => m.Seq)
                    .ToList();
                
                // 4. Thêm reactions/pins nếu có (phối hợp với người 4)
                // var missedReactions = await GetMissedReactions(allMissedMessages);
                // allMissedMessages.AddRange(missedReactions);
                
                response.MissedMessages = allMissedMessages;
                response.Success = true;
                
                Console.WriteLine($"[ResumeService] Resume successful. Returned {allMissedMessages.Count} messages");
            }
            catch (Exception ex)
            {
                response.Error = $"Resume failed: {ex.Message}";
                Console.WriteLine($"[ResumeService] Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            
            return response;
        }
        
        // Lấy missed messages cho một conversation
        private async Task<List<MissedMessage>> GetMissedMessagesForConversation(
            string conversationId, 
            long sinceSeq, 
            int limit = 200)
        {
            try
            {
                // Tạo filter: conversationId AND seq > sinceSeq
                var filter = Builders<Message>.Filter.And(
                    Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                    Builders<Message>.Filter.Gt(m => m.Seq, sinceSeq)
                );
                
                // Sắp xếp tăng dần theo seq
                var sort = Builders<Message>.Sort.Ascending(m => m.Seq);
                
                var messages = await _messagesCollection
                    .Find(filter)
                    .Sort(sort)
                    .Limit(limit)
                    .ToListAsync();
                
                Console.WriteLine($"[ResumeService] Found {messages.Count} messages for {conversationId}");
                
                // Convert sang MissedMessage
                var missedMessages = new List<MissedMessage>();
                foreach (var msg in messages)
                {
                    // Kiểm tra null và validate
                    if (msg != null && msg.Seq > sinceSeq)
                    {
                        missedMessages.Add(new MissedMessage
                        {
                            ConversationId = conversationId,
                            Seq = msg.Seq,
                            Message = new 
                            {
                                id = msg.Id,
                                senderId = msg.SenderId,
                                content = msg.Content,
                                timestamp = msg.Timestamp,
                                seq = msg.Seq,
                                type = msg.Type
                            }
                        });
                    }
                }
                
                return missedMessages;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ResumeService] Error getting messages: {ex.Message}");
                return new List<MissedMessage>();
            }
        }
        
        // Lấy seq hiện tại của conversation
        private async Task<long> GetCurrentSeqForConversation(string conversationId)
        {
            try
            {
                var filter = Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId);
                var sort = Builders<Message>.Sort.Descending(m => m.Seq);
                
                var lastMessage = await _messagesCollection
                    .Find(filter)
                    .Sort(sort)
                    .Limit(1)
                    .FirstOrDefaultAsync();
                
                return lastMessage?.Seq ?? 0;
            }
            catch
            {
                return 0;
            }
        }
        
        // Utility: Lấy tin nhắn theo khoảng seq (cho testing)
        public async Task<List<Message>> GetMessagesInRange(
            string conversationId, 
            long fromSeq, 
            long toSeq)
        {
            var filter = Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                Builders<Message>.Filter.Gte(m => m.Seq, fromSeq),
                Builders<Message>.Filter.Lte(m => m.Seq, toSeq)
            );
            
            var sort = Builders<Message>.Sort.Ascending(m => m.Seq);
            
            return await _messagesCollection
                .Find(filter)
                .Sort(sort)
                .ToListAsync();
        }
        
        // Debug: In thông tin resume
        public void DebugResumeInfo(string connectionId)
        {
            var state = _connectionManager.GetConnectionState(connectionId);
            if (state != null)
            {
                Console.WriteLine($"[ResumeService] Debug for {connectionId}:");
                Console.WriteLine($"  User: {state.UserId}");
                Console.WriteLine($"  Last heartbeat: {state.LastHeartbeat}");
                Console.WriteLine($"  Last seen seq: {string.Join(", ", state.LastSeenSeq)}");
            }
        }
    }
}