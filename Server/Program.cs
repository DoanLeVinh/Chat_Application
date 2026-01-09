using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ChatServer.Database;
using ChatServer.Services;
using ChatServer.WebSockets;
using ChatServer.Utils;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serializer để dùng camelCase
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = false;
});

// Load configuration
var configPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Config", "appsettings.json");
builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);

// Register MongoDB
var mongoConnectionString = builder.Configuration["MongoDB:ConnectionString"] ?? "";
var mongoDatabaseName = builder.Configuration["MongoDB:DatabaseName"] ?? "ChatAppDB";
builder.Services.AddSingleton(new MongoDBContext(mongoConnectionString, mongoDatabaseName));

// ============ ĐĂNG KÝ SERVICES CỦA NGƯỜI 3 ============

// 1. ConnectionManager - QUAN TRỌNG: phải đăng ký TRƯỚC
builder.Services.AddSingleton<ConnectionManager>();

// 2. PresenceService - phụ thuộc vào ConnectionManager
builder.Services.AddSingleton<PresenceService>();

// 3. ResumeService - phụ thuộc vào MongoDBContext
builder.Services.AddSingleton<ResumeService>();

// 4. GracefulShutdown - hosted service
builder.Services.AddHostedService<GracefulShutdown>();

// ============ CÁC SERVICES HIỆN CÓ CỦA NHÓM ============

// Register services hiện có
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddSingleton<MessageService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<WsConnectionManager>();
builder.Services.AddSingleton<SeedDataService>();

// JWT Settings
var jwtSecretKey = builder.Configuration["JwtSettings:SecretKey"] ?? "YourSuperSecretKey32CharactersLong!";
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "ChatServer";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "ChatClient";
var jwtExpiryMinutes = int.Parse(builder.Configuration["JwtSettings:ExpiryInMinutes"] ?? "1440");

// Auth Service
builder.Services.AddSingleton(sp => new AuthService(
    sp.GetRequiredService<MongoDBContext>(),
    jwtSecretKey,
    jwtIssuer,
    jwtAudience,
    jwtExpiryMinutes
));

// Configure JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSecretKey))
    };
});

builder.Services.AddControllers();

// Enable CORS for client
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5500", "http://127.0.0.1:5500", "http://localhost:3000", "http://127.0.0.1:5501", "http://localhost:8080", "http://127.0.0.1:8080")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Seed demo data
var seedService = app.Services.GetRequiredService<SeedDataService>();
await seedService.SeedAsync();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Enable WebSocket
app.UseWebSockets();

// WebSocket endpoint
app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var manager = context.RequestServices.GetRequiredService<WsConnectionManager>();
        var conversationService = context.RequestServices.GetRequiredService<ConversationService>();
        var messageService = context.RequestServices.GetRequiredService<MessageService>();
        var userService = context.RequestServices.GetRequiredService<UserService>();
        
        // ============ LẤY CÁC SERVICES CỦA NGƯỜI 3 ============
        var presenceService = context.RequestServices.GetRequiredService<PresenceService>();
        var resumeService = context.RequestServices.GetRequiredService<ResumeService>();
        var connectionManager = context.RequestServices.GetRequiredService<ConnectionManager>();
        
        // Cập nhật lời gọi WsHandler với đầy đủ tham số
        await WsHandler.HandleWebSocketAsync(
            webSocket, 
            manager, 
            conversationService, 
            messageService, 
            userService,
            presenceService,  // THÊM
            resumeService,    // THÊM  
            connectionManager // THÊM
        );
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

// Map Controllers
app.MapControllers();

// Health check
app.MapGet("/health", () => new { status = "ok", timestamp = DateTime.UtcNow });

// ============ TEST SERVICES NGƯỜI 3 ============

// Lấy các services của Người 3 để test (CHỈ lấy những service chưa được khai báo)
var connectionManagerTest = app.Services.GetRequiredService<ConnectionManager>();
var resumeServiceTest = app.Services.GetRequiredService<ResumeService>();

Console.WriteLine("✅ [NGƯỜI 3] Services registered successfully:");
Console.WriteLine($"   • ConnectionManager: Ready");
Console.WriteLine($"   • PresenceService: Ready");
Console.WriteLine($"   • ResumeService: Ready");
Console.WriteLine($"   • GracefulShutdown: Registered as HostedService");

// Test: Khởi tạo và log
Console.WriteLine($"   • MongoDB Database: {mongoDatabaseName}");
Console.WriteLine($"   • Presence Collection: Ready (via MongoDBContext)");

// ============ LOG STARTUP ============

Console.WriteLine("\n🚀 Chat Server started on ws://localhost:5000/ws");
Console.WriteLine("📦 MongoDB: " + mongoDatabaseName);
Console.WriteLine("🔐 Auth API: http://localhost:5000/api/auth");
Console.WriteLine("✅ Health check: http://localhost:5000/health");
Console.WriteLine("\n===== NGƯỜI 3 - PRESENCE + RESUME READY =====");

app.Run();