using Microsoft.Win32;

using TaskbarDino.Core.Graphics;

namespace TaskbarDino.Win32;

/// <summary>
/// Reads the Windows accent colour from <c>HKCU\…\Explorer\Accent</c> (<c>AccentColorMenu</c>, an
/// ABGR DWORD, or the <c>AccentPalette</c> blob). The DWM colorization value is only a last resort —
/// it is the window tint, which can differ from the accent. Falls back to Windows blue.
/// </summary>
internal static class AccentColor
{
    private static readonly Rgb Fallback = new(0x00, 0x78, 0xD4);

    public static Rgb Read()
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

                if (accent.GetValue("AccentPalette") is byte[] palette && palette.Length >= 16)
                {
                    return new Rgb(palette[12], palette[13], palette[14]); // entry 3 = the standard shade
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
        }

        return Fallback;
    }

    private static Rgb FromAbgr(uint value) => new((byte)value, (byte)(value >> 8), (byte)(value >> 16));
}
