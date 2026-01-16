using MongoDB.Driver;
using ChatServer.Models;

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
        public IMongoCollection<MessageReaction> MessageReactions => _database.GetCollection<MessageReaction>("message_reactions");
        public IMongoCollection<PinnedMessage> PinnedMessages => _database.GetCollection<PinnedMessage>("pinned_messages");


        private void CreateIndexes()
        {
            try
            {
                // Conversation: Unique direct_key for 1-1 chats
                var conversationIndexKeys = Builders<Conversation>.IndexKeys.Ascending(c => c.DirectKey);
                var conversationIndexOptions = new CreateIndexOptions { Unique = true, Sparse = true };
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

