namespace DisplayTiler.Host.Services;

/// <summary>Supplies the notification-area icon.</summary>
internal static class TrayIconFactory
{
    /// <summary>Set as an explicit LogicalName in the project file, so it does not track the root namespace.</summary>
    private const string IconResourceName = "DisplayTiler.ico";

    /// <summary>
    /// Loads the application icon at exactly the size the shell wants for the notification area.
    /// </summary>
    /// <remarks>
    /// SmallIconSize follows the user's DPI, and DisplayTiler.ico carries a purpose-drawn image at
    /// every size the shell asks for, so Windows selects one rather than resampling the 256px
    /// artwork each time it paints the tray. The caller owns the returned icon and disposes it, so
    /// the fallbacks hand back something that is safe to dispose - never a shared SystemIcons
    /// instance, which is why the last resort is a clone.
    /// </remarks>
    public static Icon Create()
    {
        var stream = typeof(TrayIconFactory).Assembly.GetManifestResourceStream(IconResourceName);
        if (stream is not null)
        {
            using (stream) return new Icon(stream, SystemInformation.SmallIconSize);
        }

        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executablePath))
            {
                var extracted = Icon.ExtractAssociatedIcon(executablePath);
                if (extracted is not null) return extracted;
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            // Fall through to the generic icon below.
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
