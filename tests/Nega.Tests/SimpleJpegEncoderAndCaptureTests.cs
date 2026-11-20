using Nega.Agent.Capture;
using Nega.Common.Models;

namespace Nega.Tests;

public sealed class SimpleJpegEncoderAndCaptureTests
{
    [Fact]
    public void EncodeRgbToJpeg_ProducesValidJpegSoiAndEoiMarkers()
    {
        const int w = 64;
        const int h = 48;
        byte[] rgb = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 3;
                rgb[idx] = (byte)(x * 4);
                rgb[idx + 1] = (byte)(y * 5);
                rgb[idx + 2] = (byte)((x + y) * 2);
            }
        }

        byte[] jpeg = SimpleJpegEncoder.EncodeRgbToJpeg(rgb, w, h, quality: 50);

        Assert.True(jpeg.Length > 200);
        Assert.Equal(0xFF, jpeg[0]); // SOI
        Assert.Equal(0xD8, jpeg[1]);
        Assert.Equal(0xFF, jpeg[^2]); // EOI
        Assert.Equal(0xD9, jpeg[^1]);
    }

    [Fact]
    public void EncodeRgbToJpeg_HigherQualityProducesLargerPayloadOnTexturedScene()
    {
        const int w = 128;
        const int h = 72;
        byte[] rgb = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 3;
                rgb[idx] = (byte)((x * 13 + y * 7) % 256);
                rgb[idx + 1] = (byte)((x * 3 ^ y * 11) % 256);
                rgb[idx + 2] = (byte)((x + y * 17) % 256);
            }
        }

        byte[] lowQ = SimpleJpegEncoder.EncodeRgbToJpeg(rgb, w, h, quality: 20);
        byte[] highQ = SimpleJpegEncoder.EncodeRgbToJpeg(rgb, w, h, quality: 85);

        Assert.True(highQ.Length > lowQ.Length);
    }

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(true, null, null)]
    [InlineData(false, "THÔNG BÁO KHẨN", 12)]
    public async Task ScreenCaptureService_SimulatedStates_RenderValidJpeg(
        bool isLocked,
        string? alertTitle,
        int? shutdownSec)
    {
        var capture = new ScreenCaptureService("MAY-01", "10.10.0.101", isSimulated: true, themeSeed: 1);
        byte[] jpeg = await capture.CaptureJpegFrameAsync(
            StreamConfigPayload.CreateGridDefault(),
            isLocked,
            alertTitle,
            alertTitle is null ? null : "Nộp bài thực hành ngay",
            shutdownSec);

        Assert.True(jpeg.Length > 500);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        Assert.Equal(0xFF, jpeg[^2]);
        Assert.Equal(0xD9, jpeg[^1]);
    }
}
