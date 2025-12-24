# Chat Application

Ứng dụng chat đơn giản sử dụng C# Socket, HTML/CSS/JS và MongoDB Atlas.

## Công nghệ sử dụng

- **Backend:** C# .NET 8.0 với Socket Programming
- **Frontend:** HTML, CSS, JavaScript
- **Database:** MongoDB Atlas
- **IDE:** Visual Studio Code

## Cấu trúc dự án

```
Chat_Application/
├── Server/                     # C# Socket Server
│   ├── Models/                # Data models
│   ├── Services/              # Business logic
│   ├── Database/              # MongoDB connection
│   ├── Utils/                 # Utilities
│   ├── Program.cs            # Entry point
│   └── Server.csproj         # Project file
├── Client/                    # Frontend
│   ├── css/                  # Stylesheets
│   ├── js/                   # JavaScript files
│   ├── assets/               # Images, icons
│   ├── index.html           # Login page
│   └── chat.html            # Chat interface
├── Config/                    # Configuration
│   └── appsettings.json     # App settings
└── Shared/                    # Shared code
    └── Constants.cs
```

## Cài đặt và chạy

### Yêu cầu
- .NET SDK 8.0 ✅
- MongoDB ✅
- VS Code với C# Dev Kit extension

### Chạy Server

```bash
cd Server
dotnet restore
dotnet run
```
Get-Service MongoDB

### Chạy Client

Mở file `Client/index.html` trong trình duyệt.

## TODO - Các tính năng cần thực hiện

### Server (C#)
- [ ] Implement SocketServer để lắng nghe kết nối
- [ ] Kết nối MongoDB Atlas
- [ ] Tạo Models (User, Message, ChatRoom)
- [ ] Implement Services (UserService, MessageService)
- [ ] Xử lý đăng nhập/đăng ký
- [ ] Xử lý gửi/nhận tin nhắn
- [ ] Broadcast tin nhắn cho nhiều clients

### Client (HTML/CSS/JS)
- [ ] Thiết kế giao diện đăng nhập
- [ ] Thiết kế giao diện chat
- [ ] Kết nối Socket với server
- [ ] Xử lý gửi/nhận tin nhắn
- [ ] Hiển thị danh sách người dùng online
- [ ] Thông báo khi có tin nhắn mới

### Database
- [ ] Tạo collections trong MongoDB
- [ ] Schema cho Users
- [ ] Schema cho Messages
- [ ] Schema cho ChatRooms

## Ghi chú

- MongoDB connection string đã được cấu hình trong `Config/appsettings.json`
- Server mặc định chạy trên port 8888
- Tất cả các file đã được tạo với TODO comments để hướng dẫn implementation
