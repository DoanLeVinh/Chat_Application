using ChatServer.Database;
using ChatServer.Models;
using MongoDB.Driver;

namespace ChatServer.Services
{
    /// <summary>
    /// Message Service - Người 2
    /// Xử lý: Send message, Lưu DB, Tạo seq tăng dần
    /// </summary>
    public class MessageService
    {
        private readonly IMongoCollection<Message> _messages;
        private readonly IMongoCollection<Conversation> _conversations;

        public MessageService(MongoDBContext dbContext)
        {
            _messages = dbContext.Messages;
            _conversations = dbContext.Conversations;

            // Tạo index
            CreateIndexes();
        }

        private void CreateIndexes()
        {
            // Index cho query theo conversation + seq (quan trọng cho resume - Người 3)
            var indexKeys = Builders<Message>.IndexKeys
                .Ascending(m => m.ConversationId)
                .Ascending(m => m.Seq);
            _messages.Indexes.CreateOne(new CreateIndexModel<Message>(indexKeys));

            // Unique index cho clientMessageId trong 1 conversation (chống trùng)
            var uniqueIndex = Builders<Message>.IndexKeys
                .Ascending(m => m.ConversationId)
                .Ascending(m => m.ClientMessageId);
            _messages.Indexes.CreateOne(new CreateIndexModel<Message>(
                uniqueIndex,
                new CreateIndexOptions { Unique = true }
            ));
        }

        /// <summary>
        /// Tạo message mới với seq tăng dần (CORE logic - Người 2)
        /// </summary>
        public async Task<Message> CreateMessageAsync(string conversationId, string senderId, string content, string type, string clientMessageId)
        {
            // Kiểm tra trùng clientMessageId
            var existing = await _messages.Find(m => 
                m.ConversationId == conversationId && 
                m.ClientMessageId == clientMessageId
            ).FirstOrDefaultAsync();

            if (existing != null)
            {
                // Idempotent: đã tồn tại, trả về message cũ
                return existing;
            }

            // Tăng seq trong conversation (atomic operation)
            var filter = Builders<Conversation>.Filter.Eq(c => c.Id, conversationId);
            var update = Builders<Conversation>.Update.Inc(c => c.LastSeq, 1);
            var options = new FindOneAndUpdateOptions<Conversation>
            {
                ReturnDocument = ReturnDocument.After
            };

            var conversation = await _conversations.FindOneAndUpdateAsync(filter, update, options);
            
            if (conversation == null)
            {
                throw new Exception("Conversation not found");
            }

            // Tạo message với seq mới
            var message = new Message
            {
                ConversationId = conversationId,
                SenderId = senderId,
                Type = type,
                Content = content,
                ClientMessageId = clientMessageId,
                Seq = conversation.LastSeq,
                CreatedAt = DateTime.UtcNow
            };

            await _messages.InsertOneAsync(message);
            return message;
        }

        /// <summary>
        /// Lấy messages theo conversation (cho load history)
        /// </summary>
        public async Task<List<Message>> GetMessagesAsync(string conversationId, int limit = 50, long? beforeSeq = null)
        {
            var filter = beforeSeq.HasValue
                ? Builders<Message>.Filter.And(
                    Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                    Builders<Message>.Filter.Lt(m => m.Seq, beforeSeq.Value))
                : Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId);

            return await _messages
                .Find(filter)
                .SortByDescending(m => m.Seq)
                .Limit(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Lấy messages sau 1 seq (cho resume - Người 3 sẽ dùng)
        /// </summary>
        public async Task<List<Message>> GetMessagesSinceSeqAsync(string conversationId, long sinceSeq, int limit = 100)
        {
            return await _messages
                .Find(m => m.ConversationId == conversationId && m.Seq > sinceSeq)
                .SortBy(m => m.Seq)
                .Limit(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Lấy 1 message theo ID
        /// </summary>
        public async Task<Message?> GetMessageByIdAsync(string messageId)
        {
            return await _messages.Find(m => m.Id == messageId).FirstOrDefaultAsync();
        }
    }
}
