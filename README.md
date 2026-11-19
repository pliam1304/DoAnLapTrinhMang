# Nega — Phần mềm Giám sát Phòng máy & Remote Control mini

**Nhóm thực hiện:**
1. **Huỳnh Phúc Lâm**
2. **Cao Khôi Nguyên.**
3. **Ngô Quyền Linh**

---

## 1. Kiến trúc Phân tầng (`Nega Architecture`)

Dự án được chia thành **3 phân hệ độc lập** trong `src/` để tách biệt rõ giữa **Giao thức lõi**, **Máy chủ Quản trị (Server)** và **Máy trạm (Client Agent)**:

```text
nega/
├── Nega.slnx                                     # Solution tổng quản lý dự án (.NET 8)
├── .env.example                                  # Mẫu cấu hình môi trường & bảo mật (copy sang .env)
├── assets/
│   └── logo.png                                  # Icon Nega gốc
└── src/
    ├── Nega.Common/                              # [1] PHÂN HỆ GIAO THỨC, MÔ HÌNH & BẢO MẬT
    │   ├── Protocol/
    │   │   ├── ProtocolConstants.cs              # Cổng UDP :9999, TCP :8888, HTTP :5050, Magic 0xAA55
    │   │   ├── PacketType.cs                     # Mã lệnh TCP: 0x01 Handshake .. 0x08 PingPong
    │   │   ├── PacketEnvelope.cs                 # Gói tin đã đóng/mở khung + Serialize JSON
    │   │   └── PacketFramer.cs                   # Length-Prefixed Binary Framing (7B Header + Payload, ReadExactAsync)
    │   ├── Models/
    │   │   ├── DiscoveryModels.cs                # Gói UDP Broadcast DISCOVER & ANNOUNCE (kèm AuthTag)
    │   │   └── ControlModels.cs                  # Payload điều khiển: Lock, Alert, Shutdown, StreamConfig, Ack
    │   ├── Networking/
    │   │   └── SubnetBroadcastHelper.cs          # Tính Directed Subnet Broadcast (IP | ~SubnetMask) đa card mạng
    │   └── Security/
    │       ├── NegaRuntimeConfig.cs              # Nạp cấu hình an toàn từ .env / biến môi trường
    │       └── PacketAuthHelper.cs               # Ký & xác thực gói tin bằng HMAC-SHA256 (chống giả mạo)
    │
    ├── Nega.Server/                              # [2] PHÂN HỆ MÁY CHỦ QUẢN TRỊ (Chia 5 tầng logic)
    │   ├── Program.cs                            # Khởi tạo DI, cấu hình cổng & chính sách an toàn
    │   ├── Discovery/
    │   │   └── UdpDiscoveryService.cs            # [Tầng 1] Phát UDP Broadcast DISCOVER (:9999) & nhận ANNOUNCE
    │   ├── TcpServer/
    │   │   ├── TcpControlServer.cs               # [Tầng 2] Lắng nghe TCP (:8888) & điều phối lệnh đơn/hàng loạt
    │   │   └── ClientSession.cs                  # [Tầng 2] Phiên TCP từng máy trạm (Reader + Mutex Writer + Ping)
    │   ├── State/
    │   │   ├── WorkstationRegistry.cs            # [Tầng 3] Kho trạng thái máy trạm Thread-safe + TTL Offline (10s)
    │   │   └── TelemetryService.cs               # [Tầng 3] Đo băng thông KB/s, FPS, theo dõi ACK & Nhật ký
    │   ├── Bridge/
    │   │   ├── ServerApiEndpoints.cs             # [Tầng 4] REST API + Bảo vệ PIN quản trị LAN + Luồng JPEG
    │   │   └── LabSimulatorService.cs            # [Tầng 4] Agent cục bộ & Cụm máy trạm giả lập qua UDP/TCP thật
    │   └── wwwroot/                              # [Tầng 5] Giao diện Nega Dashboard (Light/Dark Mode)
    │
    └── Nega.Agent/                               # [3] PHÂN HỆ MÁY TRẠM (Chia 5 tầng logic)
        ├── Program.cs                            # Hỗ trợ chạy máy trạm thật, --simulate N, --server <IP>
        ├── Discovery/
        │   └── UdpDiscoveryAgent.cs              # [Tầng 1] Nghe UDP :9999 (SO_REUSEADDR), trả lời ANNOUNCE
        ├── TcpClient/
        │   ├── TcpClientAgent.cs                 # [Tầng 2] Kết nối TCP :8888, Auto-Reconnect, gửi ảnh & nhận lệnh
        │   └── LatestFrameSlot.cs                # [Tầng 2] Single-Slot Conflation Queue (Queue = 1) chống trễ tích tụ
        ├── Capture/
        │   ├── ScreenCaptureService.cs           # [Tầng 3] Chụp màn hình / dựng desktop kèm overlay
        │   └── SimpleJpegEncoder.cs              # [Tầng 3] Bộ mã hóa chuẩn JPEG Baseline (JFIF DCT + Huffman)
        ├── Control/
        │   └── RemoteControlExecutor.cs          # [Tầng 4] Khóa (+ Fail-safe 30s), Thông báo (+ ACK), Tắt máy
        └── Simulator/
            └── WorkstationAgentInstance.cs       # [Tầng 5] Đóng gói 1 thực thể máy trạm độc lập (UDP + TCP)
```

