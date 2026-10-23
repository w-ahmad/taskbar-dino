using TaskbarDino.Core.Graphics;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace TaskbarDino.Win32;

/// <summary>
/// Renders context-menu icons as 32-bit premultiplied bitmaps, which Win32 menus draw with proper
/// alpha: Segoe Fluent Icons glyphs (the Windows 11 icon font, falling back to Segoe MDL2 Assets
/// on Windows 10) in the menu text colour, and solid colour swatches for the theme list. GDI has
/// no alpha text output, so a glyph is drawn white-on-black and its brightness becomes the alpha.
/// Bitmaps live only while a menu is open.
/// </summary>
internal sealed unsafe class MenuIcons : IDisposable
{
    public const char Play = '';
    public const char Stop = '';
    public const char Clock = '';
    public const char Show = '';
    public const char Color = '';
    public const char Size = '';
    public const char Personality = '';
    public const char FullScreen = '';
    public const char Memory = '';
    public const char Power = '';
    public const char Undo = '';
    public const char OpenFile = '';
    public const char Reset = '';
    public const char Exit = '';

    private readonly List<HBITMAP> _bitmaps = new(24);
    private readonly int _size;
    private readonly uint _textColor; // COLORREF (0x00BBGGRR)

    public MenuIcons(uint dpi)
    {
        _size = Math.Max(12, (int)MathF.Round(16f * dpi / 96f));
        _textColor = PInvoke.GetSysColor(SYS_COLOR_INDEX.COLOR_MENUTEXT);
    }

    /// <summary>A Fluent glyph in the menu text colour, or <see cref="HBITMAP.Null"/> if GDI fails.</summary>
    public HBITMAP Glyph(char code)
    {
        return Render(bits =>
        {
            HDC dc = PInvoke.CreateCompatibleDC(HDC.Null);
            if (dc.IsNull)
            {
                return false;
            }

            HBITMAP dib = bits.Bitmap;
            HGDIOBJ oldBitmap = PInvoke.SelectObject(dc, dib);
            HFONT font = CreateIconFont("Segoe Fluent Icons");
            if (font.IsNull)
            {
                font = CreateIconFont("Segoe MDL2 Assets");
            }

            HGDIOBJ oldFont = PInvoke.SelectObject(dc, font);
            _ = PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);
            _ = PInvoke.SetTextColor(dc, new COLORREF(0x00FFFFFF));
            var rect = new RECT { left = 0, top = 0, right = _size, bottom = _size };
            char glyph = code;
            _ = PInvoke.DrawText(dc, &glyph, 1, &rect, DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOCLIP);

            _ = PInvoke.SelectObject(dc, oldFont);
            _ = PInvoke.SelectObject(dc, oldBitmap);
            _ = PInvoke.DeleteObject(font);
            _ = PInvoke.DeleteDC(dc);

            // Brightness → alpha, tinted with the menu text colour (premultiplied).
            byte r = (byte)(_textColor & 0xFF), g = (byte)((_textColor >> 8) & 0xFF), b = (byte)((_textColor >> 16) & 0xFF);
            uint* px = bits.Pixels;
            for (int i = 0; i < _size * _size; i++)
            {
                uint v = px[i];
                uint a = Math.Max((v >> 16) & 0xFF, Math.Max((v >> 8) & 0xFF, v & 0xFF));
                px[i] = (a << 24) | ((b * a / 255) << 16) | ((g * a / 255) << 8) | (r * a / 255);
            }

            return true;
        });
    }

    /// <summary>A rounded colour swatch.</summary>
    public HBITMAP Swatch(Rgb color)
    {
        return Render(bits =>
        {
            uint* px = bits.Pixels;
            int n = _size;
            float radius = n / 2f - 1f;
            float c = n / 2f;
            uint outline = 0xFF000000u | ((uint)(color.B * 6 / 10) << 16) | ((uint)(color.G * 6 / 10) << 8) | (uint)(color.R * 6 / 10);
            uint fill = 0xFF000000u | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = MathF.Sqrt(((x + 0.5f - c) * (x + 0.5f - c)) + ((y + 0.5f - c) * (y + 0.5f - c)));
                    px[(y * n) + x] = d > radius ? 0u : d > radius - 1.2f ? outline : fill;
                }
            }

            return true;
        });
    }

    public void Dispose()
    {
        foreach (HBITMAP bitmap in _bitmaps)
        {
            _ = PInvoke.DeleteObject(bitmap);
        }

        _bitmaps.Clear();
    }

    private readonly struct Dib(HBITMAP bitmap, uint* pixels)
    {
        public readonly HBITMAP Bitmap = bitmap;
        public readonly uint* Pixels = pixels;
    }

    private HBITMAP Render(Func<Dib, bool> paint)
    {
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = _size;
        info.bmiHeader.biHeight = -_size; // top-down
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = 0; // BI_RGB

        void* bits;
        HBITMAP bitmap = PInvoke.CreateDIBSection(HDC.Null, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        if (bitmap.IsNull || bits is null)
        {
            return HBITMAP.Null;
        }

        new Span<byte>(bits, _size * _size * 4).Clear();
        if (!paint(new Dib(bitmap, (uint*)bits)))
        {
            _ = PInvoke.DeleteObject(bitmap);
            return HBITMAP.Null;
        }

        _bitmaps.Add(bitmap);
        return bitmap;
    }

    private HFONT CreateIconFont(string family)
    {
        fixed (char* name = family)
        {
            return PInvoke.CreateFont(-(_size - 2), 0, 0, 0, 400, 0, 0, 0, FONT_CHARSET.DEFAULT_CHARSET, FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS,
                FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS, FONT_QUALITY.CLEARTYPE_QUALITY, 0, name);
        }
    }
}
