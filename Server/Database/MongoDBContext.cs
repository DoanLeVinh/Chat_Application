using MongoDB.Driver;
using ChatServer.Models;
using MongoDB.Bson;

namespace ChatServer.Database
{
    /// <summary>
    /// MongoDB Context - Kết nối và quản lý collections
    /// </summary>
    public class MongoDBContext
    {
        private readonly IMongoDatabase _database;

        public MongoDBContext(string connectionString, string databaseName)
        {
            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
            
            Console.WriteLine($"✅ Connected to MongoDB: {databaseName}");
            
            // Create indexes
            CreateIndexes();
        }

        public IMongoCollection<User> Users => _database.GetCollection<User>("users");
        public IMongoCollection<Message> Messages => _database.GetCollection<Message>("messages");
        public IMongoCollection<Conversation> Conversations => _database.GetCollection<Conversation>("conversations");
        public IMongoCollection<ConversationMember> ConversationMembers => _database.GetCollection<ConversationMember>("conversation_members");
        public IMongoCollection<ConversationInvite> ConversationInvites => _database.GetCollection<ConversationInvite>("conversation_invites");
        public IMongoCollection<MessageReaction> MessageReactions => _database.GetCollection<MessageReaction>("message_reactions");
        public IMongoCollection<PinnedMessage> PinnedMessages => _database.GetCollection<PinnedMessage>("pinned_messages");
        public IMongoCollection<Sticker> Stickers => _database.GetCollection<Sticker>("stickers");


        private void CreateIndexes()
        {
            try
            {
                // Cleanup legacy data: remove directKey when stored as null (prevents unique index collisions)
                try
                {
                    Conversations.UpdateMany(
                        new BsonDocument("directKey", BsonNull.Value),
                        Builders<Conversation>.Update.Unset("directKey")
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ directKey cleanup warning: {ex.Message}");
                }

                // Conversation: Unique directKey ONLY for direct chats (avoid group collisions on null/missing)
                try { Conversations.Indexes.DropOne("directKey_1"); } catch { }
                try { Conversations.Indexes.DropOne("directKey_unique_sparse"); } catch { }

                var conversationIndexKeys = Builders<Conversation>.IndexKeys.Ascending(c => c.DirectKey);
                var conversationIndexOptions = new CreateIndexOptions
                {
                    Unique = true,
                    Sparse = true,
                    Name = "directKey_unique_sparse"
                };
                Conversations.Indexes.CreateOne(new CreateIndexModel<Conversation>(conversationIndexKeys, conversationIndexOptions));

                // Message: Compound index on conversationId + seq
                var messageIndexKeys = Builders<Message>.IndexKeys
                    .Ascending(m => m.ConversationId)
                    .Ascending(m => m.Seq);
                Messages.Indexes.CreateOne(new CreateIndexModel<Message>(messageIndexKeys));

                // Message: Unique clientMessageId for idempotency
                var clientMsgIndexKeys = Builders<Message>.IndexKeys.Ascending(m => m.ClientMessageId);
                var clientMsgIndexOptions = new CreateIndexOptions { Unique = true, Sparse = true };
                Messages.Indexes.CreateOne(new CreateIndexModel<Message>(clientMsgIndexKeys, clientMsgIndexOptions));

                // ConversationMember: Compound index on conversationId + userId
                var memberIndexKeys = Builders<ConversationMember>.IndexKeys
                    .Ascending(m => m.ConversationId)
                    .Ascending(m => m.UserId);
                var memberIndexOptions = new CreateIndexOptions { Unique = true };
                ConversationMembers.Indexes.CreateOne(new CreateIndexModel<ConversationMember>(memberIndexKeys, memberIndexOptions));

                // ConversationInvite: one pending invite per (conversationId + invitedUserId)
                // NOTE: Some MongoDB.Driver versions used by this project don't support
                // CreateIndexOptions.PartialFilterExpression. We enforce "one pending invite"
                // by creating a unique compound index that includes Status.
                var pendingInviteKeys = Builders<ConversationInvite>.IndexKeys
                    .Ascending(i => i.ConversationId)
                    .Ascending(i => i.InvitedUserId)
                    .Ascending(i => i.Status);

                var pendingInviteOptions = new CreateIndexOptions
                {
                    Unique = true,
                    Name = "invite_unique_by_status"
                };
                ConversationInvites.Indexes.CreateOne(new CreateIndexModel<ConversationInvite>(pendingInviteKeys, pendingInviteOptions));

                // ConversationInvite: query pending by conversation quickly
                var inviteQueryKeys = Builders<ConversationInvite>.IndexKeys
                    .Ascending(i => i.ConversationId)
                    .Ascending(i => i.Status)
                    .Descending(i => i.CreatedAt);
                ConversationInvites.Indexes.CreateOne(new CreateIndexModel<ConversationInvite>(inviteQueryKeys));

                // User: Unique email
                var userEmailIndexKeys = Builders<User>.IndexKeys.Ascending(u => u.Email);
                var userEmailIndexOptions = new CreateIndexOptions { Unique = true };
                Users.Indexes.CreateOne(new CreateIndexModel<User>(userEmailIndexKeys, userEmailIndexOptions));

                // MessageReaction: Unique (messageId + userId + emoji)
                var reactionIndexKeys = Builders<MessageReaction>.IndexKeys
                    .Ascending(r => r.MessageId)
                    .Ascending(r => r.UserId)
                    .Ascending(r => r.Emoji);

                var reactionIndexOptions = new CreateIndexOptions
                {
                    Unique = true
                };

                MessageReactions.Indexes.CreateOne(
                    new CreateIndexModel<MessageReaction>(reactionIndexKeys, reactionIndexOptions)
                );


                Console.WriteLine("✅ Database indexes created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Index creation warning: {ex.Message}");
            }
        }
    }
}

