namespace Nega.Common.Protocol;

/// <summary>
/// Mã định danh các loại gói tin truyền qua kết nối TCP song công (Port 8888).
/// </summary>
public enum PacketType : byte
{
    /// <summary>Client gửi thông tin định danh máy trạm ngay sau khi mở kết nối TCP.</summary>
    Handshake = 0x01,

    /// <summary>Client gửi mảng byte nhị phân ảnh chụp màn hình đã nén định dạng JPEG.</summary>
    ScreenFrame = 0x02,

    /// <summary>Server gửi lệnh khóa hoặc mở khóa chuột và bàn phím máy trạm.</summary>
    CmdLockInput = 0x03,

    /// <summary>Server gửi lệnh tắt máy từ xa (hoặc hủy lệnh tắt máy đang đếm ngược).</summary>
    CmdShutdown = 0x04,

    /// <summary>Server gửi thông báo khẩn cấp hiển thị cửa sổ AlwaysOnTop trên máy trạm.</summary>
    CmdAlert = 0x05,

    /// <summary>Server điều chỉnh độ phân giải, chất lượng JPEG và FPS của luồng ảnh (Grid vs Zoom).</summary>
    CmdStreamConfig = 0x06,

    /// <summary>Client phản hồi xác nhận đã thực thi lệnh hoặc người dùng đã đọc thông báo khẩn.</summary>
    Ack = 0x07,

    /// <summary>Gói kiểm tra kết nối sống (chống TCP Half-Open) và đo độ trễ mạng RTT (ms).</summary>
    PingPong = 0x08
}
