using ChatServer.Database;
using ChatServer.Models;
using MongoDB.Driver;

namespace ChatServer.Services
{
    /// <summary>
    /// Seed Demo Data - Tạo user, conversation, message mẫu
    /// </summary>
    public class SeedDataService
    {
        private readonly MongoDBContext _dbContext;
        private readonly UserService _userService;
        private readonly ConversationService _conversationService;
        private readonly MessageService _messageService;

        public SeedDataService(
            MongoDBContext dbContext,
            UserService userService,
            ConversationService conversationService,
            MessageService messageService)
        {
            _dbContext = dbContext;
            _userService = userService;
            _conversationService = conversationService;
            _messageService = messageService;
        }

        public async Task SeedAsync()
        {
            if (!await _dbContext.Stickers.Find(_ => true).AnyAsync())
            {
                await _dbContext.Stickers.InsertManyAsync(new[]
                {
                    new Sticker { Code = "thumb_up", ImageUrl = "/stickers/thumb.png" },
                    new Sticker { Code = "haha", ImageUrl = "/stickers/haha.png" },
                    new Sticker { Code = "love", ImageUrl = "/stickers/love.png" }
                });
            }
            // Kiểm tra đã có data chưa
            var userCount = await _dbContext.Users.CountDocumentsAsync(FilterDefinition<User>.Empty);
            if (userCount > 0)
            {
                Console.WriteLine("✅ Database already has data, skipping seed");
                return;
            }

            Console.WriteLine("🌱 Seeding database...");

            // Tạo demo users
            var user1 = new User
            {
                Email = "vinh@demo.com",
                DisplayName = "Doãn Vịnh",
                PasswordHash = "demo123", // TODO: Người 1 sẽ hash password
                CreatedAt = DateTime.UtcNow
            };

            var user2 = new User
            {
                Email = "quang@demo.com",
                DisplayName = "Quang Thi",
                PasswordHash = "demo123",
                CreatedAt = DateTime.UtcNow
            };

            var user3 = new User
            {
                Email = "huyen@demo.com",
                DisplayName = "Khánh Huyền",
                PasswordHash = "demo123",
                CreatedAt = DateTime.UtcNow
            };

            var user4 = new User
            {
                Email = "suong@demo.com",
                DisplayName = "Thanh Sương",
                PasswordHash = "demo123",
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.InsertManyAsync(new[] { user1, user2, user3, user4 });
            Console.WriteLine($"✅ Created 4 demo users");

            // Tạo direct conversation giữa Vịnh và Quang
            var directConv = await _conversationService.GetOrCreateDirectConversationAsync(user1.Id, user2.Id);
            Console.WriteLine($"✅ Created direct conversation: {directConv.Id}");

            // Tạo messages trong direct chat
            await _messageService.CreateMessageAsync(
                directConv.Id,
                user1.Id,
                "Chào Quang! Mình là Vịnh, đang làm phần Chat Core đây 😊",
                "text",
                Guid.NewGuid().ToString()
            );

            await _messageService.CreateMessageAsync(
                directConv.Id,
                user2.Id,
                "Hi Vịnh! Mình làm Auth và nền tảng server. Đang test WebSocket nhé!",
                "text",
                Guid.NewGuid().ToString()
            );

            await _messageService.CreateMessageAsync(
                directConv.Id,
                user1.Id,
                "Tuyệt! Message seq đang chạy tốt, bạn kiểm tra thử nhé 👍",
                "text",
                Guid.NewGuid().ToString()
            );

            Console.WriteLine($"✅ Created 3 messages in direct chat");

            // Tạo group conversation
            var groupConv = await _conversationService.CreateGroupConversationAsync(
                user1.Id,
                "Nhóm Chat App LTM",
                new List<string> { user2.Id, user3.Id, user4.Id }
            );
            Console.WriteLine($"✅ Created group conversation: {groupConv.Id}");

            // Tạo messages trong group
            await _messageService.CreateMessageAsync(
                groupConv.Id,
                user1.Id,
                "Chào cả nhóm! Đây là group chat demo 🎉",
                "text",
                Guid.NewGuid().ToString()
            );

            await _messageService.CreateMessageAsync(
                groupConv.Id,
                user3.Id,
                "Hi mọi người! Mình làm phần Presence và Reconnect đây",
                "text",
                Guid.NewGuid().ToString()
            );

            await _messageService.CreateMessageAsync(
                groupConv.Id,
                user4.Id,
                "Chào team! Mình phụ trách Search, Reaction, Pin, Sticker",
                "text",
                Guid.NewGuid().ToString()
            );

            await _messageService.CreateMessageAsync(
                groupConv.Id,
                user2.Id,
                "Teamwork makes the dream work! 💪",
                "text",
                Guid.NewGuid().ToString()
            );


            Console.WriteLine($"✅ Created 4 messages in group chat");

            Console.WriteLine("🎉 Seed completed successfully!");
            Console.WriteLine();
            Console.WriteLine("📝 Demo accounts:");
            Console.WriteLine($"   1. Email: vinh@demo.com | Password: demo123 | UserId: {user1.Id}");
            Console.WriteLine($"   2. Email: quang@demo.com | Password: demo123 | UserId: {user2.Id}");
            Console.WriteLine($"   3. Email: huyen@demo.com | Password: demo123 | UserId: {user3.Id}");
            Console.WriteLine($"   4. Email: suong@demo.com | Password: demo123 | UserId: {user4.Id}");
            Console.WriteLine();
        }
    }
}
