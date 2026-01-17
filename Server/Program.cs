using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ChatServer.Database;
using ChatServer.Services;
using ChatServer.WebSockets;
using System.Text;
using System.Text.Json;

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

// JWT Settings
var jwtSecretKey = builder.Configuration["JwtSettings:SecretKey"] ?? "YourSuperSecretKey32CharactersLong!";
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "ChatServer";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "ChatClient";
var jwtExpiryMinutes = int.Parse(builder.Configuration["JwtSettings:ExpiryInMinutes"] ?? "1440");

// Register services
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddSingleton<MessageService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<WsConnectionManager>();
builder.Services.AddSingleton<SeedDataService>();
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

// Enable CORS for client (AllowAnyOrigin for LAN testing)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
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
        var db = context.RequestServices.GetRequiredService<MongoDBContext>();
        var _db = context.RequestServices.GetRequiredService<MongoDBContext>();

        await WsHandler.HandleWebSocketAsync(webSocket, manager, conversationService, messageService, db, userService, _db);
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

Console.WriteLine("🚀 Chat Server started on ws://localhost:5000/ws");
Console.WriteLine("📦 MongoDB: " + mongoDatabaseName);
Console.WriteLine("🔐 Auth API: http://localhost:5000/api/auth");
Console.WriteLine("✅ Health check: http://localhost:5000/health");

app.Run();
