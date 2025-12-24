using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WebSocketTestClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== WebSocket Test Client ===\n");

            // Bước 1: Đăng nhập để lấy JWT token
            Console.WriteLine("Bước 1: Đăng nhập để lấy JWT token");
            Console.Write("Email (hoặc Enter để dùng test@example.com): ");
            string email = Console.ReadLine() ?? "test@example.com";
            if (string.IsNullOrWhiteSpace(email)) email = "test@example.com";

            Console.Write("Password (hoặc Enter để dùng Test123456): ");
            string password = Console.ReadLine() ?? "Test123456";
            if (string.IsNullOrWhiteSpace(password)) password = "Test123456";

            string? token = await LoginAsync(email, password);
            if (token == null)
            {
                Console.WriteLine("\n❌ Đăng nhập thất bại! Thoát...");
                return;
            }

            Console.WriteLine($"✅ Đăng nhập thành công!\nToken: {token.Substring(0, 50)}...\n");

            // Bước 2: Kết nối WebSocket
            Console.WriteLine("Bước 2: Kết nối WebSocket");
            Console.Write("WebSocket URL (hoặc Enter để dùng ws://localhost:5000/ws): ");
            string wsUrl = Console.ReadLine() ?? "ws://localhost:5000/ws";
            if (string.IsNullOrWhiteSpace(wsUrl)) wsUrl = "ws://localhost:5000/ws";

            await ConnectWebSocketAsync(wsUrl, token);
        }

        static async Task<string?> LoginAsync(string email, string password)
        {
            try
            {
                using var httpClient = new HttpClient();
                var loginData = new
                {
                    email,
                    password
                };

                var json = JsonSerializer.Serialize(loginData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("http://localhost:5000/api/auth/login", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<JsonElement>(responseBody);

                if (result.TryGetProperty("success", out var success) && success.GetBoolean())
                {
                    return result.GetProperty("token").GetString();
                }
                else
                {
                    Console.WriteLine($"❌ Lỗi: {result.GetProperty("message").GetString()}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception: {ex.Message}");
                return null;
            }
        }

        static async Task ConnectWebSocketAsync(string wsUrl, string token)
        {
            using var ws = new ClientWebSocket();

            try
            {
                Console.WriteLine($"Đang kết nối tới {wsUrl}...");
                await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                Console.WriteLine("✅ WebSocket connected!\n");

                // Gửi JWT token để xác thực
                Console.WriteLine("Đang gửi JWT token để xác thực...");
                await SendMessageAsync(ws, token);

                // Bắt đầu nhận messages trong background
                var receiveTask = Task.Run(async () =>
                {
                    await ReceiveMessagesAsync(ws);
                });

                // Loop để gửi messages
                Console.WriteLine("\n=== Gõ tin nhắn JSON và Enter để gửi (hoặc 'exit' để thoát) ===");
                Console.WriteLine("Ví dụ: {\"type\":\"test\",\"payload\":{\"message\":\"Hello\"}}\n");

                while (ws.State == WebSocketState.Open)
                {
                    Console.Write("> ");
                    string? input = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(input))
                        continue;

                    if (input.ToLower() == "exit")
                        break;

                    await SendMessageAsync(ws, input);
                }

                // Đóng kết nối
                Console.WriteLine("\nĐang đóng kết nối...");
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                await receiveTask;

                Console.WriteLine("✅ Đã đóng kết nối.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WebSocket error: {ex.Message}");
            }
        }

        static async Task SendMessageAsync(ClientWebSocket ws, string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            await ws.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);

            Console.WriteLine($"📤 Sent: {message}");
        }

        static async Task ReceiveMessagesAsync(ClientWebSocket ws)
        {
            var buffer = new byte[1024 * 4];

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("\n🔌 Server đã đóng kết nối.");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Console.WriteLine($"\n📥 Received: {message}");
                        Console.Write("> ");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Receive error: {ex.Message}");
            }
        }
    }
}
