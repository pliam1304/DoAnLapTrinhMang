---
id: toi-uu-hieu-nang
title: Tối Ưu Hiệu Năng Truyền Thông Mạng
sidebar_position: 3
---

# Tối Ưu Hiệu Năng Truyền Thông Mạng

## 1. Mục Tiêu Thiết Kế Tầng Truyền Tải

Trong hệ thống giám sát phòng máy thời gian thực, hiện tượng nghẽn mạng cục bộ hoặc chậm phản hồi không đến từ việc thiếu CPU máy chủ mà chủ yếu xuất phát từ **3 nút thắt cổ chai kinh điển trong lập trình Socket TCP**:
1. Độ trễ gom gói nhỏ (**Nagle's Algorithm**).
2. Độ trễ tích tụ hàng đợi (**TCP Buffer Bloat**) khi máy chủ hoặc Wi-Fi nhận ảnh chậm hơn tốc độ chụp màn hình.
3. Lãng phí băng thông khi cùng lúc truyền ảnh độ phân giải cao cho hàng chục ô thumbnail nhỏ.

---

## 2. Các Cơ Chế Tối Ưu Đã Cài Đặt Trong Mã Nguồn

### 2.1. Hàng Đợi Ghi Đè 1 Phần Tử (`LatestFrameSlot` — Queue = 1)
* **Vấn đề:** Nếu sử dụng hàng đợi FIFO thông thường (`Channel` không giới hạn hoặc `ConcurrentQueue`), khi băng thông Wi-Fi tụt xuống tạm thời, hàng chục frame JPEG cũ sẽ dồn ứ trong hàng đợi, khiến hình ảnh hiển thị trên màn hình Giám thị bị trễ 5–10 giây so với thực tế.
* **Giải pháp:** [`LatestFrameSlot`](../../src/Nega.Agent/TcpClient/LatestFrameSlot.cs) chỉ duy trì duy nhất một tham chiếu `byte[]? _latestJpegFrame`. Tác vụ chụp (`PublishFrame`) hoán đổi nguyên tử bằng `Interlocked.Exchange` để **vứt bỏ frame cũ chưa kịp gửi**, bảo đảm luồng `FrameSenderLoopAsync` luôn lấy đúng khung hình mới nhất tại thời điểm socket sẵn sàng.

### 2.2. Luồng Hình Ảnh Thích Ứng (`Grid View` vs `Zoom HD`)
Hệ thống không truyền một kích thước cố định cho mọi tình huống mà điều khiển động thông qua gói `0x06 CmdStreamConfig` trong [`ControlModels.cs`](../../src/Nega.Common/Models/ControlModels.cs#L23-L48):

| Chế Độ (`Mode`) | Độ Phân Giải | Chất Lượng JPEG | Tốc Độ Khung (`FPS`) | Mục Đích Tối Ưu |
|---|---|:---:|:---:|---|
| **`grid`** (Mặc định) | `480 × 270` | `45%` | `3 FPS` | Tiết kiệm băng thông & CPU khi theo dõi đồng thời toàn bộ lưới phòng máy (5–16+ máy). |
| **`zoom`** (Phóng to) | `960 × 540` | `75%` | `10 FPS` | Nâng độ nét và mượt cho **duy nhất máy trạm đang được phóng to** để đọc rõ chữ trên màn hình. |

Khi người dùng đóng cửa sổ Zoom trên trình duyệt, giao diện tự động gửi lại yêu cầu chuyển máy trạm đó về chế độ `grid` (`setStreamMode(clientId, 'grid')`), giải phóng băng thông ngay lập tức.

### 2.3. Tắt Thuật Toán Nagle (`TcpClient.NoDelay = true`) & Gửi Frame Khởi Tạo Tức Thì
* **Tắt Nagle (`NoDelay = true`):** Thiết lập đồng bộ trên cả [`ClientSession`](../../src/Nega.Server/TcpServer/ClientSession.cs#L36) lẫn [`TcpClientAgent`](../../src/Nega.Agent/TcpClient/TcpClientAgent.cs#L77) để các gói lệnh điều khiển kích thước nhỏ (`CmdLockInput`, `CmdAlert`, `PingPong`) được đẩy thẳng xuống tầng TCP ngay lập tức mà không phải đợi gom đủ kích thước MSS.
* **Đẩy Frame Khởi Tạo Ngay Sau Handshake:** Tại [`TcpClientAgent.cs`](../../src/Nega.Agent/TcpClient/TcpClientAgent.cs#L112-L120), ngay sau gói `0x01 Handshake`, Agent chụp và gửi lập tức một frame JPEG đầu tiên giúp Dashboard hiển thị ảnh máy mới chỉ trong vòng tích tắc mà không trả về `HTTP 404`.

### 2.4. Bộ Đếm Nguyên Tử & Làm Mượt Telemetry (EMA Filter)
* Tổng lưu lượng gói UDP/TCP/Command được đếm không khóa (**Lock-free**) bằng `Interlocked.Increment`.
* Tốc độ `CurrentFps` và `BandwidthKbps` trong [`WorkstationRegistry`](../../src/Nega.Server/State/WorkstationRegistry.cs#L159-L167) áp dụng bộ lọc trung bình trượt hàm mũ (`0.65 * giá trị cũ + 0.35 * giá trị tức thời`) giúp thông số hiển thị ổn định trên UI, không bị nhảy loạn số khi khoảng cách hai frame biến động.

### 2.5. Mã Hóa JPEG & QR Code Thuần C# Không Phụ Thuộc Thư Viện Ngoài
* Cả `SimpleJpegEncoder` (JFIF DCT + Huffman) và `QrSvgGenerator` (ISO/IEC 18004 + Reed-Solomon `GF(256)`) đều chạy thuần trong bộ nhớ `.NET 8`, loại bỏ hoàn toàn độ trễ khởi tạo native library hay phụ thuộc API Internet bên ngoài khi triển khai trong phòng lab nội bộ.
