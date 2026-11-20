# Nega — Phần Mềm Giám Sát Phòng Máy & Remote Control Mini

Hệ thống giám sát phòng máy tính qua mạng LAN/Wi-Fi, truyền luồng màn hình JPEG thời gian thực và điều khiển từ xa tập trung nhiều máy trạm, xây dựng trên nền tảng **.NET 8 & `System.Net.Sockets`**.

**Nhóm thực hiện (Học phần Lập Trình Mạng):**
1. **Huỳnh Phúc Lâm**
2. **Cao Khôi Nguyên**
3. **Ngô Quyền Linh**

*(Ghi chú: Nhiệm vụ lập trình chi tiết giữa các thành viên tạm thời chưa phân chia cố định trong kho mã nguồn này).*

---

## 1. Công Nghệ Sử Dụng

* **Ngôn ngữ & Nền tảng:** C# 12, .NET 8.0 (`net8.0`).
* **Giao thức & Mạng thuần Socket (`System.Net.Sockets`):**
  * **UDP Broadcast (`:9999`):** Directed Subnet Broadcast (`IP | ~SubnetMask`) đa card mạng giúp tự động phát hiện máy trạm dùng IP động DHCP, hỗ trợ `SO_REUSEADDR` và `SO_REUSEPORT` (`0x0200` trên macOS).
  * **TCP Binary Streaming (`:8888`):** Kênh điều khiển & truyền ảnh song công (Full-Duplex) với bộ đóng/mở khung nhị phân `0xAA 0x55` (`PacketFramer`) + `SemaphoreSlim` chống trộn byte + `NoDelay = true`.
* **Xử lý Ảnh & Mã QR 100% Offline (Zero External Native Dependencies):**
  * [`SimpleJpegEncoder`](src/Nega.Agent/Capture/SimpleJpegEncoder.cs): Thuật toán nén chuẩn JPEG Baseline (YCbCr, 2D DCT 8×8, Huffman) thuần C#.
  * [`QrSvgGenerator`](src/Nega.Server/Bridge/QrSvgGenerator.cs): Bộ sinh QR Code chuẩn ISO/IEC 18004 (Byte Mode, ECC-L, Reed-Solomon `GF(256)`) xuất `<svg>` offline hoàn toàn.
* **Web Dashboard & Bảo mật:** ASP.NET Core Minimal API (`:5050`), giao diện 3 cột Dark/Light Mode (HTML/CSS/JS thuần), xác thực phiên bằng Cookie `HttpOnly` (`SameSite=Strict`), phân quyền 3 vai trò (`Host`, `CoAdmin`, `Viewer`) và ký gói tin cụm LAN bằng `HMAC-SHA256`.
* **Kiểm thử & Tài liệu:** `xUnit 2.5.3` (`tests/Nega.Tests`) và bộ tài liệu kỹ thuật cấu trúc Docusaurus + Mermaid (`docs/`, `website/`).

---

## 2. Các Chức Năng Chính

