---
id: lich-su-commit-github
title: Lịch Sử Git Commits & Minh Chứng Kho Lưu Trữ
sidebar_position: 1
---

# Lịch Sử Git Commits & Minh Chứng Kho Lưu Trữ

## 1. Thông Tin Chung Kho Lưu Trữ & Thành Viên Nhóm

* **Kho lưu trữ (Repository):** [`https://github.com/pliam1304/DoAnLapTrinhMang`](https://github.com/pliam1304/DoAnLapTrinhMang)
* **Nhánh phát triển chính:** `main`
* **Thành viên nhóm thực hiện (3 sinh viên):**
  1. **Huỳnh Phúc Lâm**
  2. **Cao Khôi Nguyên**
  3. **Ngô Quyền Linh**

:::note[Minh bạch phân công cá nhân]
Tại thời điểm lập tài liệu này, nhiệm vụ lập trình chi tiết giữa từng cá nhân trong nhóm vẫn đang để mở theo kế hoạch chung (chưa ấn định bảng chia phần riêng). Bảng lịch sử dưới đây trích xuất nguyên trạng từ `git log` của nhánh `main` để phản ánh đúng dữ liệu thực tế trên GitHub.
:::

---

## 2. Danh Sách Các Commit Thực Tế Đã Ghi Nhận Trên Nhánh `main`

Kết quả trích xuất từ lệnh `git log --stat` cho đến thời điểm hiện tại trước khi hoàn tất bổ sung bộ tài liệu `docs/` và `tests/Nega.Tests`:

| Commit Hash | Ngày Ghi Nhận | Tác Giả Git | Quy Mô Thay Đổi | Thông Điệp Commit |
|:---:|:---:|---|---|---|
| [`cfd1257`](https://github.com/pliam1304/DoAnLapTrinhMang/commit/cfd1257f8e4813207d5bce92c5ce5ec0640064ce) | 2026-11-19 | `pliam1304` | `10 files` (`+1131 / -121`) | `feat: cap nhat icon Nega, tai khoan phan quyen va chia se QR` (`QrSvgGenerator.cs`, `AccountManager.cs`, cập nhật `ServerApiEndpoints.cs`, `index.html`, `nega-app.js`, icon mới) |
| [`2027769`](https://github.com/pliam1304/DoAnLapTrinhMang/commit/2027769708279622deac14cfec3f4fd83538dbac) | 2026-11-19 | `Nguyen06-CSE` | `1 file` (`+1 / -1`) | `thêm khung` (`README.md`) |
| [`631c87a`](https://github.com/pliam1304/DoAnLapTrinhMang/commit/631c87ae714638126210ed82006434f31ba4ccc7) | 2026-11-18 | `pliam1304` | `38 files` (`+5569`) | `feat: initial Nega lab monitoring & remote control with security hardening` (khởi tạo toàn bộ solution 3 phân hệ `Nega.Common`, `Nega.Server`, `Nega.Agent`, `.env.example`, `.gitignore`) |

---

## 3. Quy Ước Thông Điệp Commit Chuẩn Hóa Trong Dự Án

Để bảo đảm lịch sử phát triển dễ đọc và tiện theo dõi khi nhóm tiếp tục mở rộng Giai đoạn 2, các commit tiếp theo thống nhất theo dạng mô tả tóm tắt kết hợp danh sách gạch đầu dòng chi tiết:

```bash
git commit -m "feat: <tên tính năng ngắn gọn>" \
  -m "- Chi tiết thay đổi kỹ thuật 1" \
  -m "- Chi tiết thay đổi kỹ thuật 2" \
  -m "- Kết quả build và chạy dotnet test"
```
