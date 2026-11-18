using System.Diagnostics;
using System.Runtime.InteropServices;
using Nega.Common.Models;
using Nega.Common.Security;

namespace Nega.Agent.Capture;

/// <summary>
/// Dịch vụ chụp màn hình và nén luồng ảnh JPEG theo cấu hình thích ứng (Grid View 480x270 vs Zoom View 960x540).
/// Hỗ trợ chụp màn hình thật của hệ điều hành và chế độ dựng màn hình máy trạm trực quan (kèm lớp phủ Lock / Alert / Shutdown).
/// </summary>
public sealed class ScreenCaptureService
{
    private readonly string _hostname;
    private readonly string _ipAddress;
    private readonly bool _isSimulated;
    private readonly int _themeSeed;
    private long _frameCounter;

    public ScreenCaptureService(string hostname, string ipAddress, bool isSimulated, int themeSeed = 0)
    {
        _hostname = hostname;
        _ipAddress = ipAddress;
        _isSimulated = isSimulated;
        _themeSeed = themeSeed;
    }

    public async Task<byte[]> CaptureJpegFrameAsync(
        StreamConfigPayload config,
        bool isLocked,
        string? activeAlertTitle,
        string? activeAlertMessage,
        int? shutdownRemainingSeconds,
        CancellationToken ct = default)
    {
        int width = Math.Clamp(config.Width, 240, 1280);
        int height = Math.Clamp(config.Height, 135, 720);
        int quality = Math.Clamp(config.Quality, 20, 90);
        long tick = Interlocked.Increment(ref _frameCounter);

        // Nếu là máy trạm thật và được phép trong .env (NEGA_ALLOW_OS_CAPTURE=true), chụp màn hình thật của OS
        if (!_isSimulated &&
            NegaRuntimeConfig.Current.AllowRealOsCapture &&
            !isLocked &&
            string.IsNullOrEmpty(activeAlertTitle) &&
            shutdownRemainingSeconds is null)
        {
            byte[]? osJpeg = await TryCaptureRealOsScreenJpegAsync(width, quality, ct).ConfigureAwait(false);
            if (osJpeg is { Length: > 200 })
            {
                return osJpeg;
            }
        }

        // Dựng frame màn hình máy trạm trực quan và nén sang chuẩn JPEG Baseline
        byte[] rgb = RenderWorkstationDesktopRgb(
            width,
            height,
            tick,
            isLocked,
            activeAlertTitle,
            activeAlertMessage,
            shutdownRemainingSeconds);

        return SimpleJpegEncoder.EncodeRgbToJpeg(rgb, width, height, quality);
    }

    private static async Task<byte[]?> TryCaptureRealOsScreenJpegAsync(int maxWidth, int quality, CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return null;
        }

