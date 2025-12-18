# Hướng dẫn Test API với MongoDB Atlas

## ✅ Cấu hình hiện tại

**MongoDB Connection String:**
```
mongodb+srv://thiquang2729:***@sosapp.p7tce7j.mongodb.net/
Database: ChatApplicationDB
```

**Server đang chạy tại:** `http://localhost:5000`

## 🧪 Cách test API

### Phương án 1: Dùng Swagger UI (Khuyến nghị)
1. Mở trình duyệt tại: `http://localhost:5000/swagger`
2. Bạn sẽ thấy giao diện Swagger với 2 endpoints:
   - POST `/api/auth/register`
   - POST `/api/auth/login`

### Phương án 2: Dùng PowerShell/CMD

#### Test Register:
Dùng Postman
1. Tạo request POST tới `http://localhost:5000/api/auth/register`
2. Chọn Body → raw → JSON
3. Nhập:
```json
{
  "email": "test@example.com",
  "password": "Test123456",
  "displayName": "Nguyen Van Test"
}
```

## 📊 Kiểm tra MongoDB Atlas
1. Đăng nhập vào [MongoDB Atlas](https://cloud.mongodb.com/)
2. Vào Collections → Database: `ChatApplicationDB` → Collection: `users`
3. Sau khi đăng ký thành công, bạn sẽ thấy user mới xuất hiện tại đây

## ⚠️ Lưu ý bảo mật
> **WARNING**  
> Connection string của bạn chứa password. Trong production, nên dùng biến môi trường hoặc Azure Key Vault để bảo mật thông tin này.
