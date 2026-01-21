using ChatServer.Database;
using ChatServer.Models;
using MongoDB.Driver;

namespace ChatServer.Services
{
    /// <summary>
    /// Conversation Service - Người 2
    /// Xử lý: Direct chat (1-1), Group chat, Members
    /// </summary>
    public class ConversationService
    {
        private readonly IMongoCollection<Conversation> _conversations;
        private readonly IMongoCollection<ConversationMember> _members;

        public ConversationService(MongoDBContext dbContext)
        {
            _conversations = dbContext.Conversations;
            _members = dbContext.ConversationMembers;

            CreateIndexes();
        }

        private void CreateIndexes()
        {
            // Unique index cho directKey (QUAN TRỌNG - chống tạo trùng direct chat)
            var directKeyIndex = Builders<Conversation>.IndexKeys.Ascending(c => c.DirectKey);
            _conversations.Indexes.CreateOne(new CreateIndexModel<Conversation>(
                directKeyIndex,
                new CreateIndexOptions { Unique = true, Sparse = true }
            ));

            // Index cho member lookup
            var memberIndex = Builders<ConversationMember>.IndexKeys
                .Ascending(m => m.UserId)
                .Ascending(m => m.ConversationId);
            _members.Indexes.CreateOne(new CreateIndexModel<ConversationMember>(memberIndex));
        }

        /// <summary>
        /// Tạo hoặc lấy direct conversation (1-1) - CORE logic Người 2
        /// </summary>
        public async Task<Conversation> GetOrCreateDirectConversationAsync(string userAId, string userBId)
        {
            // Tạo directKey = min:max để tránh trùng
            var directKey = GetDirectKey(userAId, userBId);

            // Tìm conversation đã tồn tại
            var existing = await _conversations
                .Find(c => c.DirectKey == directKey)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return existing;
            }

            // Tạo mới conversation
            var conversation = new Conversation
            {
                Type = "direct",
                DirectKey = directKey,
                CreatedBy = userAId,
                CreatedAt = DateTime.UtcNow,
                LastSeq = 0
            };

            await _conversations.InsertOneAsync(conversation);

            // Thêm 2 members
            await AddMemberAsync(conversation.Id, userAId, "member");
            await AddMemberAsync(conversation.Id, userBId, "member");

            return conversation;
        }

        /// <summary>
        /// Tạo group conversation - Người 2
        /// </summary>
        public async Task<Conversation> CreateGroupConversationAsync(string creatorId, string title, List<string> memberIds)
        {
            var conversation = new Conversation
            {
                Type = "group",
                Title = title,
                CreatedBy = creatorId,
                CreatedAt = DateTime.UtcNow,
                LastSeq = 0
            };

            await _conversations.InsertOneAsync(conversation);

            // Thêm creator là owner
            await AddMemberAsync(conversation.Id, creatorId, "owner");

            // Thêm các members
            foreach (var memberId in memberIds.Where(id => id != creatorId))
            {
                await AddMemberAsync(conversation.Id, memberId, "member");
            }

            return conversation;
        }

        /// <summary>
        /// Thêm member vào conversation - Người 2
        /// </summary>
        public async Task AddMemberAsync(string conversationId, string userId, string role = "member")
        {
            // Kiểm tra đã là member chưa
            var existing = await _members
                .Find(m => m.ConversationId == conversationId && m.UserId == userId)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return; // Đã là member rồi
            }

            var member = new ConversationMember
            {
                ConversationId = conversationId,
                UserId = userId,
                Role = role,
                JoinedAt = DateTime.UtcNow
            };

            await _members.InsertOneAsync(member);
        }

        /// <summary>
        /// Xóa member khỏi group - Người 2
        /// </summary>
        public async Task RemoveMemberAsync(string conversationId, string userId)
        {
            await _members.DeleteOneAsync(m => 
                m.ConversationId == conversationId && 
                m.UserId == userId
            );
        }

        /// <summary>
        /// Lấy danh sách members của conversation
        /// </summary>
        public async Task<List<ConversationMember>> GetMembersAsync(string conversationId)
        {
            return await _members
                .Find(m => m.ConversationId == conversationId)
                .ToListAsync();
        }

        /// <summary>
        /// Lấy thông tin member
        /// </summary>
        public async Task<ConversationMember?> GetMemberAsync(string conversationId, string userId)
        {
            return await _members
                .Find(m => m.ConversationId == conversationId && m.UserId == userId)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Kiểm tra user có phải member không
        /// </summary>
        public async Task<bool> IsMemberAsync(string conversationId, string userId)
        {
            var count = await _members
                .CountDocumentsAsync(m => m.ConversationId == conversationId && m.UserId == userId);
            return count > 0;
        }

        /// <summary>
        /// Lấy danh sách conversations của user
        /// </summary>
        public async Task<List<Conversation>> GetUserConversationsAsync(string userId)
        {
            // Lấy danh sách conversationIds user tham gia
            var memberList = await _members
                .Find(m => m.UserId == userId)
                .ToListAsync();

            var conversationIds = memberList.Select(m => m.ConversationId).ToList();

            // Lấy conversations
            return await _conversations
                .Find(c => conversationIds.Contains(c.Id))
                .SortByDescending(c => c.UpdatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Lấy conversation theo ID
        /// </summary>
        public async Task<Conversation?> GetConversationByIdAsync(string conversationId)
        {
            return await _conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Helper: Tạo directKey từ 2 userIds
        /// </summary>
        private string GetDirectKey(string userAId, string userBId)
        {
            var ids = new[] { userAId, userBId }.OrderBy(id => id).ToArray();
            return $"{ids[0]}:{ids[1]}";
        }
    }
}
