# Người 2 - Chat Core Documentation

**Người thực hiện**: Doãn Vịnh  
**Nhiệm vụ**: Chat Core (1-1 + Group + Message seq + Resume foundation) + Test

---

## 📋 Tổng quan

Người 2 chịu trách nhiệm triển khai toàn bộ **Core Chat Logic** bao gồm:

1. **Direct Chat (1-1)** - Chat trực tiếp giữa 2 người với cơ chế chống trùng `directKey`
2. **Group Chat** - Tạo nhóm, quản lý thành viên (add/remove)
3. **Message với Seq** - Gửi tin nhắn với sequence number tăng dần
4. **Resume Foundation** - Nền tảng để Người 3 implement resume (GetMessagesSinceSeq)

---

## 🗂️ Database Schema

### Collection: **conversations**

```javascript
{
  "_id": ObjectId,
  "type": "direct" | "group",
  "title": string | null,  // Chỉ dùng cho group
  "directKey": string | null,  // QUAN TRỌNG - min(userA,userB):max(userA,userB)
  "createdBy": ObjectId,
  "lastSeq": long,  // Seq cuối cùng để tăng seq cho message
  "createdAt": ISODate,
  "updatedAt": ISODate
}

// Index:
// - { directKey: 1 } UNIQUE, SPARSE
```

**Giải thích directKey**:
- Tránh tạo trùng conversation 1-1
- Format: `min(userA,userB):max(userA,userB)`
- Ví dụ: user A (ID: 123), user B (ID: 456) → directKey = "123:456"
- Nếu B chat với A, cũng sẽ tìm được conversation cũ vì directKey giống nhau

### Collection: **conversation_members**

```javascript
{
  "_id": ObjectId,
  "conversationId": ObjectId,
  "userId": ObjectId,
  "role": "owner" | "admin" | "member",
  "joinedAt": ISODate
}

// Index:
// - { conversationId: 1, userId: 1 } UNIQUE
```

### Collection: **messages**

```javascript
{
  "_id": ObjectId,
  "conversationId": ObjectId,
  "senderId": ObjectId,
  "type": "text" | "sticker",
  "content": string,
  "clientMessageId": string,  // Dùng để idempotent
  "seq": long,  // QUAN TRỌNG - Tăng dần theo conversation
  "createdAt": ISODate
}

// Index:
// - { conversationId: 1, seq: 1 }
// - { conversationId: 1, clientMessageId: 1 } UNIQUE
```

**Giải thích Seq**:
- Mỗi conversation có seq riêng, bắt đầu từ 1
- Mỗi message mới sẽ tăng seq lên 1
- Dùng FindOneAndUpdate atomic để tăng `lastSeq` trong conversation
- Người 3 sẽ dùng seq để resume (lấy messages với seq > lastSeq client đã có)

---

## 🔧 Services Implemented

### **ConversationService.cs**

#### Phương thức chính:

**1. GetOrCreateDirectConversationAsync(userAId, userBId)**
```csharp
// Tạo directKey = min:max
// Tìm conversation với directKey
// Nếu không có, tạo mới và thêm 2 members
```

**2. CreateGroupConversationAsync(creatorId, title, memberIds)**
```csharp
// Tạo group conversation
// Thêm creator với role "owner"
// Thêm các members khác với role "member"
```

**3. AddMemberAsync(conversationId, userId, role)**
```csharp
// Thêm member vào conversation
// Check duplicate trước khi insert
```

**4. RemoveMemberAsync(conversationId, userId)**
```csharp
// Xóa member khỏi conversation
// Validation quyền được thực hiện ở handler
```

**5. GetMembersAsync(conversationId)**
```csharp
// Lấy danh sách members của conversation
```

**6. IsMemberAsync(conversationId, userId)**
```csharp
// Kiểm tra user có phải member không
```

**7. GetUserConversationsAsync(userId)**
```csharp
// Lấy tất cả conversations user tham gia
```

---

### **MessageService.cs**

#### Phương thức chính:

**1. CreateMessageAsync(conversationId, senderId, content, type, clientMessageId)**
```csharp
// 1. Check idempotent bằng clientMessageId
// 2. Tăng lastSeq trong conversation (atomic với FindOneAndUpdate)
// 3. Tạo message với seq mới
// 4. Return message
```

