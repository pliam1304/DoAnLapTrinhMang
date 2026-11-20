---
id: tong-quan-yeu-cau
title: Tổng Quan Đề Tài & Yêu Cầu Hệ Thống
sidebar_position: 1
---

# Tổng Quan Đề Tài & Yêu Cầu Hệ Thống

## 1. Bối Cảnh & Bài Toán Kỹ Thuật

Trong các phòng thực hành máy tính trường học hoặc phòng thi tin học kết nối qua mạng nội bộ (LAN Ethernet / Wi-Fi), các máy trạm thường được router cấp địa chỉ IP động thông qua DHCP. Điều này gây ra hai thách thức kỹ thuật trực tiếp đối với các phần mềm giám sát phòng máy:

1. **Vấn đề tự động tìm thấy nhau (Zero-Config Discovery):** Không thể cấu hình tĩnh danh sách IP từng máy sinh viên khi khởi động phần mềm quản lý. Máy chủ (Server) và các máy trạm (Client Agent) cần cơ chế tự động phát hiện qua tầng truyền tải phi kết nối **UDP Broadcast** (cổng `9999`) trên toàn bộ các card mạng hoạt động.
2. **Vấn đề truyền luồng ảnh và điều khiển song công (Full-Duplex Streaming & Control):** Khi số lượng máy trạm từ 5 đến hàng chục máy cùng gửi ảnh màn hình về máy chủ, ứng dụng phải tránh hiện tượng dính gói (`TCP Coalescing`), phân mảnh gói (`TCP Fragmentation`), nghẽn bộ đệm (`Buffer Bloat`) và chặn luồng thực thi khi máy trạm nhận thông báo hoặc khóa máy.

:::note[Phân định minh bạch giữa Phiên bản Hiện tại và Đề cương Mở rộng]
Hệ thống **Nega** hiện tại đã hoàn chỉnh phần lõi mạng **UDP Broadcast + TCP Binary Streaming + Web Dashboard + RBAC 3 vai trò + QR Code offline + Cụm giả lập thực tế (`Nega.Agent` / `LabSimulatorService`)**. Song song đó, theo đề cương nghiên cứu mở rộng do thầy hướng dẫn góp ý, dự án còn định hướng phát triển thêm mô hình **2 vai trò thi cử (Giám thị / Thí sinh)** với bảng **Xin cấp quyền truy cập & điều khiển (Consent Handshake)**, cổng tham gia trên trình duyệt `/join` và móc nối khóa cứng hệ điều hành thật (`Win32 Hook` / `macOS Kiosk`). Tài liệu luôn ghi rõ nhãn **`[Hoàn thành]`** cho mã nguồn đã thực thi và **`[Đề xuất Giai đoạn 2]`** cho phần định hướng mở rộng.
:::

---

## 2. Nhóm Chức Năng Cốt Lõi Đã Triển Khai

1. **`F01` - Tự động nhận diện máy trạm qua UDP Broadcast LAN (`:9999`):** Server định kỳ phát gói `DISCOVER` (mỗi `3,000 ms`) tới loopback và tất cả các địa chỉ Directed Subnet Broadcast (`IP | ~SubnetMask`). Agent lắng nghe và phản hồi gói `ANNOUNCE` chứa định danh, IP, độ phân giải và trạng thái khóa. Hỗ trợ fallback chỉ định IP thủ công qua tham số `--server <IP>` khi Wi-Fi cô lập Broadcast.
2. **`F02` - Truyền luồng ảnh màn hình JPEG qua TCP (`:8888`) & Chuyển chế độ Grid/Zoom HD:**
   * Thiết lập kênh kết nối TCP song công giữa `TcpClientAgent` và `TcpControlServer`/`ClientSession`.
   * Truyền khung hình bằng bộ đóng khung nhị phân `0xAA 0x55` + kích thước 4 byte Big-Endian.
   * Hỗ trợ chuyển đổi động chất lượng luồng qua gói `0x06 CMD_STREAM_CONFIG`: **Grid View** (`480×270`, chất lượng `45%`, `3 FPS`) và **Zoom HD** (`960×540`, chất lượng `75%`, `10 FPS`).
   * Mã hóa ảnh bằng thuật toán JPEG Baseline thuần C# (`SimpleJpegEncoder` - YCbCr, 2D DCT 8×8, Huffman) và tùy chọn chụp màn hình macOS thật (`/usr/sbin/screencapture` + `/usr/bin/sips`) khi cấu hình `NEGA_ALLOW_OS_CAPTURE=true`.
