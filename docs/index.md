---
id: index
title: Báo Cáo Dự Án Giám Sát Phòng Máy Nega
slug: /
sidebar_position: 1
---

# Báo Cáo Dự Án: Phần Mềm Giám Sát Phòng Máy & Remote Control Mini (Nega)

Tài liệu kỹ thuật, đặc tả giao thức mạng, kiến trúc phần mềm và hồ sơ kiểm chứng quá trình xây dựng hệ thống **Nega** — ứng dụng giám sát phòng máy tính qua mạng LAN/Wi-Fi, truyền luồng màn hình JPEG thời gian thực và điều khiển tập trung nhiều máy trạm trên nền tảng **.NET 8 & Socket thuần**.

---

## 1. Thông Tin Đồ Án Môn Học

* **Học phần:** Lập Trình Mạng (Network Programming)
* **Tên đề tài:** Xây dựng Phần mềm Giám sát phòng máy & Remote Control mini (Nega)
* **Môi trường & Công nghệ cốt lõi:** C# 12, .NET 8.0, `System.Net.Sockets` (`UdpClient`, `TcpListener`, `TcpClient`, `NetworkStream`), ASP.NET Core Minimal API, Bộ mã hóa JPEG Baseline thuần C# (`SimpleJpegEncoder`), Bộ sinh QR Code SVG offline (`QrSvgGenerator`), HTML/CSS/JS thuần (Dark/Light Mode).
* **Mô hình kiến trúc:** Kiến trúc 3 phân hệ tách biệt (`Nega.Common`, `Nega.Server`, `Nega.Agent`) kết hợp Song song UDP Broadcast Discovery (:9999) + TCP Full-Duplex Binary Streaming (:8888) + Web Dashboard HTTP (:5050).
* **Nhóm thực hiện (3 thành viên):**
  1. **Huỳnh Phúc Lâm**
  2. **Cao Khôi Nguyên**
  3. **Ngô Quyền Linh**
