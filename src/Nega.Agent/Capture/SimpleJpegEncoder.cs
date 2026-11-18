namespace Nega.Agent.Capture;

/// <summary>
/// Bộ dựng khung hình RGB và mã hóa chuẩn JPEG Baseline (JFIF / ITU-T T.81 DCT + Huffman) thuần C#.
/// Không phụ thuộc thư viện native bên ngoài, cho phép nén ảnh màn hình sang mảng byte JPEG (0xFF 0xD8 ... 0xFF 0xD9)
/// với hệ số chất lượng (Quality 10..95) tùy chỉnh theo chế độ Grid View hoặc Zoom View.
/// </summary>
public static class SimpleJpegEncoder
{
    private static readonly int[] ZigZag =
    {
         0,  1,  5,  6, 14, 15, 27, 28,
         2,  4,  7, 13, 16, 26, 29, 42,
         3,  8, 12, 17, 25, 30, 41, 43,
         9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63
    };

    private static readonly int[] StdLumQuant =
    {
        16, 11, 10, 16,  24,  40,  51,  61,
        12, 12, 14, 19,  26,  58,  60,  55,
        14, 13, 16, 24,  40,  57,  69,  56,
        14, 17, 22, 29,  51,  87,  80,  62,
        18, 22, 37, 56,  68, 109, 103,  77,
        24, 35, 55, 64,  81, 104, 113,  92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103,  99
    };

    private static readonly int[] StdChromQuant =
    {
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99
    };

    private static readonly byte[] BitsDcLuminance = { 0, 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] ValDcLuminance = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

    private static readonly byte[] BitsDcChrominance = { 0, 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };
    private static readonly byte[] ValDcChrominance = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

    private static readonly byte[] BitsAcLuminance = { 0, 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d };
    private static readonly byte[] ValAcLuminance =
    {
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08, 0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
        0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
        0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
        0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
        0xf9, 0xfa
    };

    private static readonly byte[] BitsAcChrominance = { 0, 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77 };
    private static readonly byte[] ValAcChrominance =
    {
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
        0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
        0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
        0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
        0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
        0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
        0xf9, 0xfa
    };

    private static readonly (ushort Code, byte Len)[] HufDcLum = BuildHuffmanTable(BitsDcLuminance, ValDcLuminance);
    private static readonly (ushort Code, byte Len)[] HufDcChrom = BuildHuffmanTable(BitsDcChrominance, ValDcChrominance);
    private static readonly (ushort Code, byte Len)[] HufAcLum = BuildHuffmanTable(BitsAcLuminance, ValAcLuminance);
    private static readonly (ushort Code, byte Len)[] HufAcChrom = BuildHuffmanTable(BitsAcChrominance, ValAcChrominance);
    private static readonly float[,] CosTable = BuildCosTable();

