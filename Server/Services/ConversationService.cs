using ChatServer.Database;
using ChatServer.Models;
using MongoDB.Driver;
using MongoDB.Bson;

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
        private readonly IMongoCollection<ConversationInvite> _invites;

        public ConversationService(MongoDBContext dbContext)
        {
            _conversations = dbContext.Conversations;
            _members = dbContext.ConversationMembers;
            _invites = dbContext.ConversationInvites;

            CreateIndexes();
        }

        public async Task<bool> SetInviteModeAsync(string conversationId, string inviteMode)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return false;
            var mode = (inviteMode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode != "public" && mode != "private") return false;

            var update = Builders<Conversation>.Update
                .Set(c => c.InviteMode, mode)
                .Set(c => c.UpdatedAt, DateTime.UtcNow);

            var result = await _conversations.UpdateOneAsync(c => c.Id == conversationId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<ConversationInvite?> CreateInviteAsync(string conversationId, string invitedUserId, string invitedByUserId)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(invitedUserId) || string.IsNullOrWhiteSpace(invitedByUserId))
            {
                return null;
            }

            // Check already member
            var existingMember = await _members
                .Find(m => m.ConversationId == conversationId && m.UserId == invitedUserId)
                .FirstOrDefaultAsync();
            if (existingMember != null)
            {
                return null;
            }

            // Idempotency: one pending invite
            var existingInvite = await _invites
                .Find(i => i.ConversationId == conversationId && i.InvitedUserId == invitedUserId && i.Status == "pending")
                .FirstOrDefaultAsync();
            if (existingInvite != null)
            {
                return existingInvite;
            }

            var invite = new ConversationInvite
            {
                ConversationId = conversationId,
                InvitedUserId = invitedUserId,
                InvitedByUserId = invitedByUserId,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                await _invites.InsertOneAsync(invite);
                return invite;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return await _invites
                    .Find(i => i.ConversationId == conversationId && i.InvitedUserId == invitedUserId && i.Status == "pending")
                    .FirstOrDefaultAsync();
            }
        }

        public async Task<List<ConversationInvite>> GetPendingInvitesAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<ConversationInvite>();
            return await _invites
                .Find(i => i.ConversationId == conversationId && i.Status == "pending")
                .SortByDescending(i => i.CreatedAt)
                .ToListAsync();
        }

        public async Task<ConversationInvite?> ApproveInviteAsync(string inviteId, string decidedByUserId)
        {
            if (string.IsNullOrWhiteSpace(inviteId) || string.IsNullOrWhiteSpace(decidedByUserId)) return null;
            var update = Builders<ConversationInvite>.Update
                .Set(i => i.Status, "approved")
                .Set(i => i.DecidedAt, DateTime.UtcNow)
                .Set(i => i.DecidedByUserId, decidedByUserId);

            var options = new FindOneAndUpdateOptions<ConversationInvite>
            {
                ReturnDocument = ReturnDocument.After
            };

            return await _invites.FindOneAndUpdateAsync(
                i => i.Id == inviteId && i.Status == "pending",
                update,
                options
            );
        }

        public async Task<ConversationInvite?> RejectInviteAsync(string inviteId, string decidedByUserId)
        {
            if (string.IsNullOrWhiteSpace(inviteId) || string.IsNullOrWhiteSpace(decidedByUserId)) return null;
            var update = Builders<ConversationInvite>.Update
                .Set(i => i.Status, "rejected")
                .Set(i => i.DecidedAt, DateTime.UtcNow)
                .Set(i => i.DecidedByUserId, decidedByUserId);

            var options = new FindOneAndUpdateOptions<ConversationInvite>
            {
                ReturnDocument = ReturnDocument.After
            };

            return await _invites.FindOneAndUpdateAsync(
                i => i.Id == inviteId && i.Status == "pending",
                update,
                options
            );
        }

        private void CreateIndexes()
        {
            try
            {
                // Unique index cho directKey (QUAN TRỌNG - chống tạo trùng direct chat)
                var directKeyIndex = Builders<Conversation>.IndexKeys.Ascending(c => c.DirectKey);
                _conversations.Indexes.CreateOne(new CreateIndexModel<Conversation>(
                    directKeyIndex,
                    new CreateIndexOptions { Unique = true, Sparse = true }
                ));
            }
            catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict" || ex.CodeName == "IndexKeySpecsConflict")
            {
                // Index đã tồn tại, bỏ qua
                Console.WriteLine($"DirectKey index already exists: {ex.Message}");
            }

            try
            {
                // Unique index cho (conversationId, userId) để tránh trùng member do race
                var uniqueMemberIndex = Builders<ConversationMember>.IndexKeys
                    .Ascending(m => m.ConversationId)
                    .Ascending(m => m.UserId);
                _members.Indexes.CreateOne(new CreateIndexModel<ConversationMember>(
                    uniqueMemberIndex,
                    new CreateIndexOptions { Unique = true }
                ));
            }
            catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict" || ex.CodeName == "IndexKeySpecsConflict")
            {
                // Index đã tồn tại, bỏ qua
                Console.WriteLine($"Member index already exists: {ex.Message}");
            }

            try
            {
                // Index cho list members theo conversation
                var conversationIdIndex = Builders<ConversationMember>.IndexKeys
                    .Ascending(m => m.ConversationId);
                _members.Indexes.CreateOne(new CreateIndexModel<ConversationMember>(conversationIdIndex));
            }
            catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict" || ex.CodeName == "IndexKeySpecsConflict")
            {
                Console.WriteLine($"ConversationId member index already exists: {ex.Message}");
            }
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
                UpdatedAt = DateTime.UtcNow,
                MembersVersion = 1,
                LastSeq = 0
            };

            await _conversations.InsertOneAsync(conversation);

            // Thêm 2 members
            await AddMemberAsync(conversation.Id, userAId, "member", incrementVersion: false);
            await AddMemberAsync(conversation.Id, userBId, "member", incrementVersion: false);

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
                UpdatedAt = DateTime.UtcNow,
                MembersVersion = 1,
                LastSeq = 0
            };

            await _conversations.InsertOneAsync(conversation);

            // Thêm creator là owner
            await AddMemberAsync(conversation.Id, creatorId, "owner", incrementVersion: false);

            // Thêm các members
            foreach (var memberId in memberIds.Where(id => id != creatorId))
            {
                await AddMemberAsync(conversation.Id, memberId, "member", incrementVersion: false);
            }

            return conversation;
        }

        /// <summary>
        /// Thêm member vào conversation - Người 2
        /// </summary>
        public async Task<ConversationMember?> AddMemberAsync(string conversationId, string userId, string role = "member", bool incrementVersion = true)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            // Kiểm tra đã là member chưa
            var existing = await _members
                .Find(m => m.ConversationId == conversationId && m.UserId == userId)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return existing; // Đã là member rồi
            }

            var member = new ConversationMember
            {
                ConversationId = conversationId,
                UserId = userId,
                Role = role,
                JoinedAt = DateTime.UtcNow
            };

            try
            {
                await _members.InsertOneAsync(member);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Race condition: đã có member insert trước đó
                return await _members
                    .Find(m => m.ConversationId == conversationId && m.UserId == userId)
                    .FirstOrDefaultAsync();
            }

            if (incrementVersion)
            {
                await TouchMembersVersionAsync(conversationId);
            }

            return member;
        }

        /// <summary>
        /// Xóa member khỏi group - Người 2
        /// </summary>
        public async Task<bool> RemoveMemberAsync(string conversationId, string userId, bool incrementVersion = true)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            var result = await _members.DeleteOneAsync(m => 
                m.ConversationId == conversationId && 
                m.UserId == userId
            );

            if (result.DeletedCount > 0 && incrementVersion)
            {
                await TouchMembersVersionAsync(conversationId);
            }

            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Tăng membersVersion + update updatedAt để client đồng bộ realtime.
        /// </summary>
        public async Task<long> TouchMembersVersionAsync(string conversationId)
        {
            var update = Builders<Conversation>.Update
                .Inc(c => c.MembersVersion, 1)
                .Set(c => c.UpdatedAt, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<Conversation>
            {
                ReturnDocument = ReturnDocument.After
            };

            var updated = await _conversations.FindOneAndUpdateAsync(c => c.Id == conversationId, update, options);
            return updated?.MembersVersion ?? 0;
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

            // Defensive: lọc dữ liệu bẩn trong conversation_members (conversationId null/empty)
            var conversationIds = memberList
                .Select(m => m.ConversationId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (conversationIds.Count == 0)
            {
                return new List<Conversation>();
            }

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
