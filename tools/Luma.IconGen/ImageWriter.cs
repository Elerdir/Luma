using System.Buffers.Binary;
using System.IO.Compression;

namespace Luma.IconGen;

/// <summary>
/// Minimal PNG and ICO writers — enough to emit app icons without pulling in an
/// imaging dependency. ICO frames are stored as PNG, which Windows supports.
/// </summary>
public static class ImageWriter
{
    /// <summary>Encode RGBA pixels as a PNG (8-bit, colour type 6).</summary>
    public static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // truecolour with alpha
        ihdr[10] = 0;  // deflate
        ihdr[11] = 0;  // adaptive filtering
        ihdr[12] = 0;  // no interlace
        WriteChunk(output, "IHDR", ihdr);

        // IDAT: each scanline prefixed with filter byte 0 (None), zlib-compressed.
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw.ToArray());
        WriteChunk(output, "IDAT", compressed.ToArray());

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>Pack several PNG-encoded frames into a single .ico container.</summary>
    public static byte[] EncodeIco(IReadOnlyList<(int Size, byte[] Png)> frames)
    {
        using var output = new MemoryStream();

        // ICONDIR
        Write16(output, 0);                    // reserved
        Write16(output, 1);                    // type: icon
        Write16(output, (ushort)frames.Count);

        // ICONDIRENTRY table; image data follows it.
        var offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            output.WriteByte(size >= 256 ? (byte)0 : (byte)size); // 0 means 256
            output.WriteByte(size >= 256 ? (byte)0 : (byte)size);
            output.WriteByte(0);               // palette colours
            output.WriteByte(0);               // reserved
            Write16(output, 1);                // colour planes
            Write16(output, 32);               // bits per pixel
            Write32(output, (uint)png.Length);
            Write32(output, (uint)offset);
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
            output.Write(png);

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in first) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in second) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void Write16(Stream s, ushort v)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, v);
        s.Write(buf);
    }

    private static void Write32(Stream s, uint v)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, v);
        s.Write(buf);
    }
}