**Atomic Seq Increment**:
```csharp
var filter = Builders<Conversation>.Filter.Eq(c => c.Id, conversationId);
var update = Builders<Conversation>.Update.Inc(c => c.LastSeq, 1);
var options = new FindOneAndUpdateOptions<Conversation>
{
    ReturnDocument = ReturnDocument.After
};

var conversation = await _conversations.FindOneAndUpdateAsync(filter, update, options);
var seq = conversation.LastSeq; // Seq vừa tăng
```

**2. GetMessagesAsync(conversationId, limit, beforeSeq)**
```csharp
// Lấy messages để load history
// beforeSeq: Dùng cho pagination (load older messages)
```

**3. GetMessagesSinceSeqAsync(conversationId, sinceSeq, limit)**
```csharp
// QUAN TRỌNG - Người 3 sẽ dùng cho Resume
// Lấy tất cả messages có seq > sinceSeq
// Sắp xếp theo seq tăng dần
```

---

## 📡 WebSocket Events

### **Events Người 2 xử lý:**

#### 1. **send_message**

**Request:**
```json
{
  "type": "send_message",
  "requestId": "req-123",
  "payload": {
    "conversationId": "...",
    "content": "Hello!",
    "messageType": "text",
    "clientMessageId": "msg-client-123"
  }
}
```

**Response (ack):**
```json
{
  "type": "send_message_ok",
  "requestId": "req-123",
  "payload": {
    "messageId": "...",
    "seq": 42
  }
}
```

**Broadcast (tới tất cả members):**
```json
{
  "type": "message_created",
  "payload": {
    "messageId": "...",
    "conversationId": "...",
    "senderId": "...",
    "messageType": "text",
    "content": "Hello!",
    "seq": 42,
    "createdAt": "2025-12-23T..."
  }
}
```

---

#### 2. **create_group**

**Request:**
```json
{
  "type": "create_group",
  "requestId": "req-124",
  "payload": {
    "title": "Nhóm LTM",
    "memberIds": ["user1", "user2", "user3"]
  }
}
```

**Response + Broadcast:**
```json
{
  "type": "conversation_created",
  "payload": {
    "conversationId": "...",
    "title": "Nhóm LTM",
    "type": "group",
    "members": [
      { "userId": "...", "role": "owner", "joinedAt": "..." },
      { "userId": "...", "role": "member", "joinedAt": "..." }
    ],
    "createdAt": "..."
  }
}
```

---

#### 3. **add_member**

**Request:**
```json
{
  "type": "add_member",
  "requestId": "req-125",
  "payload": {
    "conversationId": "...",
    "userId": "user-to-add"
  }
}
```

**Validation:**
- Chỉ owner/admin mới được add member

**Broadcast:**
```json
{
  "type": "member_added",
  "payload": {
    "conversationId": "...",
    "userId": "...",
    "role": "member",
    "joinedAt": "..."
  }
}
```

---

#### 4. **remove_member**

**Request:**
```json
{
  "type": "remove_member",
  "requestId": "req-126",
  "payload": {
    "conversationId": "...",
    "userId": "user-to-remove"
  }
}
```

**Validation:**
- Chỉ owner/admin mới được remove member

**Broadcast:**
```json
{
  "type": "member_removed",
  "payload": {
    "conversationId": "...",
    "userId": "..."
  }
}
```

---

#### 5. **get_conversations**

**Request:**
```json
{
  "type": "get_conversations",
  "requestId": "req-127",
  "payload": {}
}
```

**Response:**
```json
{
  "type": "conversations",
  "requestId": "req-127",
  "payload": {
    "conversations": [
      {
        "conversationId": "...",
        "title": "Nhóm LTM",
        "type": "group",
        "createdAt": "...",
        "updatedAt": "..."
      }
    ]
  }
}
```

---

#### 6. **get_messages**

**Request:**
```json
{
  "type": "get_messages",
  "requestId": "req-128",
  "payload": {
    "conversationId": "...",
    "limit": 50,
    "beforeSeq": 100  // Optional - for pagination
  }
}
```

