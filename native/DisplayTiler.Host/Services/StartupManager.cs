using Microsoft.Win32;

namespace DisplayTiler.Host.Services;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DisplayTiler";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("DisplayTiler could not determine its executable path.");
        key.SetValue(ValueName, $"\"{executablePath}\" --startup", RegistryValueKind.String);
    }
}
