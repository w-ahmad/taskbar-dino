// Renders the built-in sprite sheets to PNG files for visual review (dev tool, not shipped).
// Usage: dotnet run tools/sprite-preview.cs -- <outputDir> [scale]
#:project ../src/TaskbarDino.Core/TaskbarDino.Core.csproj

using System.Buffers.Binary;
using System.IO.Compression;

using TaskbarDino.Core.Content;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Settings;

string outDir = args.Length > 0 ? args[0] : ".";
int scale = args.Length > 1 ? int.Parse(args[1]) : 6;
Directory.CreateDirectory(outDir);

foreach (DinoTheme theme in Enum.GetValues<DinoTheme>())
{
    SpriteSheet dino = DinoSpriteSheet.Create(theme, new Rgb(0x00, 0x78, 0xD4));
    WritePng(Path.Combine(outDir, $"dino-{theme}.png"), dino.Pixels, dino.Width, dino.Height, scale, checker: true);
}

SpriteSheet game = GameSpriteSheet.Create();
WritePng(Path.Combine(outDir, "game.png"), game.Pixels, game.Width, game.Height, scale, checker: true);

Bgra32[] icon = DinoSpriteSheet.CreateIconPixels(DinoTheme.Green, 32);
WritePng(Path.Combine(outDir, "icon-32.png"), icon, 32, 32, 4, checker: true);
Console.WriteLine($"Wrote previews to {Path.GetFullPath(outDir)}");

static void WritePng(string path, Bgra32[] pixels, int width, int height, int scale, bool checker)
{
    int w = width * scale, h = height * scale;
    var raw = new byte[(w * 4 + 1) * h];
    for (int y = 0; y < h; y++)
    {
        int row = y * (w * 4 + 1);
        raw[row] = 0; // filter: none
        for (int x = 0; x < w; x++)
        {
            Bgra32 p = pixels[(y / scale * width) + (x / scale)];
            byte r = p.R, g = p.G, b = p.B;
            if (!p.IsOpaque && checker)
            {
                byte c = ((x / (scale * 4)) + (y / (scale * 4))) % 2 == 0 ? (byte)0x9A : (byte)0xB4;
                (r, g, b) = (c, c, c);
            }

            int o = row + 1 + (x * 4);
            raw[o] = r; raw[o + 1] = g; raw[o + 2] = b; raw[o + 3] = 255;
        }
    }

    using var ms = new MemoryStream();
    ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    Span<byte> ihdr = stackalloc byte[13];
    BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
    BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], h);
    ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
    Chunk(ms, "IHDR", ihdr);
    using (var z = new MemoryStream())
    {
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
    }

    Chunk(ms, "IEND", []);
    File.WriteAllBytes(path, ms.ToArray());
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