**Response:**
```json
{
  "type": "messages",
  "requestId": "req-128",
  "payload": {
    "conversationId": "...",
    "messages": [
      {
        "messageId": "...",
        "senderId": "...",
        "messageType": "text",
        "content": "Hello",
        "seq": 42,
        "createdAt": "..."
      }
    ]
  }
}
```

---

## 🧪 Testing

### **Demo Accounts (Đã seed trong MongoDB)**

```
Email: vinh@demo.com | Password: demo123 | UserId: 694a3fb3291ebb4beb88f145
Email: quang@demo.com | Password: demo123 | UserId: 694a3fb3291ebb4beb88f146
Email: huyen@demo.com | Password: demo123 | UserId: 694a3fb3291ebb4beb88f147
Email: suong@demo.com | Password: demo123 | UserId: 694a3fb3291ebb4beb88f148
```

### **Test Cases**

**1. Test Direct Chat (1-1)**
```
✅ Login với vinh@demo.com
✅ Tạo direct chat với quang@demo.com
✅ Gửi message "Hello from Vịnh"
✅ Verify seq = 1
✅ Gửi message thứ 2
✅ Verify seq = 2
✅ Check directKey unique (không tạo trùng conversation)
```

**2. Test Group Chat**
```
✅ Login với vinh@demo.com
✅ Tạo group "Nhóm Test" với members: quang, huyen
✅ Verify creator có role = "owner"
✅ Add member suong
✅ Verify broadcast member_added
✅ Remove member huyen
✅ Verify broadcast member_removed
```

**3. Test Message Seq**
```
✅ Gửi 10 messages liên tiếp
✅ Verify seq tăng từ 1 → 10
✅ Check không có seq trùng
✅ Verify atomic increment (không bị race condition)
```

**4. Test Idempotent Message**
```
✅ Gửi message với clientMessageId = "test-msg-1"
✅ Gửi lại message với cùng clientMessageId
✅ Verify không tạo message mới (return message cũ)
```

---

## 📂 File Structure

```
Server/
├── Models/
│   ├── Message.cs           # Message model với seq
│   ├── ChatRoom.cs          # Conversation + ConversationMember
│   └── User.cs              # User model (Người 1)
├── Services/
│   ├── ConversationService.cs  # Direct/Group logic, Members
│   ├── MessageService.cs       # Send message, Seq increment
│   └── SeedDataService.cs      # Seed demo data
├── WebSockets/
│   ├── WsHandler.cs            # WebSocket routing
│   ├── WsConnectionManager.cs  # Connection management
│   └── Handlers/
│       ├── ConversationHandlers.cs  # create_group, add_member, remove_member
│       └── MessageHandlers.cs       # send_message, get_messages
├── Database/
│   └── MongoDBContext.cs       # MongoDB connection + indexes
└── Program.cs                  # Server startup

Client/
├── js/
│   ├── socket-real.js      # WebSocket client (kết nối server thật)
│   ├── app.js              # UI logic + event handlers
│   └── ui.js               # Display functions
├── chat.html               # Main chat UI
└── index.html              # Login page
```

---

## 🔗 Phối hợp với các Người khác

### **Người 1 (Quang Thi) - Auth & Server**
- Người 1 sẽ implement Auth thật (JWT verify)
- Hiện tại đang dùng auth mock để Người 2 test được
- Người 2 đã chuẩn bị sẵn `userId` trong tất cả handlers

### **Người 3 (Huyền) - Reconnect & Resume**
- Người 2 đã implement `GetMessagesSinceSeqAsync(conversationId, sinceSeq)`
- Người 3 sẽ dùng method này để resume:
  ```csharp
  // Client gửi sinceSeqByConversation: { "conv1": 42, "conv2": 15 }
  // Server gọi GetMessagesSinceSeqAsync cho từng conversation
  // Trả về tất cả messages có seq > sinceSeq
  ```

### **Người 4 (Sương) - Search, Reaction, Pin, Sticker**
- Message type "sticker" đã được support
- Người 4 sẽ implement:
  - Search user để tạo direct chat
  - Reaction, Pin (collection riêng)
  - Sticker pack (validate sticker_code)

---

## ⚠️ Lưu ý quan trọng