        string tmpFile = Path.Combine(Path.GetTempPath(), $"nega_cap_{Guid.NewGuid():N}.jpg");
        try
        {
            using var capProc = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/sbin/screencapture",
                Arguments = $"-x -C -t jpg \"{tmpFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (capProc is null)
            {
                return null;
            }

            await capProc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (capProc.ExitCode != 0 || !File.Exists(tmpFile))
            {
                return null;
            }

            using var sipsProc = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/sips",
                Arguments = $"-Z {maxWidth} -s format jpeg -s formatOptions {quality} \"{tmpFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (sipsProc is not null)
            {
                await sipsProc.WaitForExitAsync(ct).ConfigureAwait(false);
            }

            return await File.ReadAllBytesAsync(tmpFile, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tmpFile))
                {
                    File.Delete(tmpFile);
                }
            }
            catch
            {
                // Ignore cleanup failure
            }
        }
    }

    private byte[] RenderWorkstationDesktopRgb(
        int w,
        int h,
        long tick,
        bool isLocked,
        string? alertTitle,
        string? alertMessage,
        int? shutdownRemainingSec)
    {
        byte[] rgb = new byte[w * h * 3];

        // Nếu đang bị khóa chuột/bàn phím -> Vẽ màn hình khóa Nega (Red/Dark Lock Overlay)
        if (isLocked)
        {
            FillGradient(rgb, w, h, (35, 10, 18), (85, 16, 30));
            DrawRect(rgb, w, h, w / 8, h / 6, w * 6 / 8, h * 4 / 6, (22, 12, 18));
            DrawBorder(rgb, w, h, w / 8, h / 6, w * 6 / 8, h * 4 / 6, 3, (239, 68, 68));

            // Biểu tượng ổ khóa ở giữa
            int cx = w / 2;
            int cy = h / 2 - 12;
            DrawRect(rgb, w, h, cx - 22, cy - 4, 44, 34, (239, 68, 68));
            DrawBorder(rgb, w, h, cx - 14, cy - 22, 28, 20, 4, (248, 113, 113));
            DrawRect(rgb, w, h, cx - 4, cy + 8, 8, 12, (255, 255, 255));

            DrawTextCentered(rgb, w, h, cy - 46, "NEGA SECURITY LOCK", 2, (254, 202, 202));
            DrawTextCentered(rgb, w, h, cy + 40, $"{_hostname} - INPUT LOCKED", 2, (255, 255, 255));
            return rgb;
        }

        // Bảng màu hình nền desktop theo từng máy trạm
        (byte R, byte G, byte B) bgTop = (_themeSeed % 4) switch
        {
            0 => (15, 23, 42),
            1 => (17, 24, 39),
            2 => (20, 30, 48),
            _ => (24, 24, 37)
        };
        (byte R, byte G, byte B) bgBot = (_themeSeed % 4) switch
        {
            0 => (30, 58, 95),
            1 => (28, 69, 88),
            2 => (49, 46, 105),
            _ => (39, 55, 77)
        };
        FillGradient(rgb, w, h, bgTop, bgBot);

        // Thanh Top Bar của Desktop máy trạm
        DrawRect(rgb, w, h, 0, 0, w, 24, (12, 16, 26));
        DrawText(rgb, w, h, 10, 6, $"NEGA AGENT | {_hostname} ({_ipAddress})", 1, (226, 232, 240));
        string clockText = DateTime.Now.ToString("HH:mm:ss");
        DrawText(rgb, w, h, Math.Max(10, w - 75), 6, clockText, 1, (148, 163, 184));

        // Cửa sổ Code Editor / Ứng dụng đang chạy trên máy sinh viên
        int winX = 18;
        int winY = 34;
        int winW = w - 36;
        int winH = h - 68;
        DrawRect(rgb, w, h, winX, winY, winW, winH, (20, 26, 38));
        DrawBorder(rgb, w, h, winX, winY, winW, winH, 1, (51, 65, 85));

        // Thanh tiêu đề cửa sổ
        DrawRect(rgb, w, h, winX, winY, winW, 20, (30, 41, 59));
        DrawRect(rgb, w, h, winX + 8, winY + 6, 8, 8, (239, 68, 68));
        DrawRect(rgb, w, h, winX + 20, winY + 6, 8, 8, (245, 158, 11));
        DrawRect(rgb, w, h, winX + 32, winY + 6, 8, 8, (34, 197, 94));
        DrawText(rgb, w, h, winX + 50, winY + 6, "WORKSPACE - NETWORK PROGRAMMING LAB", 1, (203, 213, 225));

        // Cột Sidebar bên trái trong cửa sổ
        int sideW = Math.Max(55, winW / 5);
        DrawRect(rgb, w, h, winX + 1, winY + 20, sideW, winH - 21, (15, 21, 32));
        for (int i = 0; i < 6; i++)
        {
            int ry = winY + 32 + i * 16;
            if (ry + 8 >= winY + winH) break;
            var barColor = i == (int)(tick % 6) ? ((byte)56, (byte)189, (byte)248) : ((byte)71, (byte)85, (byte)105);
            DrawRect(rgb, w, h, winX + 10, ry, sideW - 20, 6, barColor);
        }

        // Vùng dòng code chuyển động thời gian thực + Con trỏ nhấp nháy
        int codeX = winX + sideW + 12;
        int maxLines = Math.Min(12, (winH - 36) / 14);
        for (int line = 0; line < maxLines; line++)
        {
            int ly = winY + 30 + line * 14;
            int indent = ((line + _themeSeed) % 3) * 14;
            int seg1 = 30 + ((line * 19 + _themeSeed * 11) % 55);
            int seg2 = 25 + ((line * 29 + (int)tick * 7) % 85);

            var c1 = (line % 3) switch
            {
                0 => ((byte)129, (byte)140, (byte)248),
                1 => ((byte)56, (byte)189, (byte)248),
                _ => ((byte)52, (byte)211, (byte)153)
            };

            DrawRect(rgb, w, h, codeX + indent, ly, Math.Min(seg1, winW - sideW - indent - 20), 6, c1);
            if (codeX + indent + seg1 + 8 + seg2 < winX + winW - 14)
            {
                DrawRect(rgb, w, h, codeX + indent + seg1 + 8, ly, seg2, 6, (148, 163, 184));
            }

            if (line == (int)(tick % Math.Max(1, maxLines)) && (tick % 2 == 0))
            {
                int cursorX = Math.Min(winX + winW - 18, codeX + indent + seg1 + 12 + seg2);
                DrawRect(rgb, w, h, cursorX, ly - 2, 4, 10, (250, 204, 21));
            }
        }

        // Biểu đồ sóng xung nhịp mạng / CPU ở góc dưới phải
        int chartW = Math.Min(130, winW / 3);
        int chartH = 38;
        int chartX = winX + winW - chartW - 12;
        int chartY = winY + winH - chartH - 10;
        DrawRect(rgb, w, h, chartX, chartY, chartW, chartH, (12, 18, 28));
        DrawBorder(rgb, w, h, chartX, chartY, chartW, chartH, 1, (30, 58, 138));
        int bars = Math.Max(6, chartW / 8);
        for (int b = 0; b < bars; b++)
        {
            int bh = 6 + (int)((Math.Sin((tick + b + _themeSeed) * 0.65) + 1.0) * ((chartH - 12) / 2.0));
            DrawRect(rgb, w, h, chartX + 4 + b * 8, chartY + chartH - bh - 3, 5, bh, (34, 197, 94));
        }

        // Thanh Taskbar dưới cùng
        DrawRect(rgb, w, h, 0, h - 22, w, 22, (10, 14, 23));
        DrawRect(rgb, w, h, 10, h - 17, 12, 12, (56, 189, 248));
        DrawText(rgb, w, h, 28, h - 15, $"TCP STREAM ACTIVE | FRAME #{tick}", 1, (148, 163, 184));

        // Nếu đang có cảnh báo Tắt máy đếm ngược -> Hiển thị Banner cam/đỏ
        if (shutdownRemainingSec is not null)
        {
            DrawRect(rgb, w, h, 24, 30, w - 48, 34, (153, 27, 27));
            DrawBorder(rgb, w, h, 24, 30, w - 48, 34, 2, (252, 165, 165));
            DrawTextCentered(rgb, w, h, 42, $"SHUTDOWN IN {shutdownRemainingSec.Value}S", 2, (255, 255, 255));
        }

        // Nếu có Thông báo khẩn cấp chưa đóng -> Hiển thị cửa sổ TopMost nổi giữa màn hình
        if (!string.IsNullOrEmpty(alertTitle))
        {
            int mw = w * 3 / 4;
            int mh = h / 2;
            int mx = (w - mw) / 2;
            int my = (h - mh) / 2;
            DrawRect(rgb, w, h, mx, my, mw, mh, (28, 15, 22));
            DrawBorder(rgb, w, h, mx, my, mw, mh, 3, (239, 68, 68));
            DrawRect(rgb, w, h, mx, my, mw, 24, (185, 28, 28));
            DrawTextCentered(rgb, w, h, my + 8, "EMERGENCY ALERT", 1, (255, 255, 255));
            DrawTextCentered(rgb, w, h, my + mh / 2 - 4, "URGENT MESSAGE FROM SERVER", 1, (254, 202, 202));
            DrawRect(rgb, w, h, mx + mw / 2 - 40, my + mh - 26, 80, 18, (239, 68, 68));
            DrawTextCentered(rgb, w, h, my + mh - 20, "ACK READY", 1, (255, 255, 255));
        }

        return rgb;
    }

    private static void FillGradient(byte[] rgb, int w, int h, (byte R, byte G, byte B) top, (byte R, byte G, byte B) bot)
    {
        for (int y = 0; y < h; y++)
        {
            float t = (float)y / Math.Max(1, h - 1);
            byte r = (byte)(top.R + (bot.R - top.R) * t);
            byte g = (byte)(top.G + (bot.G - top.G) * t);
            byte b = (byte)(top.B + (bot.B - top.B) * t);
            int row = y * w * 3;
            for (int x = 0; x < w; x++)
            {
                int p = row + x * 3;
                rgb[p] = r;
                rgb[p + 1] = g;
                rgb[p + 2] = b;
            }
        }
    }

    private static void DrawRect(byte[] rgb, int w, int h, int rx, int ry, int rw, int rh, (byte R, byte G, byte B) c)
    {
        int x0 = Math.Clamp(rx, 0, w);
        int y0 = Math.Clamp(ry, 0, h);
        int x1 = Math.Clamp(rx + rw, 0, w);
        int y1 = Math.Clamp(ry + rh, 0, h);

        for (int y = y0; y < y1; y++)
        {
            int row = y * w * 3;
            for (int x = x0; x < x1; x++)
            {
                int p = row + x * 3;
                rgb[p] = c.R;
                rgb[p + 1] = c.G;
                rgb[p + 2] = c.B;
            }
        }
    }

    private static void DrawBorder(byte[] rgb, int w, int h, int rx, int ry, int rw, int rh, int thick, (byte R, byte G, byte B) c)
    {
        DrawRect(rgb, w, h, rx, ry, rw, thick, c);
        DrawRect(rgb, w, h, rx, ry + rh - thick, rw, thick, c);
        DrawRect(rgb, w, h, rx, ry, thick, rh, c);
        DrawRect(rgb, w, h, rx + rw - thick, ry, thick, rh, c);
    }

    private static void DrawTextCentered(byte[] rgb, int w, int h, int y, string text, int scale, (byte R, byte G, byte B) c)
    {
        int textW = text.Length * 6 * scale;
        int x = Math.Max(4, (w - textW) / 2);
        DrawText(rgb, w, h, x, y, text, scale, c);
    }

    private static void DrawText(byte[] rgb, int w, int h, int x, int y, string text, int scale, (byte R, byte G, byte B) c)
    {
        int cursorX = x;
        foreach (char rawCh in text.ToUpperInvariant())
        {
            byte[] glyph = GetGlyph5x7(rawCh);
            for (int row = 0; row < 7; row++)
            {
                byte bits = glyph[row];
                for (int col = 0; col < 5; col++)
                {
                    if (((bits >> (4 - col)) & 1) != 0)
                    {
                        DrawRect(rgb, w, h, cursorX + col * scale, y + row * scale, scale, scale, c);
                    }
                }
            }
            cursorX += 6 * scale;
            if (cursorX >= w - 6) break;
        }
    }

    private static byte[] GetGlyph5x7(char ch) => ch switch
    {
        'A' => new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        'B' => new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E },
        'C' => new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
        'D' => new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E },
        'E' => new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
        'F' => new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 },
        'G' => new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F },
        'H' => new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        'I' => new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
        'J' => new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C },
        'K' => new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 },
        'L' => new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
        'M' => new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
        'N' => new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 },
        'O' => new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        'P' => new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
        'Q' => new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D },
        'R' => new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
        'S' => new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E },
        'T' => new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
        'U' => new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        'V' => new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
        'W' => new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11 },
        'X' => new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
        'Y' => new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 },
        'Z' => new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
        '0' => new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
        '1' => new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
        '2' => new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
        '3' => new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
        '4' => new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
        '5' => new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
        '6' => new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
        '7' => new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
        '8' => new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
        '9' => new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
        '-' => new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 },
        ':' => new byte[] { 0x00, 0x04, 0x04, 0x00, 0x04, 0x04, 0x00 },
        '.' => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x06 },
        '|' => new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
        '(' => new byte[] { 0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02 },
        ')' => new byte[] { 0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08 },
        '#' => new byte[] { 0x0A, 0x1F, 0x0A, 0x0A, 0x1F, 0x0A, 0x00 },
        _ => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
    };
}
