using System.IO.Compression;
using System.Text;

namespace ArchiveExplorer.Demo;

/// <summary>Minimal PNG writer for 8-bit RGB pixels, so the demo needs no image library.</summary>
internal static class PngEncoder
{
    public static byte[] Encode(int width, int height, byte[] rgb, CompressionLevel level = CompressionLevel.Optimal)
    {
        var stride = width * 3;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0; // filter: none
            Buffer.BlockCopy(rgb, y * stride, raw, y * (stride + 1) + 1, stride);
        }

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, level, leaveOpen: true))
        {
            z.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", idat.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        s.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32([.. typeBytes, .. data]));
        s.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }
}
