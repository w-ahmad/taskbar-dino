// Generates the application icon (src/TaskbarDino/Assets/TaskbarDino.ico) from the built-in dino art.
// Usage: dotnet run tools/make-icon.cs -- <output.ico>
#:project ../src/TaskbarDino.Core/TaskbarDino.Core.csproj

using System.Buffers.Binary;
using System.IO.Compression;

using TaskbarDino.Core.Content;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Settings;

string output = args.Length > 0 ? args[0] : "TaskbarDino.ico";
int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var images = new List<byte[]>();
foreach (int size in sizes)
{
    Bgra32[] pixels = DinoSpriteSheet.CreateIconPixels(DinoTheme.Green, size);
    images.Add(Png(pixels, size, size));
}

using var ico = new MemoryStream();
Span<byte> header = stackalloc byte[6];
BinaryPrimitives.WriteUInt16LittleEndian(header, 0);
BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)sizes.Length);
ico.Write(header);

int offset = 6 + (16 * sizes.Length);
Span<byte> entryBuffer = stackalloc byte[16];
for (int i = 0; i < sizes.Length; i++)
{
    Span<byte> entry = entryBuffer;
    entry[0] = (byte)(sizes[i] == 256 ? 0 : sizes[i]);
    entry[1] = (byte)(sizes[i] == 256 ? 0 : sizes[i]);
    entry[2] = 0; entry[3] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
    BinaryPrimitives.WriteInt32LittleEndian(entry[8..], images[i].Length);
    BinaryPrimitives.WriteInt32LittleEndian(entry[12..], offset);
    ico.Write(entry);
    offset += images[i].Length;
}

foreach (byte[] image in images)
{
    ico.Write(image);
}

File.WriteAllBytes(output, ico.ToArray());
Console.WriteLine($"Wrote {output} ({ico.Length} bytes, {sizes.Length} sizes)");

static byte[] Png(Bgra32[] pixels, int w, int h)
{
    var raw = new byte[(w * 4 + 1) * h];
    for (int y = 0; y < h; y++)
    {
        int row = y * (w * 4 + 1);
        for (int x = 0; x < w; x++)
        {
            Bgra32 p = pixels[(y * w) + x];
            int o = row + 1 + (x * 4);
            raw[o] = p.R; raw[o + 1] = p.G; raw[o + 2] = p.B; raw[o + 3] = p.A;
        }
    }

    using var ms = new MemoryStream();
    ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    Span<byte> ihdr = stackalloc byte[13];
    BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
    BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], h);
    ihdr[8] = 8; ihdr[9] = 6;
    Chunk(ms, "IHDR", ihdr);
    using (var z = new MemoryStream())
    {
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
    }

    Chunk(ms, "IEND", []);
    return ms.ToArray();
}

static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
{
    Span<byte> len = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
    s.Write(len);
    byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
    s.Write(typeBytes);
    s.Write(data);
    uint crc = Crc32(Crc32(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
    Span<byte> c = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(c, crc);
    s.Write(c);
}

static uint Crc32(uint crc, ReadOnlySpan<byte> data)
{
    foreach (byte b in data)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
    }

    return crc;
}
