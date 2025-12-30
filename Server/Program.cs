using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using ChatServer.Database;
using ChatServer.Services;
using ChatServer.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Hosting;


var builder = WebApplication.CreateBuilder(args);

// Configure JSON serializer để dùng camelCase (type, requestId, payload)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = false;
});

// Load configuration từ current directory
var configPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Config", "appsettings.json");
builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);

// Register MongoDB
var mongoConnectionString = builder.Configuration["MongoDB:ConnectionString"] ?? "";
var mongoDatabaseName = builder.Configuration["MongoDB:DatabaseName"] ?? "ChatAppDB";
builder.Services.AddSingleton(new MongoDBContext(mongoConnectionString, mongoDatabaseName));

// Register services
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddSingleton<MessageService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<WsConnectionManager>();
builder.Services.AddSingleton<SeedDataService>();
// Đăng ký services của người thứ 3
builder.Services.AddSingleton<PresenceResumeManager>();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<ResumeService>();

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
        
        await WsHandler.HandleWebSocketAsync(webSocket, manager, conversationService, messageService, userService);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

// Health check
app.MapGet("/health", () => new { status = "ok", timestamp = DateTime.UtcNow });

Console.WriteLine("🚀 Chat Server started on ws://localhost:5000/ws");
Console.WriteLine("📦 MongoDB: " + mongoDatabaseName);
Console.WriteLine("✅ Health check: http://localhost:5000/health");

// Graceful shutdown handling
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
appLifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("🛑 Server is shutting down gracefully...");
    
    var presenceManager = app.Services.GetRequiredService<PresenceResumeManager>();
    
    // Broadcast server going down message
    _ = presenceManager.BroadcastServerGoingDownAsync(10);
    
    // Wait a bit for messages to be sent
    Thread.Sleep(3000);
    
    Console.WriteLine("👋 Server shutdown complete");
});

// Handle Ctrl+C
Console.CancelKeyPress += (sender, e) =>
{
    Console.WriteLine("\n🛑 Ctrl+C detected, initiating graceful shutdown...");
    e.Cancel = true; // Prevent immediate termination
    
    var presenceManager = app.Services.GetRequiredService<PresenceResumeManager>();
    _ = presenceManager.BroadcastServerGoingDownAsync(5);
    
    // Give time for broadcast
    Thread.Sleep(3000);
    Environment.Exit(0);
};
app.Run();
