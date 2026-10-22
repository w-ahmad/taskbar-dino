namespace TaskbarDino.Core.Platform;

/// <summary>
/// Identifies the current foreground window and the process behind it. The Home Position provider
/// uses this to locate the matching taskbar button; the app uses it to debounce rapid switches.
/// </summary>
public sealed record ActiveWindowInfo(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string Title);
