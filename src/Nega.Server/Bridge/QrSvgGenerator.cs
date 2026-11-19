using System.Text;

namespace Nega.Server.Bridge;

/// <summary>
/// Bộ sinh mã QR Code chuẩn ISO/IEC 18004 (Byte Mode, ECC Level L, Versions 1..6) thuần C# xuất ra định dạng SVG.
/// Hoạt động 100% offline trong mạng LAN không cần kết nối Internet hay thư viện bên ngoài.
/// </summary>
public static class QrSvgGenerator
{
    // Thông số Version 1..6 ở mức sửa lỗi L (Low): (TotalCodewords, EccCodewordsPerBlock, NumBlocks)
    private static readonly (int Size, int TotalCw, int DataCw, int EccCw, int[] Align)[] Versions =
    {
        (21,  26,  19,  7, Array.Empty<int>()),
        (25,  44,  34, 10, new[] { 6, 18 }),
        (29,  70,  55, 15, new[] { 6, 22 }),
        (33, 100,  80, 20, new[] { 6, 26 }),
        (37, 134, 108, 26, new[] { 6, 30 }),
        (41, 172, 136, 18, new[] { 6, 34 }) // V6-L has 2 blocks of (68 data, 18 ecc)
    };

    private static readonly byte[] GfExp = new byte[512];
    private static readonly byte[] GfLog = new byte[256];

