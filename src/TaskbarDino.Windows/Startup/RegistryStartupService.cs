using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using TaskbarDino.Core.Platform;

namespace TaskbarDino.Windows.Startup;

/// <summary>
/// Enables "start with Windows" via the per-user Run key (no admin rights, fully reversible). The
/// value is the quoted path to the running executable.
/// </summary>
public sealed class RegistryStartupService(ILogger<RegistryStartupService> logger) : IStartupService
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarDino";

    private readonly ILogger<RegistryStartupService> _logger = logger;

    public bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunSubKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunSubKey);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update the startup registration.");
        }
    }
}
