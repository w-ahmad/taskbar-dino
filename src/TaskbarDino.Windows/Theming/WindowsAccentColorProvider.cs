using Microsoft.Win32;

using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Platform;

namespace TaskbarDino.Windows.Theming;

/// <summary>
/// Reads the current Windows accent colour. The reliable source is
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent</c>: <c>AccentColorMenu</c>
/// (a <c>0xAABBGGRR</c> DWORD — the shade shown in menus/UI) or, failing that, the standard shade in
/// the binary <c>AccentPalette</c>. (The <c>DWM\AccentColor</c> value is the window <em>colorization</em>
/// tint, which can differ from the accent, so it's only a last resort.) Falls back to a Windows blue.
/// </summary>
public sealed class WindowsAccentColorProvider : IAccentColorProvider
{
    private static readonly Rgba Fallback = Rgba.Rgb(0x00, 0x78, 0xD4);

    public Rgba GetAccentColor()
    {
        try
        {
            using RegistryKey? accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (accent is not null)
            {
                if (accent.GetValue("AccentColorMenu") is int menu)
                {
                    return FromAbgr(unchecked((uint)menu));
                }

                // 8 RGBA entries; index 3 is the standard accent shade users see.
                if (accent.GetValue("AccentPalette") is byte[] palette && palette.Length >= 16)
                {
                    return Rgba.Rgb(palette[12], palette[13], palette[14]);
                }
            }

            using RegistryKey? dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (dwm?.GetValue("AccentColor") is int raw)
            {
                return FromAbgr(unchecked((uint)raw));
            }
        }
        catch (Exception)
        {
            // Unreadable (permissions, missing keys) — use the fallback.
        }

        return Fallback;
    }

    private static Rgba FromAbgr(uint value) =>
        Rgba.Rgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
}
