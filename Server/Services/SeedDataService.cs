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
        private readonly IWebHostEnvironment _env;

        public SeedDataService(
            MongoDBContext dbContext,
            UserService userService,
            ConversationService conversationService,
            MessageService messageService,
            IWebHostEnvironment env)
        {
            _dbContext = dbContext;
            _userService = userService;
            _conversationService = conversationService;
            _messageService = messageService;
            _env = env;
        }

        public async Task SeedAsync()
        {
            await SeedStickersAsync();

            // Nếu đã có user thì skip seed demo
            var userCount = await _dbContext.Users.CountDocumentsAsync(FilterDefinition<User>.Empty);
            if (userCount > 0)
            {
                Console.WriteLine("✅ Database already has data, skipping seed");
                return;
            }

            Console.WriteLine("🌱 Seeding database...");

            // ================= USERS =================
            var user1 = new User
            {
                Email = "vinh@demo.com",
                DisplayName = "Doãn Vịnh",
                PasswordHash = "demo123",
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
            Console.WriteLine("✅ Created 4 demo users");

            // ================= DIRECT CHAT =================
            var directConv = await _conversationService
                .GetOrCreateDirectConversationAsync(user1.Id, user2.Id);

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

            Console.WriteLine("✅ Created direct conversation messages");

            // ================= GROUP CHAT =================
            var groupConv = await _conversationService.CreateGroupConversationAsync(
                user1.Id,
                "Nhóm Chat App LTM",
                new List<string> { user2.Id, user3.Id, user4.Id }
            );

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

            Console.WriteLine("🎉 Seed completed successfully!");
        }

        /// <summary>
        /// AUTO seed stickers từ folder wwwroot/stickers
        /// </summary>
        private async Task SeedStickersAsync()
        {
            var webRoot = _env.WebRootPath;

            if (string.IsNullOrEmpty(webRoot))
            {
                Console.WriteLine("⚠️ WebRootPath is null. Ensure wwwroot exists & UseStaticFiles() is enabled.");
                return;
            }

            var stickerPath = Path.Combine(webRoot, "stickers");

            if (!Directory.Exists(stickerPath))
            {
                Console.WriteLine("⚠️ Sticker folder not found, skipping sticker seed");
                return;
            }

            var files = Directory.GetFiles(stickerPath);

            int count = 0;

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var code = Path.GetFileNameWithoutExtension(fileName);
                var imageUrl = $"/stickers/{fileName}";

                var exists = await _dbContext.Stickers
                    .Find(s => s.Code == code)
                    .AnyAsync();

                if (!exists)
                {
                    await _dbContext.Stickers.InsertOneAsync(new Sticker
                    {
                        Code = code,
                        ImageUrl = imageUrl
                    });
                    count++;
                }
            }

            Console.WriteLine($"🖼️ Seeded {count} stickers");
        }

    }
}
