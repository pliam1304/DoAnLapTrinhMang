---
id: nhat-ky-su-co-va-sua-loi
title: Nhật Ký Xử Lý Sự Cố & Sửa Lỗi Kỹ Thuật
sidebar_position: 3
---

# Nhật Ký Xử Lý Sự Cố & Sửa Lỗi Kỹ Thuật

Tài liệu này tổng hợp các sự cố thực tế về Socket, hệ điều hành và môi trường biên dịch đã gặp trong quá trình xây dựng hệ thống **Nega** cùng phương án xử lý triệt để.

---

## Sự Cố 01: Lỗi Bind Cổng UDP Broadcast Đa Tiến Trình & `No route to host` Trên macOS

* **Triệu chứng:** Khi chạy đồng thời `UdpDiscoveryService` (Server) và nhiều thực thể `UdpDiscoveryAgent` giả lập trên cùng một máy macOS đều lắng nghe cổng `:9999`, hệ điều hành từ chối mở socket thứ hai (`Address already in use`); đồng thời gửi gói UDP tới `255.255.255.255` có thể thất bại trên một số giao diện ảo (`VPN` / `Docker`).
* **Nguyên nhân gốc rễ:** Trên hệ thống BSD/macOS, cờ `SO_REUSEADDR` đơn lẻ không đủ để nhiều socket UDP cùng `Bind` vào một cổng nhận Broadcast; cần thêm tùy chọn `SO_REUSEPORT` (`0x0200`). Ngoài ra, địa chỉ `255.255.255.255` dễ bị định tuyến sai vào card mạng ảo khi không gửi theo địa chỉ Broadcast của từng Subnet cụ thể.
* **Giải pháp đã áp dụng:**
  * Kích hoạt `socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)0x0200, true)` có điều kiện `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)` tại [`UdpDiscoveryService.cs`](../../src/Nega.Server/Discovery/UdpDiscoveryService.cs#L64-L74) và [`UdpDiscoveryAgent.cs`](../../src/Nega.Agent/Discovery/UdpDiscoveryAgent.cs#L63-L73) (kèm cơ chế fallback `Bind` cổng ngẫu nhiên `0` ở Agent nếu OS chặn trùng cổng).
  * Tạo lớp [`SubnetBroadcastHelper.cs`](../../src/Nega.Common/Networking/SubnetBroadcastHelper.cs) tính toán toàn bộ địa chỉ **Directed Subnet Broadcast** (`IP | ~SubnetMask`) trên các card mạng đang `Up` (bỏ qua `Loopback`/`Tunnel`), kết hợp `127.0.0.1` và `255.255.255.255` trong khối `try/catch` cô lập từng card.

---

## Sự Cố 02: Trình Duyệt Trả Về `HTTP 404` Khung Hình Màn Hình Lúc Mới Kết Nối

* **Triệu chứng:** Khi một máy trạm vừa hoàn tất gói `0x01 Handshake` và xuất hiện trên Nega Dashboard, yêu cầu tải ảnh `GET /api/frame/{clientId}` ngay lập tức có thể nhận mã `404 Not Found` trong khoảng vài trăm mili-giây đầu tiên nếu `CaptureProducerLoopAsync` chưa đẩy xong khung hình đầu.
* **Nguyên nhân gốc rễ:** Luồng vòng lặp chụp ảnh định kỳ chạy tách rời khỏi thời điểm thiết lập kết nối TCP; `WorkstationState.LatestJpegFrame` vẫn là `null` ngay khoảnh khắc `UpsertFromTcpHandshake` vừa thêm máy vào `WorkstationRegistry`.
* **Giải pháp đã áp dụng:** Ngay sau khi gửi `Handshake` thành công trong [`TcpClientAgent.cs`](../../src/Nega.Agent/TcpClient/TcpClientAgent.cs#L112-L120), Agent chủ động dựng và gửi ngay một khung `0x02 ScreenFrame` khởi tạo trước khi vào vòng lặp định kỳ.

---

## Sự Cố 03: Lỗi Trộn Byte Khi Hai Luồng Cùng Ghi Ra Một `NetworkStream`

* **Triệu chứng:** Trên kết nối TCP song công, vòng lặp giữ kết nối (`PingLoopAsync` chạy mỗi 5 giây) và hàm gửi lệnh điều khiển từ HTTP API có thể gọi `stream.WriteAsync` đồng thời.
* **Nguyên nhân gốc rễ:** `NetworkStream` không an toàn cho nhiều thao tác ghi song song; nếu ghi đan xen giữa header 7 byte của gói Ping và payload JPEG hoặc lệnh điều khiển, phía đầu nhận sẽ hỏng đồng bộ Magic Bytes `0xAA 0x55`.
* **Giải pháp đã áp dụng:** Đưa toàn bộ thao tác ghi vào hàm tập trung [`PacketFramer.WritePacketAsync`](../../src/Nega.Common/Protocol/PacketFramer.cs#L15-L42) khóa tuần tự bằng `SemaphoreSlim writeLock` riêng của từng phiên (`_writeLock = new(1, 1)`).

---

## Sự Cố 04: Nguy Cơ Đóng Băng Socket Khi Đọc Thông Báo Khẩn & Kẹt Khóa Khi Mất Mạng

* **Triệu chứng:** Nếu luồng đọc TCP chờ người dùng bấm xác nhận thông báo khẩn mới quay lại vòng lặp đọc, mọi gói `PingPong` hoặc lệnh hủy tiếp theo sẽ bị kẹt, khiến Server đánh dấu máy trạm `Offline` hoặc đứt phiên. Tương tự, nếu Server sập đột ngột khi phòng máy đang bị khóa phím chuột, máy trạm có nguy cơ kẹt trạng thái khóa.
* **Giải pháp đã áp dụng:**
  * Thực thi thông báo khẩn trong nền bằng `Task.Run` tại [`RemoteControlExecutor.ExecuteEmergencyAlert`](../../src/Nega.Agent/Control/RemoteControlExecutor.cs#L98-L137), giữ vòng lặp `CommandReceiverLoopAsync` không bao giờ bị chặn.
  * Theo dõi dấu thời gian mất kết nối `_disconnectedSinceUtc` trong [`TcpClientAgent.cs`](../../src/Nega.Agent/TcpClient/TcpClientAgent.cs#L144-L149) để tự động kích hoạt `TriggerFailSafeUnlockIfDisconnected()` khi vượt quá **30 giây** (`ProtocolConstants.FailSafeAutoUnlockSeconds`).

---

## Sự Cố 05: Lỗi Biên Dịch / Kiểm Thử MSBuild Node Reuse Trong Môi Trường Sandbox Cô Lập

* **Triệu chứng:** Khi chạy `dotnet build` hoặc `dotnet test` trong terminal có giới hạn quyền socket/thư mục tạm, tiến trình MSBuild đa nhân mặc định tạo các worker node nền sử dụng Unix Domain Socket hoặc đường dẫn `.dotnet` mặc định và bị treo/từ chối.
* **Giải pháp đã áp dụng:** Chạy lệnh build với tham số `--disable-build-servers -m:1` kết hợp `HOME=/tmp/dotnet_home DOTNET_CLI_HOME=/tmp/dotnet_home` khi cần cô lập cục bộ, đồng thời giữ sạch quy trình `dotnet test tests/Nega.Tests/Nega.Tests.csproj` ở môi trường chuẩn của sinh viên.
