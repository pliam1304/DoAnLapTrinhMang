---
id: huong-dan-cai-dat-khoi-chay
title: Hướng Dẫn Cài Đặt & Khởi Chạy Hệ Thống
sidebar_position: 4
---

# Hướng Dẫn Cài Đặt & Khởi Chạy Hệ Thống

## 1. Yêu Cầu Môi Trường

* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (hỗ trợ Windows 10/11, macOS Apple Silicon/Intel và Linux).
* Trình duyệt web hiện đại (Chrome, Edge, Safari, Firefox) để truy cập Nega Dashboard tại cổng `5050`.
* Nếu chạy thử trong phòng LAN nhiều máy thật: các máy nằm chung dải mạng LAN/Wi-Fi (hoặc sử dụng `--server <IP>` nếu mạng Wi-Fi bật chế độ chặn gói Broadcast).

---

## 2. Cấu Hình Biến Môi Trường Cục Bộ (`.env`)

Hệ thống mặc định chạy an toàn ngay sau khi clone mã nguồn mà không bắt buộc tạo `.env`. Để tùy chỉnh cổng mạng, đặt mã PIN cố định hoặc bật xác thực cụm LAN `HMAC-SHA256`, sao chép tệp mẫu từ thư mục gốc `nega/`:

```bash
cp .env.example .env
```

Các biến cấu hình quan trọng hỗ trợ trong [`.env.example`](../../.env.example):

| Biến Môi Trường | Giá Trị Mặc Định | Ý Nghĩa |
|---|---|---|
| `NEGA_UDP_PORT` | `9999` | Cổng phát và lắng nghe gói UDP Broadcast (`DISCOVER` / `ANNOUNCE`) |
| `NEGA_TCP_PORT` | `8888` | Cổng máy chủ TCP Control & truyền luồng ảnh nhị phân JPEG |
| `NEGA_HTTP_PORT` | `5050` | Cổng Web Dashboard & REST API của `Nega.Server` |
| `NEGA_BIND_HOST` | `0.0.0.0` | Cho phép máy khác trong mạng LAN truy cập vào Dashboard (`127.0.0.1` nếu chỉ mở nội bộ) |
| `NEGA_ADMIN_PIN` | *(tự sinh ngẫu nhiên)* | Mã PIN đăng nhập tài khoản `admin` (`Host`) từ máy khác trong LAN |
| `NEGA_REQUIRE_PIN_ON_LOCALHOST` | `false` | Đặt `true` nếu muốn bắt buộc đăng nhập cả khi mở từ `localhost` |
| `NEGA_CLUSTER_KEY` | *(trống - Dev mở)* | Khóa bí mật chung bật chữ ký `HMAC-SHA256` cho gói tin UDP & TCP Handshake |
| `NEGA_ALLOW_REAL_SHUTDOWN` | `false` | Giữ `false` để mọi lệnh tắt máy chạy Safe Mode (`DryRun`); chỉ bật `true` khi demo thật |
| `NEGA_ALLOW_OS_CAPTURE` | `false` | Bật `true` trên macOS thật để chụp màn hình hệ điều hành qua `/usr/sbin/screencapture` |
| `NEGA_MASK_HOST_IDENTITY` | `true` | Ẩn tên máy tính và tên đăng nhập OS thật khỏi UI và log |
| `NEGA_DEFAULT_SIMULATED_COUNT` | `5` | Số lượng máy trạm giả lập (`0..16`) tự khởi tạo khi bật `Nega.Server` |

---

## 3. Biên Dịch & Khởi Chạy `Nega.Server`

Từ thư mục gốc `nega/`:

```bash
dotnet build Nega.slnx
dotnet run --project src/Nega.Server
```

Sau khi khởi động, màn hình Console sẽ hiển thị thông tin tổng quan cùng mã PIN phiên (nếu không đặt sẵn trong `.env`). Mở trình duyệt tại:
* Trên chính máy chủ: **`http://localhost:5050`**
* Từ laptop/điện thoại khác trong cùng LAN: **`http://<IP_LAN_SERVER>:5050`** (nhập tài khoản được `Host` cấp hoặc quét mã QR mời).

---

## 4. Khởi Chạy `Nega.Agent` Trên Máy Trạm Khác Hoặc Tham Số CLI

Chạy trực tiếp trên máy trạm khác trong cùng phòng LAN để tự động tìm Server qua UDP Broadcast (`:9999`):

```bash
dotnet run --project src/Nega.Agent
```

Các tham số dòng lệnh được hỗ trợ tại [`src/Nega.Agent/Program.cs`](../../src/Nega.Agent/Program.cs):

* **Chỉ định thủ công IP Server (dùng khi Wi-Fi trường chặn gói Broadcast):**
  ```bash
  dotnet run --project src/Nega.Agent -- --server 192.168.1.50
  ```
* **Khởi chạy hàng loạt máy trạm giả lập kết nối từ một máy khác (`1..30` máy):**
  ```bash
  dotnet run --project src/Nega.Agent -- --simulate 8 --server 192.168.1.50
  ```
* **Định danh tên máy và IP tùy chọn khi demo LAN:**
  ```bash
  dotnet run --project src/Nega.Agent -- --name LAB-PC-12 --ip 10.10.0.112 --server 192.168.1.50
  ```

---

## 5. Chạy Bộ Kiểm Thử Tự Động (Unit Tests)

Thực thi toàn bộ 34 unit test trong `tests/Nega.Tests`:

```bash
dotnet test tests/Nega.Tests/Nega.Tests.csproj
```