### **1. DirectKey PHẢI UNIQUE**
```csharp
var directKey = GetDirectKey(userAId, userBId);
// KHÔNG BAO GIỜ được phép tạo 2 conversations với cùng directKey
```

### **2. Seq PHẢI Atomic**
```csharp
// SỬ DỤNG FindOneAndUpdateAsync với Inc
// TUYỆT ĐỐI KHÔNG dùng: lastSeq = conversation.LastSeq + 1 (race condition)
```

### **3. ClientMessageId cho Idempotent**
```csharp
// Client gửi kèm clientMessageId duy nhất
// Server check trùng trước khi insert
// Nếu trùng, return message cũ (không tạo mới)
```

### **4. Broadcast đúng members**
```csharp
// Lấy danh sách members của conversation
// Broadcast tới TẤT CẢ members (kể cả sender)
```

---

## 🚀 Cách chạy

### **1. Start MongoDB**
```bash
# MongoDB đã chạy dưới dạng Windows service
Get-Service MongoDB
```

### **2. Start Server**
```bash
cd C:\LTM\CHAT\Chat_Application\Server
dotnet run
```

**Output:**
```
✅ Connected to MongoDB: ChatAppDB
✅ Database indexes created
🌱 Seeding database...
✅ Created 4 demo users
✅ Created direct conversation: ...
✅ Created group conversation: ...
🎉 Seed completed successfully!
🚀 Chat Server started on ws://localhost:5000/ws
```

### **3. Start Client**
```bash
# Mở Client/index.html trong browser
# Hoặc dùng Live Server extension (http://localhost:5500)
```

### **4. Test Flow**
1. Login với `vinh@demo.com` / `demo123`
2. Xem danh sách conversations (đã có 1 direct + 1 group từ seed data)
3. Click vào conversation để xem messages
4. Gửi message mới → Verify seq tăng
5. Mở tab mới, login với `quang@demo.com`
6. Verify nhận được message real-time

---

## 📊 Demo Data Structure

### **Users**
- vinh@demo.com (Người 2 - Chat Core)
- quang@demo.com (Người 1 - Auth)
- huyen@demo.com (Người 3 - Reconnect)
- suong@demo.com (Người 4 - Search/Reaction)

### **Conversations**
1. **Direct**: Vịnh ↔ Quang
   - DirectKey: `694a3fb3291ebb4beb88f145:694a3fb3291ebb4beb88f146`
   - Messages: 3 messages (seq 1-3)

2. **Group**: "Nhóm Chat App LTM"
   - Members: Vịnh (owner), Quang, Huyền, Sương
   - Messages: 4 messages (seq 1-4)

---

## ✅ Checklist Hoàn thành

- [x] Database Schema (conversations, conversation_members, messages)
- [x] Indexes (directKey unique, seq compound, clientMessageId unique)
- [x] ConversationService (Direct, Group, Members)
- [x] MessageService (Send với seq atomic, GetMessages, GetMessagesSinceSeq)
- [x] WebSocket Handlers (send_message, create_group, add/remove_member)
- [x] Client UI (Login, Conversation list, Message display)
- [x] Client WebSocket (Real connection to server)
- [x] Seed Data (4 users, 2 conversations, 7 messages)
- [x] Testing (Tất cả flow đã test thành công)
- [x] Documentation (File này)

---

## 📞 Hỗ trợ Resume cho Người 3

**Method đã chuẩn bị:**
```csharp
public async Task<List<Message>> GetMessagesSinceSeqAsync(
    string conversationId, 
    long sinceSeq, 
    int limit = 100)
{
    return await _messages
        .Find(m => m.ConversationId == conversationId && m.Seq > sinceSeq)
        .SortBy(m => m.Seq)
        .Limit(limit)
        .ToListAsync();
}
```

**Cách Người 3 dùng:**
1. Client lưu `lastSeqByConversation` trong localStorage
2. Khi reconnect, gửi event `resume` với `sinceSeqByConversation`
3. Server gọi `GetMessagesSinceSeqAsync` cho từng conversation
4. Return tất cả missed messages theo thứ tự seq

---

**Document hoàn thành bởi**: Doãn Vịnh (Người 2)  
**Ngày**: 23/12/2025  
**Status**: ✅ Production Ready
