---
id: design-system-ui-ux
title: Hệ Thống Thiết Kế UI/UX & Nega Dashboard
sidebar_position: 5
---

# Hệ Thống Thiết Kế UI/UX & Nega Dashboard

## 1. Triết Lý Thiết Kế: Tối Giản Hiện Đại & Đồng Bộ Dark/Light Mode

Giao diện điều khiển trung tâm của **Nega** (`src/Nega.Server/wwwroot/`) được xây dựng hoàn toàn bằng HTML5, CSS3 và JavaScript thuần không phụ thuộc framework đóng gói nặng nề, giúp `Nega.Server` tự phục vụ Web Dashboard trực tiếp trong bộ nhớ ở tốc độ cao qua mạng LAN. Hệ thống giao diện kế thừa bảng token màu sắc tinh gọn từ dòng thiết kế `natri`, kết hợp bộ phông chữ **`Comfortaa`** bo tròn thân thiện và chuyển đổi chế độ **Tối (Dark Mode - mặc định)** / **Sáng (Light Mode)** tức thì.

---

## 2. Hệ Thống Token Màu Sắc (`nega.css`)

Toàn bộ bảng màu được khai báo bằng biến CSS HSL tại [`nega.css`](../../src/Nega.Server/wwwroot/css/nega.css):

| CSS Token | Theme Sáng (`:root`) | Theme Tối (`.dark`) | Vai Trò Giao Diện |
|---|---|---|---|
| `--background` | `224 5% 95%` | `224 71.4% 4.1%` | Nền tổng thể toàn trang Dashboard |
| `--card` | `0 0% 100%` | `222 47% 7.5%` | Nền thẻ máy trạm, Sidebar, Top Navbar, Telemetry |
| `--foreground` | `240 10% 3.9%` | `210 20% 98%` | Màu chữ tiêu đề & nội dung chính |
| `--primary` | `0 0% 9%` | `210 20% 98%` | Nút hành động chính (`btn-primary`), màu nhấn thương hiệu |
| `--secondary` | `0 0% 96.1%` | `215 27.9% 14.5%` | Nút thao tác nhanh, thẻ trạng thái phụ (`btn-secondary`) |
| `--muted-foreground` | `240 3.8% 46.1%` | `217.9 10.6% 64.9%` | Nhãn chú thích kỹ thuật, thông số phụ |
| `--destructive` | `0 84.2% 60.2%` | `0 78% 58%` | Nút tắt máy khẩn, trạng thái khóa/mất kết nối (`Locked`) |
| `--success` | `142 71% 45%` | `142 70% 45%` | Huy hiệu máy trạm đang `Online` |
| `--warning` | `38 92% 50%` | `38 92% 50%` | Cảnh báo kết nối chờ / thông báo mức `warning` |
| `--border` | `0 0% 89.8%` | `215 27.9% 16.9%` | Đường viền tinh tế 1px tách khối card (`--radius: 1rem`) |

---

## 3. Bố Cục Khung 3 Cột (`3-Column Responsive Shell`)

Khung giao diện chính áp dụng CSS Grid `.nega-shell` tối ưu hiển thị đồng thời cả luồng hình ảnh và chỉ số mạng:

1. **Top Sticky Navbar (`.nega-navbar`):**
   * Bên trái: Biểu tượng 3D Nega (`/assets/logo.png`, kích thước `28×28`, bo góc `8px`), tên thương hiệu **`nega`** và badge cổng mạng `UDP :9999 • TCP :8888`.
   * Giữa: Ô tìm kiếm tức thì (`#search-input`) lọc theo tên máy, địa chỉ IP hoặc tên hệ điều hành.
   * Bên phải: Cụm nút điều khiển nhanh toàn phòng (`Quét LAN`, `Khóa tất cả`, `Mở khóa`, `Thông báo khẩn`, `Tắt máy`), nút **`Chia sẻ & Cấp TK`** (chỉ hiển thị với `Host`), badge vai trò `@username • Role`, nút Đăng xuất và công tắc đổi Theme Sáng/Tối.
2. **Cột trái - Sidebar Điều hướng (`.nega-menubar`, `240px`):** Chứa 4 khung làm việc kèm bộ đếm badge thời gian thực:
   * **Lưới Giám sát** (`grid`): Hiển thị từng card ảnh JPEG màn hình máy trạm kèm nút Zoom HD.
   * **Danh sách Máy trạm** (`table`): Bảng thông số kỹ thuật chi tiết (`Hostname`, `IP`, `OS`, `Trạng thái`, `Ping ms`, `FPS`, `KB/s`, `ACK gần nhất`).
   * **Thông báo & ACK** (`alerts`): Theo dõi tỷ lệ máy đã xác nhận đọc từng thông báo khẩn đã gửi.
   * **Phòng máy Giả lập** (`simulator`): Tùy chỉnh số lượng máy ảo `0..16` đang chạy thật qua UDP/TCP để kiểm thử tải.
3. **Cột phải - Bảng Telemetry & Nhật ký (`.nega-telemetry`, `290px`):** Thống kê tức thời tổng băng thông KB/s, tổng FPS, số gói UDP, số frame TCP, số lệnh đã phát và danh sách 30 dòng nhật ký mới nhất gắn dấu thời gian + `[@username]`.

---

## 4. Chuẩn Hóa Biểu Tượng & An Toàn Giao Diện

* **Biểu tượng Vector SVG Nội Tuyến (Inline SVG):** Toàn bộ icon trong `index.html` sử dụng nét vẽ vector chuẩn `viewBox="0 0 24 24"`, `stroke-width="2"` (đồng bộ độ dày nét vẽ, tự đổi màu theo `currentColor`), **loại bỏ hoàn toàn emoji bàn phím** nhằm bảo đảm hiển thị đồng nhất trên Windows, macOS và mọi độ phân giải.
* **Phòng chống XSS trên chuỗi động (`escapeHtml`):** Mọi dữ liệu đến từ máy trạm trong mạng LAN (`Hostname`, `OsDescription`, `Detail`, nội dung log) đều đi qua hàm [`escapeHtml`](../../src/Nega.Server/wwwroot/js/nega-app.js#L15-L22) trước khi chèn vào DOM.
* **Ẩn thành phần theo Vai trò (`applyRolePermissionsToUi`):** Với vai trò `Viewer`, thanh nút điều khiển phòng, khung nút điều khiển trong Zoom Modal và tab `Phòng máy Giả lập` được ẩn gọn giúp giao diện tập trung hoàn toàn vào việc quan sát; kết hợp chốt chặn tầng API (`403 Forbidden`).
