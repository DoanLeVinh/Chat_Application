using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChatServer.Models;
using ChatServer.Database;
using MongoDB.Driver;

namespace ChatServer.Services
{
    public class ResumeService
    {
        private readonly MongoDBContext _dbContext;
        
        public ResumeService(MongoDBContext dbContext)
        {
            _dbContext = dbContext;
        }
        
        // Lấy tin nhắn bị miss
        public async Task<List<Message>> GetMissedMessages(string conversationId, long fromSeq)
        {
            try
            {
                Console.WriteLine($"[Resume] Getting messages from {conversationId}, seq > {fromSeq}");
                
                var filter = Builders<Message>.Filter.And(
                    Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                    Builders<Message>.Filter.Gt(m => m.Seq, fromSeq)
                );
                
                var sort = Builders<Message>.Sort.Ascending(m => m.Seq);
                
                var messages = await _dbContext.Messages
                    .Find(filter)
                    .Sort(sort)
                    .Limit(50) // Giới hạn 50 tin nhắn
                    .ToListAsync();
                
                Console.WriteLine($"[Resume] Found {messages.Count} missed messages");
                return messages;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Resume] Error: {ex.Message}");
                return new List<Message>();
            }
        }
        
        // Lấy seq hiện tại của conversation
        public async Task<long> GetCurrentSeq(string conversationId)
        {
            try
            {
                var filter = Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId);
                var sort = Builders<Message>.Sort.Descending(m => m.Seq);
                
                var lastMessage = await _dbContext.Messages
                    .Find(filter)
                    .Sort(sort)
                    .FirstOrDefaultAsync();
                
                return lastMessage?.Seq ?? 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}