    private static float[,] BuildCosTable()
    {
        var table = new float[8, 8];
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                table[i, j] = (float)Math.Cos(((2 * i + 1) * j * Math.PI) / 16.0);
            }
        }
        return table;
    }

    private static (ushort Code, byte Len)[] BuildHuffmanTable(byte[] bits, byte[] vals)
    {
        var table = new (ushort Code, byte Len)[256];
        int p = 0;
        ushort code = 0;
        for (byte l = 1; l <= 16; l++)
        {
            int count = bits[l];
            for (int i = 0; i < count; i++)
            {
                table[vals[p++]] = (code, l);
                code++;
            }
            code <<= 1;
        }
        return table;
    }

    public static byte[] EncodeRgbToJpeg(byte[] rgb, int width, int height, int quality = 50)
    {
        quality = Math.Clamp(quality, 10, 95);
        int scale = quality < 50 ? 5000 / quality : 200 - quality * 2;

        byte[] lumTableZigZag = new byte[64];
        byte[] chromTableZigZag = new byte[64];
        float[] invLum = new float[64];
        float[] invChrom = new float[64];

        for (int i = 0; i < 64; i++)
        {
            int lq = Math.Clamp((StdLumQuant[i] * scale + 50) / 100, 1, 255);
            int cq = Math.Clamp((StdChromQuant[i] * scale + 50) / 100, 1, 255);
            lumTableZigZag[ZigZag[i]] = (byte)lq;
            chromTableZigZag[ZigZag[i]] = (byte)cq;
            invLum[i] = 1.0f / lq;
            invChrom[i] = 1.0f / cq;
        }

        using var ms = new MemoryStream(width * height / 2);
        // SOI
        ms.WriteByte(0xFF);
        ms.WriteByte(0xD8);

        // APP0 JFIF
        ms.Write(new byte[]
        {
            0xFF, 0xE0, 0x00, 0x10,
            (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
            0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00
        });

        // DQT
        WriteDqt(ms, lumTableZigZag, chromTableZigZag);

        // SOF0
        ms.Write(new byte[]
        {
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)(height & 0xFF),
            (byte)(width >> 8), (byte)(width & 0xFF),
            0x03,
            0x01, 0x11, 0x00,
            0x02, 0x11, 0x01,
            0x03, 0x11, 0x01
        });

        // DHT
        WriteDht(ms);

        // SOS
        ms.Write(new byte[]
        {
            0xFF, 0xDA, 0x00, 0x0C, 0x03,
            0x01, 0x00,
            0x02, 0x11,
            0x03, 0x11,
            0x00, 0x3F, 0x00
        });

        var bw = new BitWriter(ms);
        int lastYDc = 0, lastCbDc = 0, lastCrDc = 0;
        float[] blockY = new float[64];
        float[] blockCb = new float[64];
        float[] blockCr = new float[64];
        int[] quantBlock = new int[64];

        for (int by = 0; by < height; by += 8)
        {
            for (int bx = 0; bx < width; bx += 8)
            {
                int idx = 0;
                for (int y = 0; y < 8; y++)
                {
                    int py = Math.Min(by + y, height - 1);
                    int rowOffset = py * width * 3;
                    for (int x = 0; x < 8; x++)
                    {
                        int px = Math.Min(bx + x, width - 1);
                        int p = rowOffset + px * 3;
                        float r = rgb[p];
                        float g = rgb[p + 1];
                        float b = rgb[p + 2];

                        blockY[idx] = (0.299f * r + 0.587f * g + 0.114f * b) - 128f;
                        blockCb[idx] = (-0.168736f * r - 0.331264f * g + 0.5f * b);
                        blockCr[idx] = (0.5f * r - 0.418688f * g - 0.081312f * b);
                        idx++;
                    }
                }

                ForwardDctAndQuantize(blockY, invLum, quantBlock);
                lastYDc = EncodeBlock(bw, quantBlock, lastYDc, HufDcLum, HufAcLum);

                ForwardDctAndQuantize(blockCb, invChrom, quantBlock);
                lastCbDc = EncodeBlock(bw, quantBlock, lastCbDc, HufDcChrom, HufAcChrom);

                ForwardDctAndQuantize(blockCr, invChrom, quantBlock);
                lastCrDc = EncodeBlock(bw, quantBlock, lastCrDc, HufDcChrom, HufAcChrom);
            }
        }

        bw.Flush();

        // EOI
        ms.WriteByte(0xFF);
        ms.WriteByte(0xD9);
        return ms.ToArray();
    }

    private static void ForwardDctAndQuantize(float[] input, float[] invQuant, int[] outputZigZag)
    {
        Span<float> temp = stackalloc float[64];

        // 1D DCT rows
        for (int y = 0; y < 8; y++)
        {
            int row = y * 8;
            for (int u = 0; u < 8; u++)
            {
                float sum = 0f;
                for (int x = 0; x < 8; x++)
                {
                    sum += input[row + x] * CosTable[x, u];
                }
                temp[row + u] = sum * (u == 0 ? 0.35355339f : 0.5f);
            }
        }

        // 1D DCT cols + quantize into ZigZag order
        for (int x = 0; x < 8; x++)
        {
            for (int v = 0; v < 8; v++)
            {
                float sum = 0f;
                for (int y = 0; y < 8; y++)
                {
                    sum += temp[y * 8 + x] * CosTable[y, v];
                }
                float dct = sum * (v == 0 ? 0.35355339f : 0.5f);
                int idx = v * 8 + x;
                outputZigZag[ZigZag[idx]] = (int)MathF.Round(dct * invQuant[idx]);
            }
        }
    }

    private static int EncodeBlock(
        BitWriter bw,
        int[] zz,
        int lastDc,
        (ushort Code, byte Len)[] hufDc,
        (ushort Code, byte Len)[] hufAc)
    {
        int dcDiff = zz[0] - lastDc;
        if (dcDiff == 0)
        {
            bw.WriteBits(hufDc[0].Code, hufDc[0].Len);
        }
        else
        {
            var (cat, bits) = GetCategoryAndBits(dcDiff);
            bw.WriteBits(hufDc[cat].Code, hufDc[cat].Len);
            bw.WriteBits(bits, cat);
        }

        int endPos = 63;
        while (endPos > 0 && zz[endPos] == 0)
        {
            endPos--;
        }

        int zeroRun = 0;
        for (int i = 1; i <= endPos; i++)
        {
            int val = zz[i];
            if (val == 0)
            {
                zeroRun++;
                continue;
            }

            while (zeroRun >= 16)
            {
                bw.WriteBits(hufAc[0xF0].Code, hufAc[0xF0].Len);
                zeroRun -= 16;
            }

            var (cat, bits) = GetCategoryAndBits(val);
            int sym = (zeroRun << 4) | cat;
            bw.WriteBits(hufAc[sym].Code, hufAc[sym].Len);
            bw.WriteBits(bits, cat);
            zeroRun = 0;
        }

        if (endPos < 63)
        {
            bw.WriteBits(hufAc[0x00].Code, hufAc[0x00].Len); // EOB
        }

        return zz[0];
    }

    private static (byte Category, ushort Bits) GetCategoryAndBits(int value)
    {
        int absVal = value < 0 ? -value : value;
        byte cat = 0;
        while (absVal > 0)
        {
            absVal >>= 1;
            cat++;
        }
        int bits = value < 0 ? value + ((1 << cat) - 1) : value;
        return (cat, (ushort)bits);
    }

    private static void WriteDqt(Stream s, byte[] lum, byte[] chrom)
    {
        s.Write(new byte[] { 0xFF, 0xDB, 0x00, 132, 0x00 });
        s.Write(lum, 0, 64);
        s.WriteByte(0x01);
        s.Write(chrom, 0, 64);
    }

    private static void WriteDht(Stream s)
    {
        int len = 2 + (1 + 16 + ValDcLuminance.Length)
                    + (1 + 16 + ValAcLuminance.Length)
                    + (1 + 16 + ValDcChrominance.Length)
                    + (1 + 16 + ValAcChrominance.Length);
        s.WriteByte(0xFF);
        s.WriteByte(0xC4);
        s.WriteByte((byte)(len >> 8));
        s.WriteByte((byte)(len & 0xFF));

        WriteHuffTableSpec(s, 0x00, BitsDcLuminance, ValDcLuminance);
        WriteHuffTableSpec(s, 0x10, BitsAcLuminance, ValAcLuminance);
        WriteHuffTableSpec(s, 0x01, BitsDcChrominance, ValDcChrominance);
        WriteHuffTableSpec(s, 0x11, BitsAcChrominance, ValAcChrominance);
    }

    private static void WriteHuffTableSpec(Stream s, byte classAndId, byte[] bits, byte[] vals)
    {
        s.WriteByte(classAndId);
        s.Write(bits, 1, 16);
        s.Write(vals, 0, vals.Length);
    }

    private sealed class BitWriter
    {
        private readonly Stream _stream;
        private uint _bitBuffer;
        private int _bitCount;

        public BitWriter(Stream stream)
        {
            _stream = stream;
        }

        public void WriteBits(ushort code, int length)
        {
            _bitBuffer = (_bitBuffer << length) | (uint)(code & ((1 << length) - 1));
            _bitCount += length;

            while (_bitCount >= 8)
            {
                _bitCount -= 8;
                byte b = (byte)((_bitBuffer >> _bitCount) & 0xFF);
                _stream.WriteByte(b);
                if (b == 0xFF)
                {
                    _stream.WriteByte(0x00); // Byte stuffing chuẩn JPEG
                }
            }
        }

        public void Flush()
        {
            if (_bitCount > 0)
            {
                int pad = 8 - _bitCount;
                WriteBits((ushort)((1 << pad) - 1), pad);
            }
        }
    }
}
