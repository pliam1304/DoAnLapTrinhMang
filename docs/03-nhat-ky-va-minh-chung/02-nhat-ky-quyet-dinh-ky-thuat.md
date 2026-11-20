---
id: nhat-ky-quyet-dinh-ky-thuat
title: Nhật Ký Quyết Định Kỹ Thuật (ADR)
sidebar_position: 2
---

# Nhật Ký Quyết Định Kỹ Thuật (Architectural Decision Records)

Tài liệu này ghi nhận các phân tích đánh đổi (Trade-offs) và quyết định thiết kế kiến trúc quan trọng đã được hiện thực trực tiếp trong mã nguồn dự án **Nega**.

---

## ADR-01: Kết Nối TCP Theo Chiều Ngược (`Agent -> Server`) Sau Khi Khám Phá UDP

* **Bối cảnh:** Khi phát hiện máy trạm trong LAN, có hai lựa chọn thiết lập kênh truyền chính TCP: (A) Server chủ động `ConnectAsync` tới IP máy trạm; hoặc (B) Máy trạm `ConnectAsync` tới IP + cổng TCP `:8888` do Server phát qua UDP `DISCOVER`.
* **Quyết định:** Lựa chọn phương án **(B) Agent chủ động kết nối TCP tới Server** tại [`UdpDiscoveryAgent.cs`](../../src/Nega.Agent/Discovery/UdpDiscoveryAgent.cs) và [`TcpClientAgent.cs`](../../src/Nega.Agent/TcpClient/TcpClientAgent.cs).
* **Lý do kỹ thuật:** Máy sinh viên thường bật tường lửa (Windows Defender Firewall) chặn các kết nối đi vào (Inbound), trong khi lại cho phép kết nối đi ra (Outbound). Hơn nữa, nếu máy trạm đổi IP do cấp lại DHCP, Server chỉ cần nhận kết nối đến trên một cổng duy nhất `:8888` rồi sử dụng chính luồng song công đó để gửi lệnh điều khiển ngược xuống máy trạm.

---

## ADR-02: Tự Thiết Kế Khung Nhị Phân `0xAA 0x55` Thay Vì Truyền Text/Base64

* **Bối cảnh:** Cần truyền cả thông điệp điều khiển JSON và dữ liệu ảnh nhị phân JPEG liên tục trên cùng một kết nối `NetworkStream`. Nếu đóng gói ảnh bằng chuỗi Base64 bên trong JSON (như WebSocket văn bản hoặc JSON Lines), dung lượng ảnh sẽ phình to khoảng **33%** và tăng áp lực cấp phát chuỗi (String Allocation) cho GC.
* **Quyết định:** Cài đặt bộ đóng/mở khung nhị phân 7-byte Header `[0xAA][0x55][Type:1B][Length:4B Big-Endian]` tại [`PacketFramer.cs`](../../src/Nega.Common/Protocol/PacketFramer.cs) với `PacketType.ScreenFrame (0x02)` gửi mảng byte JPEG thô.
* **Lý do kỹ thuật:** Giảm trực tiếp ~33% lưu lượng băng thông ảnh, phân tách rạch ròi ranh giới gói tin chống TCP Coalescing/Fragmentation và kiểm soát được kích thước tối đa (`10 MB`) ngay trước khi cấp phát mảng bộ nhớ nhận.

---

## ADR-03: Cài Đặt Bộ Mã Hóa JPEG Baseline (`SimpleJpegEncoder`) & QR SVG (`QrSvgGenerator`) Thuần C#

* **Bối cảnh:** Trong môi trường đa hệ điều hành (Windows / macOS ARM64 / Linux), việc sử dụng các thư viện đồ họa phụ thuộc thư viện hệ thống C/native (như `System.Drawing.Common` vốn không còn hỗ trợ tốt trên macOS/Linux từ .NET 6+, hoặc phụ thuộc API mạng ngoài để vẽ QR Code) dễ gây lỗi `DllNotFoundException` và làm hỏng kịch bản chạy phòng lab offline không có Internet.
* **Quyết định:** Tự viết hoàn chỉnh [`SimpleJpegEncoder.cs`](../../src/Nega.Agent/Capture/SimpleJpegEncoder.cs) (hệ màu YCbCr, biến đổi 2D DCT 8×8, lượng tử hóa Q-Table, Huffman) và [`QrSvgGenerator.cs`](../../src/Nega.Server/Bridge/QrSvgGenerator.cs) (chuẩn ISO/IEC 18004, ECC-L, đa thức sửa lỗi Reed-Solomon `GF(256)` xuất trực tiếp `<svg>`).
* **Lý do kỹ thuật:** Đảm bảo 0 thư viện ngoài, biên dịch chạy ngay trên cả macOS và Windows sau một lệnh `dotnet run`, hoạt động ổn định 100% ngay cả khi phòng thi ngắt hoàn toàn Internet.

---

## ADR-04: Dùng `LatestFrameSlot` (Single-Slot Conflation) Chống TCP Buffer Bloat

* **Bối cảnh:** Tốc độ dựng/chụp màn hình tại máy trạm có thể nhanh hơn tốc độ nhận khung hình của mạng LAN khi phòng có nhiều máy trạm. Dùng hàng đợi FIFO bình thường gây tích tụ trễ hình ảnh.
* **Quyết định:** Triển khai [`LatestFrameSlot.cs`](../../src/Nega.Agent/TcpClient/LatestFrameSlot.cs) sử dụng `Interlocked.Exchange` + `SemaphoreSlim(0, 1)` để giữ tối đa **1 khung hình mới nhất**.
* **Lý do kỹ thuật:** Chấp nhận bỏ qua khung cũ đã lỗi thời nếu mạng chưa gửi kịp, giữ độ trễ hiển thị màn hình luôn bám sát thời gian thực.

---

## ADR-05: Tách Bí Mật Ra `.env` + Bảo Vệ Đa Lớp (`PIN`, `HttpOnly Cookie`, `HMAC-SHA256`, `DryRun`)

* **Bối cảnh:** Mã nguồn đồ án được đẩy lên kho GitHub công khai (`pliam1304/DoAnLapTrinhMang`), đồng thời ứng dụng sẽ được chạy demo trên mạng LAN thật.
* **Quyết định:** Xây dựng [`NegaRuntimeConfig.cs`](../../src/Nega.Common/Security/NegaRuntimeConfig.cs) nạp `.env` từ thư mục chạy lên đến tối đa 6 cấp cha, đi kèm [`PacketAuthHelper.cs`](../../src/Nega.Common/Security/PacketAuthHelper.cs) và chính sách mặc định an toàn (`NEGA_ALLOW_REAL_SHUTDOWN=false`, `NEGA_MASK_HOST_IDENTITY=true`, PIN tự sinh bằng CSPRNG nếu chưa đặt).
* **Lý do kỹ thuật:** Tuyệt đối không lộ bí mật thật trên Git lịch sử, bảo vệ máy lập trình viên khỏi thao tác tắt máy nhầm khi test và ngăn chặn thiết bị lạ trong cùng Wi-Fi giả danh gói tin Broadcast.
