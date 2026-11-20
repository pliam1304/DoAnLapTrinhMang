---
id: tong-ket-chuc-nang
title: Bảng Ma Trận Đối Chiếu & Tổng Kết Chức Năng
sidebar_position: 1
---

# Bảng Ma Trận Đối Chiếu & Tổng Kết Chức Năng

## 1. Đánh Giá Mức Độ Hoàn Thiện Tổng Thể

Hệ thống **Nega** đã hoàn thiện trọn vẹn kiến trúc 3 phân hệ (`Nega.Common`, `Nega.Server`, `Nega.Agent`), tầng giao tiếp Socket UDP/TCP nhị phân, Web Dashboard phân quyền và bộ kiểm thử tự động xUnit; đồng thời tách biệt rõ ràng phần định hướng mở rộng 2 vai trò thi cử theo đề cương để bảo đảm tính minh bạch khi nghiệm thu.

* **Nhóm chức năng lõi mạng & Dashboard (`F01`–`F07`):** `7/7` phân hệ `[Hoàn thành]`
* **Cơ chế bảo mật & an toàn mặc định (`.env`, HMAC, PIN, Safe Mode):** `4/4` hạng mục `[Hoàn thành]`
* **Bộ kiểm thử tự động (`tests/Nega.Tests`):** `34/34` Test Cases `[Passed 100%]`
* **Định hướng mở rộng 2 vai trò thi cử (`F08` - Đề cương):** `[Đề xuất Giai đoạn 2]`

---

## 2. Ma Trận Đối Chiếu Chi Tiết Chức Năng Đã Triển Khai

| Mã | Chức Năng Hệ Thống | Hiện Trạng Triển Khai Trong Mã Nguồn | Tệp Mã Nguồn Chính | Trạng Thái |
|:---:|---|---|---|:---:|
| **`F01`** | Nhận diện phòng máy qua UDP Broadcast LAN (`:9999`) | Directed Subnet Broadcast đa card mạng, `SO_REUSEADDR`/`SO_REUSEPORT`, fallback `--server <IP>` | `UdpDiscoveryService.cs`, `UdpDiscoveryAgent.cs`, `SubnetBroadcastHelper.cs` | [Hoàn thành] |
| **`F02`** | Truyền luồng màn hình JPEG qua TCP (`:8888`) & Zoom HD | Khung nhị phân `0xAA55`, `LatestFrameSlot` (Queue=1), `SimpleJpegEncoder` thuần C#, chuyển `Grid`/`Zoom HD` | `PacketFramer.cs`, `SimpleJpegEncoder.cs`, `LatestFrameSlot.cs` | [Hoàn thành] |
| **`F03`** | Khóa / Mở khóa chuột & bàn phím + Fail-safe 30s | Lệnh `0x03`, lớp phủ cảnh báo trên khung hình, tự nhả khóa sau `30s` mất kết nối Server | `TcpControlServer.cs`, `RemoteControlExecutor.cs` | [Hoàn thành] |
| **`F04`** | Phát thông báo khẩn cấp & theo dõi xác nhận (ACK) | Lệnh `0x05` chạy bất đồng bộ `Task.Run` không chặn socket, phản hồi `0x07 ACK (ALERT_READ)` | `TelemetryService.cs`, `RemoteControlExecutor.cs` | [Hoàn thành] |
| **`F05`** | Tắt máy từ xa có đếm ngược, Hủy & Safe Mode | Đếm ngược `5..300s`, hủy lệnh bằng `CancellationTokenSource`, ép `DryRun` khi `NEGA_ALLOW_REAL_SHUTDOWN=false` | `TcpControlServer.cs`, `RemoteControlExecutor.cs` | [Hoàn thành] |
| **`F06`** | Phân quyền (`Host`/`CoAdmin`/`Viewer`), Link mời & QR SVG | Quản lý tài khoản, cookie `HttpOnly`, link `?invite=...`, sinh QR chuẩn ISO/IEC 18004 offline | `AccountManager.cs`, `QrSvgGenerator.cs`, `ServerApiEndpoints.cs` | [Hoàn thành] |
| **`F07`** | Cụm giả lập UDP/TCP thật & Telemetry thời gian thực | Tự bật Host Agent + `0..16` máy ảo qua Socket thật, đo RTT `PingPong`, FPS/KBps EMA | `LabSimulatorService.cs`, `WorkstationAgentInstance.cs`, `TelemetryService.cs` | [Hoàn thành] |

---

## 3. Lộ Trình Định Hướng Mở Rộng Theo Đề Cương (`Giai đoạn 2`)

| Mã | Hạng Mục Mở Rộng Đề Xuất | Giá Trị Nghiệp Vụ Dự Kiến | Trạng Thái |
|:---:|---|---|:---:|
| **`F08a`** | Tạo Phòng thi bằng Mã phòng (`RoomCode`) & Web `/join` (WebSocket) | Thí sinh tham gia nhanh từ laptop cá nhân qua trình duyệt không cần cài SDK | [Đề xuất Giai đoạn 2] |
| **`F08b`** | Kiểm tra Pre-flight OS & Bảng đồng thuận Xin cấp quyền (`Consent Handshake`) | Đảm bảo tính minh bạch đạo đức phần mềm, chống lạm dụng theo dõi ngầm | [Đề xuất Giai đoạn 2] |
| **`F08c`** | Tác động OS Hook thật (`SetWindowsHookEx`, `SendInput`, `AppKit`, Anti-Cheat) | Khóa phím chuột toàn cục, cảnh báo thoát Fullscreen/chuyển tab, Auto-Revoke cuối ca | [Đề xuất Giai đoạn 2] |
