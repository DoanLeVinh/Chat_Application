# Git Workflow - Làm việc nhóm với nhiều nhánh

## 📌 Tình huống của bạn
- **Nhánh của bạn**: `quangthi`
- **Các nhánh khác**: 4 nhánh của các thành viên khác
- **Mục tiêu**: Push code lên → Lấy code người khác → Merge → Tiếp tục code

---

## 🔄 Quy trình chuẩn

### Bước 1: Commit code hiện tại của bạn
```bash
# Xem những file đã thay đổi
git status

# Thêm tất cả file thay đổi vào staging
git add .

# Hoặc thêm từng file cụ thể
git add Server/Program.cs Server/Services/AuthService.cs

# Commit với message rõ ràng
git commit -m "feat: Hoàn thành Auth API với MongoDB Atlas (Ngày 1-4)"
```

### Bước 2: Push code lên nhánh quangthi
```bash
# Lần đầu tiên push nhánh mới lên remote
git push -u origin quangthi

# Các lần sau chỉ cần
git push
```

### Bước 3: Lấy code mới nhất từ remote (tất cả các nhánh)
```bash
# Fetch tất cả thông tin từ remote (không merge)
git fetch origin

# Xem tất cả các nhánh (local và remote)
git branch -a
```

### Bước 4: Merge code từ nhánh khác vào nhánh của bạn

**Cách 1: Merge trực tiếp**
```bash
# Đảm bảo đang ở nhánh quangthi
git checkout quangthi

# Merge nhánh của người khác vào (ví dụ nhánh "nguyen")
git merge origin/nguyen

# Giải quyết conflict nếu có, sau đó commit
git commit -m "merge: Merge code từ nhánh nguyen"
```

**Cách 2: Rebase (cách clean hơn - khuyến nghị)**
```bash
# Pull code mới nhất từ main/develop
git fetch origin
git rebase origin/main

# Nếu có conflict, giải quyết rồi
git rebase --continue
```

### Bước 5: Tích hợp code từ nhiều nhánh

**Phương án A: Merge tất cả vào nhánh chính (main/develop)**
```bash
# Chuyển về nhánh main
git checkout main

# Pull code mới nhất
git pull origin main

# Merge từng nhánh một
git merge origin/quangthi
git merge origin/nguyen
# ... các nhánh khác

# Push lên
git push origin main
```

**Phương án B: Tạo Pull Request (khuyến nghị)**
1. Push nhánh của bạn lên GitHub/GitLab
2. Tạo Pull Request từ `quangthi` → `main`
3. Request review từ team
4. Merge sau khi được approve

---

## ⚠️ Xử lý Conflict

Khi merge bị conflict:
```bash
# Git sẽ báo conflict, mở file conflict và sửa
# File sẽ có dạng:
<<<<<<< HEAD
// Code của bạn
=======
// Code của người khác
>>>>>>> origin/nguyen

# Sửa lại code, xóa các dấu <<<, ===, >>>
# Sau đó add và commit
git add .
git commit -m "fix: Giải quyết conflict"
```

---

## 🎯 Lệnh Git cần nhớ

```bash
# Luôn pull trước khi push
git pull origin quangthi
git push origin quangthi

# Xem lịch sử commit
git log --oneline --graph --all

# Lưu tạm thời thay đổi chưa commit
git stash
git stash pop

# Hủy thay đổi chưa commit
git restore .
```
