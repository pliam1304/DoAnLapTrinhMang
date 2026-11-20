---
id: kiem-thu-unit-test
title: Báo Cáo Kết Quả Kiểm Thử Tự Động (Unit Test)
sidebar_position: 2
---

# Báo Cáo Kết Quả Kiểm Thử Tự Động (Unit Test)

## 1. Chiến Lược & Môi Trường Kiểm Thử

Để xác minh độ chính xác của bộ giao thức nhị phân, cơ chế mã hóa ảnh, thuật toán sinh QR và logic quản trị trạng thái mà không phụ thuộc môi trường mạng ngoài, dự án xây dựng bộ kiểm thử tự động tại [`tests/Nega.Tests/Nega.Tests.csproj`](../../tests/Nega.Tests/Nega.Tests.csproj):

* **Framework kiểm thử:** `xUnit 2.5.3` chạy trên nền tảng `.NET 8.0` (`Microsoft.NET.Test.Sdk 17.8.0`).
* **Phạm vi kiểm tra trực tiếp:** Tham chiếu cả 3 phân hệ nguồn (`Nega.Common`, `Nega.Server`, `Nega.Agent`).
* **Tổng số test cases đã chạy và xác minh:** **34 bài kiểm thử (Unit Tests)** — **34/34 Passed (100%)**, `0` Failed, `0` Skipped.

---

## 2. Bảng Thống Kê Chi Tiết 34 Unit Tests Theo Phân Hệ

| Tệp Kiểm Thử (Test Suite) | Số Case | Trọng Tâm Kiểm Tra Kỹ Thuật | Kết Quả |
|---|:---:|---|:---:|
| [`PacketFramerTests.cs`](../../tests/Nega.Tests/PacketFramerTests.cs) | 4 | Khép kín JSON round-trip `0xAA 0x55`, xử lý phân mảnh byte (3B chunks) & dính gói liền kề (**Fragmentation & Coalescing**), từ chối Magic Header sai và Payload vượt quá `10 MB`. | [4/4 Passed] |
| [`PacketAuthHelperTests.cs`](../../tests/Nega.Tests/PacketAuthHelperTests.cs) | 5 | Sinh chữ ký `HMAC-SHA256`, cơ chế Dev mở khi rỗng key, xác thực chữ ký hợp lệ, bác bỏ trường dữ liệu bị sửa đổi hoặc quá hạn `>120s`, đối chiếu `ConstantTimeEquals`. | [5/5 Passed] |
| [`SimpleJpegEncoderAndCaptureTests.cs`](../../tests/Nega.Tests/SimpleJpegEncoderAndCaptureTests.cs) | 5 | Xác minh Header JPEG `FF D8` và Footer `FF D9`, đối chứng dung lượng tăng theo chất lượng (`q=20` vs `q=85`), dựng frame `ScreenCaptureService` ở 3 trạng thái (`Online`, `Locked`, `Alert + Shutdown`). | [5/5 Passed] |
| [`LatestFrameSlotTests.cs`](../../tests/Nega.Tests/LatestFrameSlotTests.cs) | 3 | Kiểm chứng ghi đè khung hình mới nhất loại bỏ frame cũ chưa gửi (**Single-Slot Conflation**), đánh thức đồng bộ qua `SemaphoreSlim`, tôn trọng `CancellationToken`. | [3/3 Passed] |
| [`WorkstationRegistryTests.cs`](../../tests/Nega.Tests/WorkstationRegistryTests.cs) | 5 | Upsert UDP Discovery, chuyển trạng thái TCP Handshake (lấy `RemoteIp` dự phòng), đo `TotalFramesReceived` & `BandwidthKbps`, cập nhật ACK (`LOCK`/`SHUTDOWN`/`CANCEL`), hết hạn TTL 10s sang `Offline`, sắp xếp máy thật trước máy ảo. | [5/5 Passed] |
| [`AccountManagerAndQrTests.cs`](../../tests/Nega.Tests/AccountManagerAndQrTests.cs) | 6 | Khởi tạo mặc định `admin`/`coadmin`/`viewer`, đăng nhập đúng/sai mật khẩu, đăng nhập qua `InviteToken`, bảo vệ không cho xóa `admin`, hủy phiên khi thu hồi tài khoản, sinh QR SVG offline chuẩn cho cả URL ngắn và URL vượt 130 ký tự. | [6/6 Passed] |
| [`RemoteControlExecutorTests.cs`](../../tests/Nega.Tests/RemoteControlExecutorTests.cs) | 4 | Thực thi `LOCK`/`UNLOCK` kèm phát ACK, tự nhả khóa `TriggerFailSafeUnlockIfDisconnected`, đếm ngược tắt máy ở chế độ `DryRun` + hủy lệnh (`CANCEL_SHUTDOWN`), nhận Alert khẩn không chặn luồng. | [4/4 Passed] |
| [`TelemetryAndNetworkTests.cs`](../../tests/Nega.Tests/TelemetryAndNetworkTests.cs) | 2 | Cộng dồn bộ đếm nguyên tử UDP/TCP/Command, theo dõi `ALERT_READ`, giới hạn hàng đợi nhật ký tối đa 60 dòng và kiểm tra `SubnetBroadcastHelper` trả về `127.0.0.1` + `255.255.255.255`. | [2/2 Passed] |
| **TỔNG CỘNG** | **34** | **Bao phủ cả 3 project `Nega.Common`, `Nega.Server` và `Nega.Agent`** | **[34/34 PASSED]** |

---

## 3. Lệnh Thực Thi & Kết Quả Thực Tế Ghi Nhận

Chạy kiểm thử từ thư mục gốc `nega/`:

```bash
dotnet test tests/Nega.Tests/Nega.Tests.csproj --verbosity normal
```

Kết quả đầu ra thực tế thu được trên môi trường .NET 8.0:

```text
Test Run Successful.
Total tests: 34
     Passed: 34
 Total time: 0,5642 Seconds
```
