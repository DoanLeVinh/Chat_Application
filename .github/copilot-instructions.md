# Chat Application - AI Agent Instructions

## Architecture Overview

This is a **real-time chat application** with a client-server architecture:
- **Backend**: C# .NET 8.0 TCP socket server (NOT WebSocket/SignalR) in [Server/](Server/)
- **Frontend**: Vanilla HTML/CSS/JS connecting via raw TCP sockets in [Client/](Client/)
- **Database**: MongoDB Atlas for persistence
- **Language**: Vietnamese (variable names, comments, README)

### Critical Architectural Decision
The project uses **raw TCP sockets** (System.Net.Sockets), not HTTP/WebSocket. Client-server communication is over port 8888 with custom binary/text protocol.

## Project Structure

```
Server/                 # C# socket server
  ├── Models/          # MongoDB entities (User, Message, ChatRoom)
  ├── Services/        # Business logic (SocketServer, UserService, MessageService)
  ├── Database/        # MongoDBContext for data access
  └── Utils/           # Logger and helpers
Client/
  ├── js/socket.js     # Client-side TCP socket handler
  ├── js/app.js        # Main application logic
  └── js/ui.js         # UI interactions
Config/appsettings.json # MongoDB connection string + server port
Shared/Constants.cs     # Shared constants (ports, limits, etc.)
```

## Development Workflow

### Build & Run
```bash
# Server (from repo root)
cd Server
dotnet restore
dotnet run

# Client
# Open Client/index.html in browser
```

### Database Connection
- MongoDB Atlas connection string in [Config/appsettings.json](Config/appsettings.json)
- Database: `ChatAppDB`
- Collections: Users, Messages, ChatRooms (create as needed)

## Implementation Patterns

### 1. MongoDB Models
All models in [Server/Models/](Server/Models/) should:
- Use `MongoDB.Bson.Serialization.Attributes` for ID mapping
- Include `[BsonId]` and `[BsonRepresentation(BsonType.ObjectId)]` for `Id` property
- Example structure:
  ```csharp
  public class User {
      [BsonId]
      [BsonRepresentation(BsonType.ObjectId)]
      public string Id { get; set; }
      public string Username { get; set; }
      // ... other properties
  }
  ```

### 2. Service Layer Pattern
Services in [Server/Services/](Server/Services/) follow constructor injection:
- Inject `MongoDBContext` for database access
- Keep business logic separate from socket handling
- Example: `UserService` handles auth/user CRUD, `SocketServer` handles connections

### 3. Socket Communication Protocol
Define a simple text-based protocol in [Shared/Constants.cs](Shared/Constants.cs):
- Message format: `COMMAND:DATA`
- Examples: `LOGIN:username:password`, `MESSAGE:roomId:content`
- Both client and server must parse/format consistently

### 4. Client-Side Socket Pattern
[Client/js/socket.js](Client/js/socket.js) uses WebSocket API (browser limitation) connecting to server:
- **Note**: Browser cannot use raw TCP sockets directly
- **Solution**: Server must support WebSocket upgrade OR use a WebSocket-to-TCP proxy
- Alternative: Use HTTP long-polling for development

## Key Configuration

- **Server Port**: `8888` (from [Config/appsettings.json](Config/appsettings.json))
- **Max Connections**: `100`
- **MongoDB Connection**: Already configured, credentials embedded
- **.NET Version**: `net8.0` (see [Server/Server.csproj](Server/Server.csproj))

## TODO Tracking

All files contain `TODO:` comments indicating incomplete implementations. Priority order:
1. [Server/Database/MongoDBContext.cs](Server/Database/MongoDBContext.cs) - Initialize DB connection
2. [Server/Models/](Server/Models/) - Define all model properties
3. [Server/Services/SocketServer.cs](Server/Services/SocketServer.cs) - Implement TCP listener
4. [Server/Services/UserService.cs](Server/Services/UserService.cs) - Auth logic
5. [Client/js/socket.js](Client/js/socket.js) - Client connection handler

## Critical Gotchas

1. **Browser TCP Limitation**: Browsers cannot open raw TCP sockets. Consider:
   - Implementing WebSocket support in C# server (add `System.Net.WebSockets`)
   - Using HTTP polling for MVP
   - Running a local proxy

2. **MongoDB Connection**: Connection string in config file contains actual credentials - avoid committing sensitive data

3. **Namespace Convention**: Root namespace is `ChatServer`, not `Chat_Application`

4. **Vietnamese Language**: Keep all user-facing text and comments in Vietnamese as per existing README

## When Adding Features

- Add new message types to protocol in [Shared/Constants.cs](Shared/Constants.cs)
- Update both client ([Client/js/socket.js](Client/js/socket.js)) and server ([Server/Services/SocketServer.cs](Server/Services/SocketServer.cs)) handlers
- Create corresponding service methods in [Server/Services/](Server/Services/)
- Add MongoDB collections/queries in [Server/Database/MongoDBContext.cs](Server/Database/MongoDBContext.cs)
