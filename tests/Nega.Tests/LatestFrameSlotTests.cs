using Nega.Agent.TcpClient;

namespace Nega.Tests;

public sealed class LatestFrameSlotTests
{
    [Fact]
    public async Task PublishFrame_OverwritesStaleFramesWithLatest_PreventingBufferBloat()
    {
        var slot = new LatestFrameSlot();

        byte[] frame1 = { 1, 2, 3 };
        byte[] frame2 = { 4, 5, 6 };
        byte[] frame3 = { 7, 8, 9, 10 };

        slot.PublishFrame(frame1);
        slot.PublishFrame(frame2);
        slot.PublishFrame(frame3);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        byte[]? consumed = await slot.WaitAndTakeFrameAsync(cts.Token);

        Assert.NotNull(consumed);
        Assert.Equal(frame3, consumed);
    }

    [Fact]
    public async Task WaitAndTakeFrameAsync_UnblocksOnAsyncPublish()
    {
        var slot = new LatestFrameSlot();
        byte[] expected = { 0xFF, 0xD8, 0xFF, 0xD9 };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Task<byte[]?> waiter = slot.WaitAndTakeFrameAsync(cts.Token);
        await Task.Delay(30, cts.Token);
        slot.PublishFrame(expected);

        byte[]? result = await waiter;
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task WaitAndTakeFrameAsync_RespectsCancellationWhenEmpty()
    {
        var slot = new LatestFrameSlot();
        using var cts = new CancellationTokenSource(40);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slot.WaitAndTakeFrameAsync(cts.Token));
    }
}
