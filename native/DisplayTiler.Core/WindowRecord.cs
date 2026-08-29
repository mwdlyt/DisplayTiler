namespace DisplayTiler.Core;

public sealed record WindowRecord(
    nint Handle,
    uint ProcessId,
    string ProcessPath,
    string ProcessName,
    string Title,
    string ClassName,
    string? AppUserModelId,
    long LastActivatedUnixMilliseconds)
{
    public string ApplicationKey => !string.IsNullOrWhiteSpace(AppUserModelId)
        ? $"aumid:{AppUserModelId}"
        : !string.IsNullOrWhiteSpace(ProcessPath)
            ? $"path:{ProcessPath}"
            : !string.IsNullOrWhiteSpace(ProcessName)
                ? $"process:{ProcessName}"
            : $"pid:{ProcessId}";

    public string ApplicationName
    {
        get
        {
            var executable = !string.IsNullOrWhiteSpace(ProcessPath)
                ? Path.GetFileNameWithoutExtension(ProcessPath)
                : ProcessName;
            if (string.IsNullOrWhiteSpace(executable))
                return string.IsNullOrWhiteSpace(AppUserModelId) ? FallbackWindowName() : AppUserModelId;
            return executable.ToLowerInvariant() switch
            {
                "chrome" => "Google Chrome",
                "msedge" => "Microsoft Edge",
                "explorer" => "File Explorer",
                "code" => "Visual Studio Code",
                "windowsterminal" => "Windows Terminal",
                "taskmgr" => "Task Manager",
                "systemsettings" => "Settings",
                "applicationframehost" => FallbackWindowName(),
                "searchhost" => "Windows Search",
                "startmenuexperiencehost" => "Start",
                "shellexperiencehost" => "Windows Shell Experience",
                _ => char.ToUpperInvariant(executable[0]) + executable[1..],
            };
        }
    }

    public string ApplicationIconPath => ProcessPath;

    private string FallbackWindowName() => !string.IsNullOrWhiteSpace(Title)
        ? Title
        : !string.IsNullOrWhiteSpace(ClassName)
            ? ClassName
            : "Unknown app";
}
