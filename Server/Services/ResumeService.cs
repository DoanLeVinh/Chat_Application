using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using ChatServer.Models;
using ChatServer.Models.Presence;

namespace ChatServer.Services
{
    public class ResumeService
    {
        private readonly IMongoCollection<Message> _messageCollection;
        private readonly IMongoCollection<ClientState> _clientStateCollection;
        private readonly IMongoCollection<UserSession> _sessionCollection;
        
        public ResumeService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("MongoDB");
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase("ChatAppDB");
            
            _messageCollection = database.GetCollection<Message>("messages");
            _clientStateCollection = database.GetCollection<ClientState>("client_states");
            _sessionCollection = database.GetCollection<UserSession>("user_sessions");
            
            Console.WriteLine("✅ ResumeService initialized");
        }
        
        public async Task SaveClientStateAsync(string userId, Dictionary<string, long> lastSequenceByConversation)
        {
            try
            {
                var filter = Builders<ClientState>.Filter.Eq(c => c.UserId, userId);
                var update = Builders<ClientState>.Update
                    .Set(c => c.LastSequenceByConversation, lastSequenceByConversation)
                    .Set(c => c.LastUpdated, DateTime.UtcNow)
                    .SetOnInsert(c => c.Id, ObjectId.GenerateNewId().ToString());
                
                await _clientStateCollection.UpdateOneAsync(
                    filter, 
                    update, 
                    new UpdateOptions { IsUpsert = true });
                
                Console.WriteLine($"✅ Client state saved for user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving client state: {ex.Message}");
            }
        }
        
        public async Task<List<Message>> GetMissedMessagesAsync(
            string userId, 
            Dictionary<string, long> sinceSeqByConversation)
        {
            var allMessages = new List<Message>();
            
            try
            {
                Console.WriteLine($"🔍 Getting missed messages for user {userId}");
                
                foreach (var kvp in sinceSeqByConversation)
                {
                    var conversationId = kvp.Key;
                    var lastSeq = kvp.Value;
                    
                    Console.WriteLine($"  - Conversation: {conversationId}, LastSeq: {lastSeq}");
                    
                    // Lấy messages bị miss (seq > lastSeq)
                    var filter = Builders<Message>.Filter.And(
                        Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                        Builders<Message>.Filter.Gt(m => m.Sequence, lastSeq),
                        Builders<Message>.Filter.Eq(m => m.Deleted, false)
                    );
                    
                    var messages = await _messageCollection
                        .Find(filter)
                        .SortBy(m => m.Sequence)
                        .Limit(50) // Giới hạn 50 messages mỗi conversation
                        .ToListAsync();
                    
                    if (messages.Any())
                    {
                        allMessages.AddRange(messages);
                        Console.WriteLine($"  - Found {messages.Count} missed messages");
                    }
                }
                
                // Sắp xếp messages theo sequence
                allMessages = allMessages.OrderBy(m => m.Sequence).ToList();
                
                Console.WriteLine($"✅ Total missed messages for user {userId}: {allMessages.Count}");
                return allMessages;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting missed messages: {ex.Message}");
                return allMessages;
            }
        }
        
        public async Task<Dictionary<string, object>> GetSnapshotAsync(List<Message> messages)
        {
            var snapshot = new Dictionary<string, object>();
            
            try
            {
                // TODO: Lấy thông tin pinned messages - phối hợp với Người 4
                // TODO: Lấy thông tin reactions - phối hợp với Người 4
                
                // Tạm thời trả về snapshot rỗng
                snapshot["pinned_messages"] = new List<object>();
                snapshot["reactions"] = new Dictionary<string, object>();
                
                return snapshot;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting snapshot: {ex.Message}");
                return snapshot;
            }
        }
        
        public async Task<ClientState> GetClientStateAsync(string userId)
        {
            return await _clientStateCollection
                .Find(c => c.UserId == userId)
                .FirstOrDefaultAsync();
        }
    }
}