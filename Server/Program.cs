using System.Text;
using ChatServer.Database;
using ChatServer.Services;
using ChatServer.WebSockets;
using ChatServer.WebSockets.Handlers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Database
var dbConfig = new DatabaseConfig
{
    ConnectionString = builder.Configuration.GetConnectionString("MongoDB")!,
    DatabaseName = builder.Configuration.GetConnectionString("DatabaseName")!
};

var jwtSettings = new JwtSettings
{
    SecretKey = builder.Configuration["JwtSettings:SecretKey"]!,
    Issuer = builder.Configuration["JwtSettings:Issuer"]!,
    Audience = builder.Configuration["JwtSettings:Audience"]!,
    ExpiryInMinutes = int.Parse(builder.Configuration["JwtSettings:ExpiryInMinutes"]!)
};

// Đăng ký services
builder.Services.AddSingleton(dbConfig);
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<MongoDBContext>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<AuthService>();

// WebSocket services
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddScoped(sp => new WebSocketHandler(
    sp.GetRequiredService<ConnectionManager>(),
    sp.GetRequiredService<UserRepository>(),
    sp.GetRequiredService<MessageRouter>(),
    jwtSettings.SecretKey
));

// Cấu hình JWT Authentication
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
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSettings.SecretKey))
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Cấu hình CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Cấu hình WebSocket
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromMinutes(2)
};
app.UseWebSockets(webSocketOptions);

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// WebSocket endpoint
app.Map("/ws", async (HttpContext context, WebSocketHandler handler) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await handler.HandleWebSocketAsync(context, webSocket);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.MapControllers();

Console.WriteLine("Chat Server Starting...");
Console.WriteLine($"Server is running on: {builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5000"}");

app.Run();
