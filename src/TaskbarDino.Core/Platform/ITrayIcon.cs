using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Settings;

namespace TaskbarDino.Core.Platform;

/// <summary>Commands the tray context menu can raise (colour choice is separate — see <see cref="ITrayIcon.ThemeSelected"/>).</summary>
public enum TrayCommand
{
    StartGame,
    HideOneHour,
    HideFourHours,
    ToggleStartWithWindows,
    OpenSettings,
    Exit,
}

/// <summary>
/// The system-tray presence: a notification-area icon plus its right-click context menu. Decoupled
/// from rendering; the app supplies the icon pixels and reacts to the raised commands.
/// </summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>Adds the tray icon built from the given premultiplied BGRA pixels, with a tooltip.</summary>
    void Install(ReadOnlySpan<Bgra32> iconPixels, int width, int height, string tooltip);

    /// <summary>Replaces the tray icon image (e.g. after a theme change).</summary>
    void UpdateIcon(ReadOnlySpan<Bgra32> iconPixels, int width, int height);

    /// <summary>Shows the context menu at the cursor (used for right-clicking the dino itself).</summary>
    void ShowMenu();

    /// <summary>Sets the check state of the "Start with Windows" menu item.</summary>
    void SetStartupChecked(bool isChecked);

    /// <summary>Marks the given theme as the selected one in the colour submenu.</summary>
    void SetActiveTheme(DinoTheme theme);

    /// <summary>Marks the given size as the selected one in the size submenu.</summary>
    void SetActiveSize(DinoSize size);

    event EventHandler<TrayCommand>? CommandInvoked;

    event EventHandler<DinoTheme>? ThemeSelected;

    event EventHandler<DinoSize>? SizeSelected;
}

/// <summary>Enables/disables launching Taskbar Dino when the user signs in.</summary>
public interface IStartupService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