3. **`F03` - Khóa / Mở khóa chuột và bàn phím kèm Fail-safe 30 giây:** Gửi lệnh `0x03 CMD_LOCK_INPUT` tới một máy hoặc toàn bộ phòng (`"ALL"`). Trạng thái khóa được áp dụng tức thì, đồng thời tự động nhả khóa an toàn (`TriggerFailSafeUnlockIfDisconnected`) nếu mất kết nối Server quá `30 giây`.
4. **`F04` - Gửi Thông báo khẩn cấp & Theo dõi Xác nhận (ACK):** Server phát lệnh `0x05 CMD_ALERT` kèm mức độ (`urgent`, `warning`, `info`). Máy trạm xử lý thông báo trên luồng bất đồng bộ (`Task.Run`) để không làm nghẽn luồng đọc TCP, sau đó phản hồi gói `0x07 ACK` (`ALERT_READ`) để Server cập nhật tỷ lệ máy đã xác nhận đọc (`AckCount / TargetCount`).
5. **`F05` - Tắt máy từ xa có đếm ngược, Hủy lệnh & Chế độ Safe Mode:** Lệnh `0x04 CMD_SHUTDOWN` hỗ trợ trễ `5..300 giây`, hỗ trợ hủy đếm ngược tức thì (`action: "cancel"`), làm sạch chuỗi lý do (`SanitizeShutdownComment`) chống chèn lệnh Shell, và cưỡng chế **Safe Mode (`DryRun = true`)** mặc định khi `NEGA_ALLOW_REAL_SHUTDOWN=false` trong `.env`.
6. **`F06` - Cấp tài khoản Phân quyền (`Host` / `CoAdmin` / `Viewer`), Link mời 1 chạm & Mã QR Code SVG Offline:** Host quản trị có thể tạo/xóa tài khoản tại modal **"Chia sẻ & Cấp TK"**, sao chép link mời tự động đăng nhập (`/?invite=<token>`) và quét mã **QR Code SVG** do `QrSvgGenerator` tự sinh hoàn toàn offline theo chuẩn ISO/IEC 18004 (Version 1–6, ECC-L, Reed-Solomon GF(256)).
7. **`F07` - Cụm Giả lập Phòng máy & Đo đạc Telemetry Thời gian thực:** Tự động khởi chạy Agent máy chủ cục bộ kết hợp tối đa 16 máy trạm ảo (`MAY-01`..`MAY-16`) giao tiếp qua Socket UDP/TCP thật (`LabSimulatorService`), đo độ trễ RTT (PingPong mỗi 5 giây), FPS, băng thông KB/s bằng bộ lọc trượt EMA và nhật ký sự kiện gắn nhãn người thao tác `[@username]`.

---

## 3. Định Hướng Mở Rộng Theo Đề Cương `[Đề xuất Giai đoạn 2]`

* **`F08` - Mô hình Thi cử 2 Vai trò (Giám thị / Thí sinh) & Tác động Hệ điều hành thật:**
  * Phân tách luồng tham gia bằng **Mã phòng (Room Code)** và trang web **`/join`** kết nối qua **WebSocket (`/ws/examinee`)**.
  * Bổ sung bước **Kiểm tra hệ thống trước thi (Pre-flight OS Check)** và **Bảng đồng thuận cấp quyền (Permission Consent Handshake)** minh bạch trước khi chia sẻ màn hình thật và trao quyền điều khiển.
  * Đóng gói lớp thực thi hệ điều hành thật (`Win32 Hook SetWindowsHookEx` / `SendInput` / `WinForms TopMost` trên Windows; `AppKit` / `CGEvent` trên macOS) và cơ chế **Auto-Revoke** thu hồi toàn bộ quyền khi kết thúc ca thi.