    static QrSvgGenerator()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)x;
            GfLog[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++)
        {
            GfExp[i] = GfExp[i - 255];
        }
    }

    public static string GenerateSvg(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text ?? string.Empty);
        if (payload.Length > 130)
        {
            payload = payload[..130];
        }

        int verIdx = 0;
        while (verIdx < Versions.Length - 1 && payload.Length + 3 > Versions[verIdx].DataCw)
        {
            verIdx++;
        }

        var v = Versions[verIdx];
        byte[] dataCw = EncodeDataCodewords(payload, v.DataCw);
        byte[] allCw = BuildFinalMessage(dataCw, verIdx);

        int size = v.Size;
        bool[,] modules = new bool[size, size];
        bool[,] isFunc = new bool[size, size];

        DrawFunctionPatterns(modules, isFunc, size, v.Align);
        DrawCodewords(modules, isFunc, size, allCw);
        ApplyMask0(modules, isFunc, size);
        DrawFormatBits(modules, size, mask: 0); // ECC L (01) + Mask 0 (000)

        var sb = new StringBuilder();
        int border = 3;
        int viewSize = size + border * 2;
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {viewSize} {viewSize}\" shape-rendering=\"crispEdges\">");
        sb.Append($"<rect width=\"{viewSize}\" height=\"{viewSize}\" fill=\"#FFFFFF\" rx=\"1.5\"/>");
        sb.Append("<path fill=\"#0F172A\" d=\"");
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (modules[y, x])
                {
                    sb.Append($"M{x + border},{y + border}h1v1h-1z");
                }
            }
        }
        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    private static byte[] EncodeDataCodewords(byte[] payload, int capacityCw)
    {
        var bits = new List<bool>(capacityCw * 8);
        void AddBits(int val, int len)
        {
            for (int i = len - 1; i >= 0; i--)
            {
                bits.Add(((val >> i) & 1) != 0);
            }
        }

        AddBits(0b0100, 4); // Byte mode
        AddBits(payload.Length, 8);
        foreach (byte b in payload) AddBits(b, 8);

        int maxBits = capacityCw * 8;
        int term = Math.Min(4, maxBits - bits.Count);
        AddBits(0, term);
        while (bits.Count % 8 != 0) bits.Add(false);

        byte[] result = new byte[capacityCw];
        for (int i = 0; i < bits.Count / 8; i++)
        {
            byte val = 0;
            for (int b = 0; b < 8; b++)
            {
                val = (byte)((val << 1) | (bits[i * 8 + b] ? 1 : 0));
            }
            result[i] = val;
        }

        byte[] padBytes = { 0xEC, 0x11 };
        for (int i = bits.Count / 8, p = 0; i < capacityCw; i++, p ^= 1)
        {
            result[i] = padBytes[p];
        }
        return result;
    }

    private static byte[] BuildFinalMessage(byte[] dataCw, int verIdx)
    {
        var v = Versions[verIdx];
        if (verIdx < 5)
        {
            byte[] ecc = ComputeReedSolomon(dataCw, v.EccCw);
            byte[] res = new byte[v.TotalCw];
            Buffer.BlockCopy(dataCw, 0, res, 0, dataCw.Length);
            Buffer.BlockCopy(ecc, 0, res, dataCw.Length, ecc.Length);
            return res;
        }

        // Version 6-L: 2 blocks of 68 data codewords + 18 ECC codewords interleaved
        byte[] b1 = dataCw[..68];
        byte[] b2 = dataCw[68..136];
        byte[] e1 = ComputeReedSolomon(b1, 18);
        byte[] e2 = ComputeReedSolomon(b2, 18);
        var finalList = new List<byte>(172);
        for (int i = 0; i < 68; i++) { finalList.Add(b1[i]); finalList.Add(b2[i]); }
        for (int i = 0; i < 18; i++) { finalList.Add(e1[i]); finalList.Add(e2[i]); }
        return finalList.ToArray();
    }

    private static byte[] ComputeReedSolomon(byte[] data, int degree)
    {
        byte[] gen = new byte[degree];
        gen[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                gen[j] = GfMul(gen[j], (byte)root);
                if (j + 1 < degree) gen[j] ^= gen[j + 1];
            }
            root = GfMul((byte)root, 0x02);
        }

        byte[] rem = new byte[degree];
        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ rem[0]);
            Array.Copy(rem, 1, rem, 0, degree - 1);
            rem[degree - 1] = 0;
            for (int j = 0; j < degree; j++)
            {
                rem[j] ^= GfMul(gen[j], factor);
            }
        }
        return rem;
    }

    private static byte GfMul(byte x, byte y) =>
        (x == 0 || y == 0) ? (byte)0 : GfExp[GfLog[x] + GfLog[y]];

    private static void DrawFunctionPatterns(bool[,] m, bool[,] fn, int size, int[] align)
    {
        for (int i = 0; i < size; i++)
        {
            SetFunc(m, fn, 6, i, i % 2 == 0);
            SetFunc(m, fn, i, 6, i % 2 == 0);
        }

        DrawFinder(m, fn, size, 3, 3);
        DrawFinder(m, fn, size, size - 4, 3);
        DrawFinder(m, fn, size, 3, size - 4);

        for (int i = 0; i < align.Length; i++)
        {
            for (int j = 0; j < align.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == align.Length - 1) || (i == align.Length - 1 && j == 0))
                    continue;
                DrawAlignment(m, fn, align[i], align[j]);
            }
        }

        // Reserve format info areas
        for (int i = 0; i <= 8; i++)
        {
            if (i != 6)
            {
                fn[8, i] = true;
                fn[i, 8] = true;
            }
        }
        for (int i = 0; i < 8; i++)
        {
            fn[size - 1 - i, 8] = true;
            fn[8, size - 1 - i] = true;
        }
        SetFunc(m, fn, 8, size - 8, true); // Dark module
    }

    private static void DrawFinder(bool[,] m, bool[,] fn, int size, int cx, int cy)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < size && y >= 0 && y < size)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunc(m, fn, x, y, dist != 2 && dist != 4);
                }
            }
        }
    }

    private static void DrawAlignment(bool[,] m, bool[,] fn, int cx, int cy)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                SetFunc(m, fn, cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }
    }

    private static void SetFunc(bool[,] m, bool[,] fn, int x, int y, bool val)
    {
        m[y, x] = val;
        fn[y, x] = true;
    }

    private static void DrawCodewords(bool[,] m, bool[,] fn, int size, byte[] allCw)
    {
        int bitIdx = 0;
        int totalBits = allCw.Length * 8;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;
                    if (!fn[y, x] && bitIdx < totalBits)
                    {
                        m[y, x] = ((allCw[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1) != 0;
                        bitIdx++;
                    }
                }
            }
        }
    }

    private static void ApplyMask0(bool[,] m, bool[,] fn, int size)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (!fn[y, x] && ((x + y) % 2 == 0))
                {
                    m[y, x] = !m[y, x];
                }
            }
        }
    }

    private static void DrawFormatBits(bool[,] m, int size, int mask)
    {
        // Precomputed BCH format bits for ECC Level L (01) and Mask 0 (000) -> 0x77C4 (30660)
        int bits = mask == 0 ? 0x77C4 : 0x72F3;
        bool GetBit(int i) => ((bits >> i) & 1) != 0;

        for (int i = 0; i <= 5; i++) m[8, i] = GetBit(i);
        m[8, 7] = GetBit(6);
        m[8, 8] = GetBit(7);
        m[7, 8] = GetBit(8);
        for (int i = 9; i < 15; i++) m[14 - i, 8] = GetBit(i);

        for (int i = 0; i < 8; i++) m[8, size - 1 - i] = GetBit(i);
        for (int i = 8; i < 15; i++) m[size - 15 + i, 8] = GetBit(i);
    }
}
