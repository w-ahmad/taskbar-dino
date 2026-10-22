using Microsoft.Win32;

namespace TaskbarDino.Win32;

/// <summary>"Start with Windows" via the per-user Run key: no admin rights, fully reversible.</summary>
internal static class StartupRegistration
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarDino";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunSubKey);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
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
            Log.Warn($"Failed to update the startup registration: {ex.Message}");
        }
    }
}