### 2.1. Các phân hệ lõi đã triển khai (`F01`–`F07`)
1. **`F01` - Tự động nhận diện phòng máy qua UDP Broadcast (`:9999`):** Server phát định kỳ `DISCOVER` mỗi `3,000 ms`, Agent tự phản hồi `ANNOUNCE` và mở kết nối TCP đi ra (hoặc dùng `--server <IP>` nếu Wi-Fi chặn Broadcast).
2. **`F02` - Truyền luồng màn hình JPEG (`0x02`) & Chuyển chế độ Grid/Zoom HD (`0x06`):** Dùng hàng đợi 1 phần tử `LatestFrameSlot` triệt tiêu TCP Buffer Bloat; chuyển động giữa lưới `480×270 @ 3 FPS (q=45%)` và Zoom HD `960×540 @ 10 FPS (q=75%)`.
3. **`F03` - Khóa / Mở khóa chuột và bàn phím (`0x03`) + Fail-safe 30s:** Khóa đơn lẻ hoặc toàn phòng kèm lớp phủ trực quan; tự nhả khóa sau `30 giây` nếu mất kết nối Server.
4. **`F04` - Gửi Thông báo khẩn cấp (`0x05`) & Theo dõi xác nhận (`0x07 ACK`):** Xử lý bất đồng bộ `Task.Run` không chặn socket đọc, đếm số máy đã xác nhận đọc (`ALERT_READ`).
5. **`F05` - Tắt máy từ xa có đếm ngược (`0x04`), Hủy lệnh & Safe Mode:** Hỗ trợ trễ `5..300s`, hủy đếm ngược tức thì (`cancel`), lọc chuỗi tham số chống chèn lệnh và khóa an toàn mặc định (`DryRun = true`).
6. **`F06` - Cấp tài khoản Phân quyền (`Host` / `CoAdmin` / `Viewer`), Link mời & QR Code:** Tạo/thu hồi tài khoản, chia sẻ link đăng nhập 1 chạm `/?invite=...` và quét mã QR SVG không cần Internet.
7. **`F07` - Cụm giả lập phòng máy thật & Telemetry thời gian thực:** Tự khởi chạy Agent máy chủ + `0..16` máy ảo (`MAY-01..MAY-N`) qua UDP/TCP thật, đo RTT `PingPong` (5s), FPS, băng thông KB/s (EMA) và nhật ký gắn thẻ `[@username]`.

### 2.2. Định hướng mở rộng theo Đề cương (`F08` - `[Đề xuất Giai đoạn 2]`)
* Mô hình **2 vai trò thi cử (Giám thị / Thí sinh)** với **Mã phòng (`RoomCode`)**, cổng tham gia trên trình duyệt **`/join` (WebSocket)**, bảng kiểm tra **Pre-flight OS Check**, bước **Đồng thuận Xin cấp quyền (`Consent Handshake`)**, lớp thực thi hệ điều hành thật (`Win32 Hook SetWindowsHookEx` / `SendInput` / `macOS Kiosk`) và cơ chế **Auto-Revoke** khi kết thúc ca thi. Xem chi tiết tại [`docs/01-dac-ta-ung-dung/chuc-nang/F08-dinh-huong-thi-cu-2-vai-tro.md`](docs/01-dac-ta-ung-dung/chuc-nang/F08-dinh-huong-thi-cu-2-vai-tro.md).

---

## 3. Cơ Chế Bảo Mật & An Toàn Mặc Định

* **Tách bí mật khỏi mã nguồn:** Cấu hình nhạy cảm (`NEGA_ADMIN_PIN`, `NEGA_CLUSTER_KEY`) và cờ hệ thống (`NEGA_ALLOW_REAL_SHUTDOWN`, `NEGA_ALLOW_OS_CAPTURE`) nằm trong `.env` (bị bỏ qua bởi `.gitignore`).
* **Bảo vệ Dashboard qua LAN:** Truy cập từ máy khác trong LAN yêu cầu đăng nhập tài khoản/PIN hợp lệ (`ConstantTimeEquals`) để nhận Cookie `HttpOnly` (`SameSite=Strict`, hạn 8 giờ).
* **Xác thực gói tin cụm LAN (HMAC-SHA256):** Khi đặt `NEGA_CLUSTER_KEY`, mọi gói UDP `DISCOVER`/`ANNOUNCE` và TCP `Handshake` đều kiểm tra chữ ký HMAC-SHA256 và cửa sổ chống phát lại `±120s`.
* **Safe-by-Default:** `NEGA_MASK_HOST_IDENTITY=true` ẩn thông tin OS cá nhân; `NEGA_ALLOW_REAL_SHUTDOWN=false` giữ mọi lệnh tắt máy ở Safe Mode.

---

## 4. Hướng Dẫn Cài Đặt, Chạy & Kiểm Thử

### 4.1. Yêu cầu môi trường
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 4.2. Cấu hình (tùy chọn) & Khởi chạy Server
```bash
cp .env.example .env   # Tùy chọn; hệ thống tự dùng mặc định an toàn nếu bỏ qua
dotnet build Nega.slnx
dotnet run --project src/Nega.Server
```
Mở trình duyệt tại **`http://localhost:5050`** (hoặc `http://<IP_LAN_SERVER>:5050` trên máy khác trong LAN).