---

## 2. Cơ chế Bảo mật & An toàn Mặc định

- **Tách bí mật khỏi mã nguồn:** Tất cả khóa bảo mật (`NEGA_ADMIN_PIN`, `NEGA_CLUSTER_KEY`) và cờ hệ thống (`NEGA_ALLOW_REAL_SHUTDOWN`, `NEGA_ALLOW_OS_CAPTURE`) được đọc từ tệp `.env` (đã chặn trong `.gitignore`, không đẩy lên GitHub).
- **Bảo vệ Dashboard qua LAN:** Truy cập từ máy khác qua IP LAN yêu cầu nhập **Mã PIN quản trị** (đọc từ `.env` hoặc tự sinh ngẫu nhiên theo phiên và in trên Console Server) và cấp cookie `HttpOnly` (`SameSite=Strict`).
- **Xác thực gói tin cụm LAN (HMAC-SHA256):** Khi thiết lập `NEGA_CLUSTER_KEY` giống nhau trên Server và các Agent, toàn bộ gói UDP `DISCOVER`/`ANNOUNCE` và TCP `Handshake` được ký và kiểm tra replay window (`±120s`).
- **Ẩn danh thông tin cá nhân & Khóa tắt máy thật mặc định:** `NEGA_MASK_HOST_IDENTITY=true` ẩn tên đăng nhập/tên máy cá nhân; `NEGA_ALLOW_REAL_SHUTDOWN=false` đảm bảo lệnh tắt máy luôn chạy ở chế độ Safe Mode trừ khi bạn chủ động mở trong `.env`.

---

## 3. Hướng dẫn Cài đặt & Chạy Chương trình

### Bước 1: Tạo tệp cấu hình cục bộ (tùy chọn)
```bash
cd nega
cp .env.example .env
```
*(Nếu bỏ qua bước này, hệ thống vẫn tự chạy an toàn với mã PIN ngẫu nhiên hiển thị trên Console và chế độ Safe Mode).*

### Bước 2: Build & Chạy Nega Server
```bash
dotnet build Nega.slnx
dotnet run --project src/Nega.Server
```
Mở trình duyệt tại **`http://localhost:5050`** (hoặc `http://<IP_LAN_SERVER>:5050` trên máy khác và nhập mã PIN quản trị).

### Bước 3: Chạy Nega Agent trên máy trạm khác trong cùng LAN
```bash
# Tự động tìm Server qua UDP Broadcast (:9999):
dotnet run --project src/Nega.Agent

# Hoặc chỉ định trực tiếp IP LAN của Server (nếu Wi-Fi chặn Broadcast):
dotnet run --project src/Nega.Agent -- --server <IP_LAN_SERVER>
```
