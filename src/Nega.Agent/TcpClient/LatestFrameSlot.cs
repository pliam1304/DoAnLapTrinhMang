namespace Nega.Agent.TcpClient;

/// <summary>
/// Hàng đợi 1 phần tử ghi đè (Single-Slot Conflation Queue).
/// Giải quyết triệt để hiện tượng TCP Buffer Bloat (độ trễ tích tụ):
/// Luồng chụp màn hình luôn ghi đè frame JPEG mới nhất vào ô nhớ, luồng gửi TCP luôn lấy frame mới nhất đi gửi.
/// </summary>
public sealed class LatestFrameSlot
{
    private byte[]? _latestJpegFrame;
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void PublishFrame(byte[] jpegBytes)
    {
        Interlocked.Exchange(ref _latestJpegFrame, jpegBytes);
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Đã có tín hiệu chờ
            }
        }
    }

    public async Task<byte[]?> WaitAndTakeFrameAsync(CancellationToken ct)
    {
        await _signal.WaitAsync(ct).ConfigureAwait(false);
        return Interlocked.Exchange(ref _latestJpegFrame, null);
    }
}