* **Mã nguồn dự án (GitHub):** [DoAnLapTrinhMang](https://github.com/pliam1304/DoAnLapTrinhMang)
* **Website tài liệu:** Cấu hình triển khai tự động qua GitHub Pages & Docusaurus (`website/`).

:::note[Ghi chú phân công nhiệm vụ]
Nhiệm vụ lập trình cụ thể của từng cá nhân tạm thời chưa phân chia cố định trong giai đoạn hiện tại và sẽ được cập nhật khi nhóm chốt bảng phân công chi tiết cuối kỳ. Mọi ghi nhận đóng góp mã nguồn trong tài liệu này được trích xuất nguyên trạng từ lịch sử Git của kho lưu trữ.
:::

---

## 2. Mục Tiêu & Cấu Trúc Báo Cáo

Báo cáo kỹ thuật này được tổ chức thành ba phần trọng tâm theo chuẩn đồ án phần mềm:

### Phần I: Đặc Tả Ứng Dụng (Application Specifications)
Trình bày toàn diện bài toán giám sát phòng máy LAN/Wi-Fi có IP cấp động DHCP, đặc tả kiến trúc 3 phân hệ, cấu trúc gói tin nhị phân `0xAA 0x55` (`PacketFramer`), các mô hình trạng thái bộ nhớ (`WorkstationRegistry`, `AccountManager`, `TelemetryService`), ma trận phân quyền 3 vai trò (`Host` / `CoAdmin` / `Viewer`), hệ thống giao diện Dark/Light UI và chi tiết 8 phân hệ chức năng từ `F01` đến `F08` (bao gồm phần định hướng mở rộng mô hình 2 vai trò thi cử trong đề cương).

### Phần II: Kết Quả Đạt Được (Deliverables & Results)
Tổng hợp ma trận đối chiếu mức độ hoàn thiện giữa các tính năng lõi đã chạy thực tế trong mã nguồn hiện tại và các hạng mục định hướng giai đoạn tiếp theo; báo cáo kết quả kiểm thử tự động (`34/34 Unit Tests` bằng xUnit trong `tests/Nega.Tests`); phân tích tối ưu hóa truyền thông mạng (`LatestFrameSlot` chống Buffer Bloat, chuyển đổi `Grid` / `Zoom HD`, tắt thuật toán Nagle `NoDelay = true`) và hướng dẫn cài đặt chi tiết.

### Phần III: Minh Chứng & Nhật Ký Kỹ Thuật (Engineering Audit Trail)
Ghi nhận nguyên trạng lịch sử Git Commits từ kho lưu trữ `pliam1304/DoAnLapTrinhMang`, các quyết định thiết kế kiến trúc quan trọng (Architecture Decision Records - ADR) và các sự cố mạng/hệ điều hành thực tế đã phân tích, xử lý trong quá trình phát triển (macOS `SO_REUSEPORT`, `No route to host` khi Broadcast, khung hình khởi tạo ban đầu, khóa an toàn `.env` & MSBuild sandbox).

---

## 3. Bản Đồ Tra Cứu Tài Liệu

| Mục | Nội dung chính | Trọng tâm đánh giá |
|---|---|---|
| **[Đặc tả Tổng quan Yêu cầu](01-dac-ta-ung-dung/01-tong-quan-yeu-cau.md)** | Bối cảnh bài toán, phạm vi đã cài đặt và định hướng mở rộng 2 vai trò | Phạm vi và tính thực tiễn đề tài |
| **[Kiến trúc Hệ thống](01-dac-ta-ung-dung/02-kien-truc-he-thong.md)** | Solution 3 phân hệ (`Common`/`Server`/`Agent`), Topology UDP/TCP/HTTP | Thiết kế mạng & tách tầng phần mềm |
| **[Giao thức & Mô hình Dữ liệu](01-dac-ta-ung-dung/03-giao-thuc-va-du-lieu.md)** | Framing `0xAA55`, `PacketType` `0x01..0x08`, Payload JSON, Models | Độ chặt chẽ tầng Giao vận |
| **[Ma trận Phân quyền & Bảo mật](01-dac-ta-ung-dung/04-ma-tran-phan-quyen.md)** | RBAC `Host` / `CoAdmin` / `Viewer`, PIN LAN, Cookie, HMAC-SHA256 | An toàn thông tin & chống giả mạo |
| **[Hệ thống Giao diện UI/UX](01-dac-ta-ung-dung/05-design-system-ui-ux.md)** | Design Tokens Dark/Light (`nega.css`), App Shell 3 cột, SVG thuần | Thẩm mỹ & trải nghiệm vận hành |
| **[Quy chuẩn Lập trình](01-dac-ta-ung-dung/06-quy-chuan-lap-trinh.md)** | Quy chuẩn C# 12/.NET 8, Thread-Safety, `SemaphoreSlim`, Git | Chất lượng mã nguồn nhóm |
| **[Đặc tả Chức năng F01 - F08](01-dac-ta-ung-dung/chuc-nang/F01-tu-dong-nhan-dien-udp-lan.md)** | Luồng `sequenceDiagram` chi tiết từng chức năng UDP, JPEG, Lock, Alert, Shutdown, QR, Simulator & Roadmap | Chiều sâu kỹ thuật lập trình mạng |
| **[Tổng kết Chức năng](02-ket-qua-dat-duoc/01-tong-ket-chuc-nang.md)** | Đối chiếu hạng mục đã hoàn thiện `[Hoàn thành]` và định hướng `[Đề xuất]` | Tính minh bạch nghiệm thu |
| **[Báo cáo Kiểm thử Unit Test](02-ket-qua-dat-duoc/02-kiem-thu-unit-test.md)** | Kết quả chạy thực tế `34/34 Passed` trên `tests/Nega.Tests` | Độ tin cậy và khả năng tái kiểm chứng |
| **[Tối ưu Hiệu năng Mạng](02-ket-qua-dat-duoc/03-toi-uu-hieu-nang.md)** | Single-slot Conflation Queue, Adaptive Stream, Zero native dependency | Xử lý nghẽn cổ chai TCP Streaming |
| **[Hướng dẫn Cài đặt & Khởi chạy](02-ket-qua-dat-duoc/04-huong-dan-cai-dat-khoi-chay.md)** | Cấu hình `.env`, chạy Server, chạy Agent LAN/Simulator, chạy Tests | Tính khả thi triển khai thực tế |
| **[Lịch sử Git Commits](03-nhat-ky-va-minh-chung/01-lich-su-commit-github.md)** | Trích xuất các commit thực tế trên nhánh `main` của repository | Minh chứng quá trình phát triển |
| **[Quyết định Kỹ thuật (ADR)](03-nhat-ky-va-minh-chung/02-nhat-ky-quyet-dinh-ky-thuat.md)** | 6 quyết định kiến trúc cốt lõi (TCP Reverse Connect, Framing, Pure C# JPEG/QR) | Năng lực phân tích đánh đổi |
| **[Nhật ký Sự cố & Sửa lỗi](03-nhat-ky-va-minh-chung/03-nhat-ky-su-co-va-sua-loi.md)** | Root Cause Analysis cho các lỗi macOS Broadcast, HTTP 404 frame đầu, Build Sandbox | Kinh nghiệm gỡ lỗi mạng thực chiến |