### 4.3. Khởi chạy Agent trên máy trạm khác
```bash
# Tự động tìm Server qua UDP Broadcast (:9999):
dotnet run --project src/Nega.Agent

# Hoặc chỉ định trực tiếp IP LAN Server nếu Wi-Fi chặn Broadcast:
dotnet run --project src/Nega.Agent -- --server <IP_LAN_SERVER>
```

### 4.4. Chạy toàn bộ Unit Tests
```bash
dotnet test tests/Nega.Tests/Nega.Tests.csproj
```
*(Toàn bộ **34/34 unit tests** kiểm tra `PacketFramer`, `PacketAuthHelper`, `SimpleJpegEncoder`, `LatestFrameSlot`, `WorkstationRegistry`, `AccountManager`, `QrSvgGenerator`, `RemoteControlExecutor`, `TelemetryService` và `SubnetBroadcastHelper` đạt **100% Passed**).*

---

## 5. Cấu Trúc Solution & Tài Liệu Kỹ Thuật

```text
nega/
├── Nega.slnx                                     # Solution tổng (.NET 8)
├── .env.example                                  # Mẫu biến môi trường & bảo mật
├── README.md                                     # Tổng quan dự án
├── assets/
│   └── logo.png                                  # Biểu tượng Nega 3D gốc
├── src/
│   ├── Nega.Common/                              # Giao thức 0xAA55, Models, SubnetBroadcast, .env & HMAC Security
│   ├── Nega.Server/                              # UDP Discovery, TCP Control, Registry, Account/QR, HTTP API & UI
│   └── Nega.Agent/                               # Agent máy trạm, LatestFrameSlot, SimpleJpegEncoder, RemoteControlExecutor
├── tests/
│   └── Nega.Tests/                               # 34 Unit Tests (xUnit) cho toàn bộ 3 phân hệ
├── docs/                                         # Hồ sơ Đặc tả, Kết quả & Nhật ký Kỹ thuật (Chuẩn Docusaurus)
│   ├── index.md                                  # Trang chủ & Bản đồ tra cứu tài liệu
│   ├── 01-dac-ta-ung-dung/                       # Yêu cầu, Kiến trúc, Giao thức, Phân quyền, UI/UX, Quy chuẩn & F01..F08
│   ├── 02-ket-qua-dat-duoc/                      # Tổng kết đối chiếu, Báo cáo Unit Test, Tối ưu hiệu năng, Cài đặt
│   └── 03-nhat-ky-va-minh-chung/                 # Lịch sử Git, Quyết định Kỹ thuật (ADR), Nhật ký Sự cố & Sửa lỗi
├── website/                                      # Cấu hình website tài liệu Docusaurus + Mermaid
└── .github/
    └── workflows/deploy-docs.yml                 # Workflow CI/CD tự động build & deploy docs lên GitHub Pages
```

### Tài liệu tra cứu nhanh trong `docs/`
* [Trang chủ Báo cáo & Bản đồ Tài liệu (`docs/index.md`)](docs/index.md)
* [Kiến trúc Hệ thống & Topology Mạng](docs/01-dac-ta-ung-dung/02-kien-truc-he-thong.md)
* [Đặc tả Giao thức Nhị phân `0xAA55` & Mô hình Dữ liệu](docs/01-dac-ta-ung-dung/03-giao-thuc-va-du-lieu.md)
* [Ma trận Phân quyền & Bảo mật Hệ thống](docs/01-dac-ta-ung-dung/04-ma-tran-phan-quyen.md)
* [Tổng kết Chức năng](docs/02-ket-qua-dat-duoc/01-tong-ket-chuc-nang.md) & [Báo cáo Kiểm thử 34 Unit Tests](docs/02-ket-qua-dat-duoc/02-kiem-thu-unit-test.md)
* [Lịch sử Git Commits](docs/03-nhat-ky-va-minh-chung/01-lich-su-commit-github.md), [ADR](docs/03-nhat-ky-va-minh-chung/02-nhat-ky-quyet-dinh-ky-thuat.md) & [Nhật ký Sự cố](docs/03-nhat-ky-va-minh-chung/03-nhat-ky-su-co-va-sua-loi.md)
