---
id: quy-chuan-lap-trinh
title: Quy Chuẩn Lập Trình & An Toàn Mã Nguồn
sidebar_position: 6
---

# Quy Chuẩn Lập Trình & An Toàn Mã Nguồn

## 1. Quy Chuẩn C# 12 & .NET 8 Trong Lập Trình Mạng

Toàn bộ dự án hướng tới nền tảng thống nhất `net8.0` với `<ImplicitUsings>enable</ImplicitUsings>` và `<Nullable>enable</Nullable>` nhằm triệt tiêu lỗi tham chiếu null (`NullReferenceException`) ngay ở thời điểm biên dịch:

* **File-Scoped Namespaces:** Khai báo `namespace Nega.Common.Protocol;` ở đầu tệp để giữ mã nguồn gọn, phẳng.
* **Bất đồng bộ không chặn luồng (`Async/Await` + `ConfigureAwait(false)`):** Toàn bộ các hàm I/O Socket (`UdpClient.ReceiveAsync`, `NetworkStream.ReadAsync`, `WriteAsync`) đều sử dụng `async/await` kèm `CancellationToken` để thu hồi tài nguyên tức thì khi tắt ứng dụng, và dùng `.ConfigureAwait(false)` ở các thư viện nền (`Nega.Common`, `Nega.Agent`, dịch vụ nền `Nega.Server`) tránh chiếm dụng SynchronizationContext.
* **Cú pháp biến thiên thời hiện đại:** Sử dụng `Span<byte>` / `Memory<byte>` và `BinaryPrimitives.WriteInt32BigEndian` khi đọc/ghi header nhị phân, tránh cấp phát mảng trung gian thừa gây áp lực cho Garbage Collector.

---

## 2. Quy Chuẩn Xử Lý Đa Luồng & Trạng Thái (Concurrency Rules)

| Kịch Bản Tranh Chấp | Giải Pháp Bắt Buộc Trong Dự Án | Tệp Áp Dụng Minh Họa |
|---|---|---|
| Hai luồng ghi cùng lúc vào `NetworkStream` (VD: `PingLoop` và lệnh `CMD_LOCK`) | Bảo vệ bằng `SemaphoreSlim _writeLock = new(1, 1)` bên trong `PacketFramer.WritePacketAsync` | [`PacketFramer.cs`](../../src/Nega.Common/Protocol/PacketFramer.cs) |
| Truy cập đồng thời danh sách máy trạm / phiên kết nối | Dùng `ConcurrentDictionary<string, T>` với bộ so sánh `StringComparer.OrdinalIgnoreCase` | [`WorkstationRegistry.cs`](../../src/Nega.Server/State/WorkstationRegistry.cs), [`TcpControlServer.cs`](../../src/Nega.Server/TcpServer/TcpControlServer.cs) |
| Cập nhật biến đếm lưu lượng từ nhiều kết nối TCP | Dùng phép toán nguyên tử `Interlocked.Increment` và `Interlocked.Read` | [`TelemetryService.cs`](../../src/Nega.Server/State/TelemetryService.cs) |
| Tránh tích tụ hàng đợi ảnh khi mạng chập chờn | Dùng `Interlocked.Exchange` + `SemaphoreSlim(0, 1)` ghi đè frame mới nhất | [`LatestFrameSlot.cs`](../../src/Nega.Agent/TcpClient/LatestFrameSlot.cs) |

---

## 3. Quy Chuẩn Đặt Tên & Tổ Chức Mã Nguồn

| Thành Phần | Quy Tắc | Ví Dụ Chuẩn Trong Nega |
|---|---|---|
| Tên lớp, Enum, Record | PascalCase | `TcpControlServer`, `PacketType`, `LockRequestDto` |
| Tên phương thức bất đồng bộ | Kết thúc bằng `Async` | `ReadPacketAsync`, `SendLockInputCommandAsync` |
| Biến thành viên riêng (Field) | `_camelCase` có gạch dưới | `_registry`, `_telemetry`, `_writeLock` |
| Biến cấu hình môi trường | Tiền tố `NEGA_` viết hoa toàn bộ | `NEGA_UDP_PORT`, `NEGA_CLUSTER_KEY`, `NEGA_ADMIN_PIN` |

---

## 4. Quy Chuẩn Bảo Mật Bắt Buộc Khi Phát Triển (Security Hygiene)

1. **Không Hardcode Bí mật trong Mã nguồn:** Tất cả mật khẩu, khóa cụm (`NEGA_CLUSTER_KEY`), mã PIN (`NEGA_ADMIN_PIN`) chỉ nằm trong tệp `.env` cục bộ đã khai báo trong `.gitignore`; chỉ đưa `.env.example` mẫu rỗng lên GitHub.
2. **So sánh bí mật chống Timing Attack:** Mọi thao tác đối chiếu PIN, mật khẩu tài khoản, Invite Token và chữ ký HMAC-SHA256 đều bắt buộc đi qua `CryptographicOperations.FixedTimeEquals` trong `PacketAuthHelper`.
3. **Ngăn chặn Command Injection:** Trước khi truyền tham số từ mạng sang tiến trình hệ điều hành (như `shutdown /c "<Reason>"`), chuỗi đầu vào phải đi qua bộ lọc `SanitizeShutdownComment` và thực thi với `UseShellExecute = false`.
4. **Hàng rào An toàn Mặc định (Safe-by-Default):** Cờ `AllowRealShutdown` và `AllowRealOsCapture` mặc định luôn là `false`, `MaskHostIdentity` mặc định là `true` để bất kỳ thành viên hay giảng viên nào `git clone` về chạy thử đều không vô tình tắt nhầm máy cá nhân hoặc làm lộ tên thật OS.
