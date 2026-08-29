using System.Text.Json;

namespace DisplayTiler.Host.Services;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayTiler");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public bool AltTabReplacementEnabled { get; set; } = true;
    public bool ActivateOnAltRelease { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public SwitcherLayoutMode LayoutMode { get; set; } = SwitcherLayoutMode.PackedGrid;

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            // Not an IOException, so it would otherwise escape and kill the process before the tray
            // icon ever appears - with no window, that failure would be completely invisible.
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }
}
