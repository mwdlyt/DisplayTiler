namespace DisplayTiler.Core.Tests;

// The switcher is only as good as its grouping: every window of one application has to land in one
// tile, and the tiles have to come back in the order the user last touched them. Both rules are pure
// functions of a window list, so they are worth pinning down here rather than by opening the overlay.
public class SwitcherGrouperTests
{
    private static WindowRecord Window(
        nint handle,
        string processPath,
        long lastActivated,
        string title = "window",
        string? appUserModelId = null,
        uint processId = 100) =>
        new(
            handle,
            processId,
            processPath,
            Path.GetFileNameWithoutExtension(processPath),
            title,
            "ClassName",
            appUserModelId,
            lastActivated);

    [Fact]
    public void Windows_of_the_same_executable_collapse_into_one_group()
    {
        var groups = SwitcherGrouper.Group(new[]
        {
            Window(1, @"C:\Program Files\Chrome\chrome.exe", 30),
            Window(2, @"C:\Program Files\Chrome\chrome.exe", 20),
            Window(3, @"C:\Windows\explorer.exe", 10),
        });

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Single(g => g.Name == "Google Chrome").Windows.Count);
        Assert.Single(groups.Single(g => g.Name == "File Explorer").Windows);
    }

    [Fact]
    public void Paths_differing_only_in_case_are_the_same_application()
    {
        // Windows paths are case insensitive, so two handles reported with different casing are one
        // application. Grouping them apart would show the user the same program twice.
        var groups = SwitcherGrouper.Group(new[]
        {
            Window(1, @"C:\Windows\Explorer.exe", 20),
            Window(2, @"c:\windows\explorer.EXE", 10),
        });

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Windows.Count);
    }

    [Fact]
    public void Groups_are_ordered_by_the_most_recently_activated_window()
    {
        var groups = SwitcherGrouper.Group(new[]
        {
            Window(1, @"C:\apps\slack.exe", 5),
            Window(2, @"C:\apps\code.exe", 50),
            Window(3, @"C:\apps\slack.exe", 90),
        });

        // Slack owns the newest window, so it leads even though its other window is the oldest.
        Assert.Equal(new[] { "Slack", "Visual Studio Code" }, groups.Select(g => g.Name).ToArray());
        Assert.Equal(90, groups[0].LastActivatedUnixMilliseconds);
    }

    [Fact]
    public void Windows_within_a_group_are_ordered_newest_first()
    {
        var groups = SwitcherGrouper.Group(new[]
        {
            Window(1, @"C:\apps\code.exe", 10),
            Window(2, @"C:\apps\code.exe", 90),
            Window(3, @"C:\apps\code.exe", 50),
        });

        Assert.Equal(new nint[] { 2, 3, 1 }, groups[0].Windows.Select(w => w.Handle).ToArray());
    }

    [Fact]
    public void Groups_activated_at_the_same_moment_fall_back_to_name_order()
    {
        // A first enumeration can stamp every window with the same timestamp. Without the name
        // tiebreak the tile order would then be whatever order the enumeration happened to produce.
        var groups = SwitcherGrouper.Group(new[]
        {
            Window(1, @"C:\apps\zoom.exe", 42),
            Window(2, @"C:\apps\notepad.exe", 42),
            Window(3, @"C:\apps\firefox.exe", 42),
        });

        Assert.Equal(new[] { "Firefox", "Notepad", "Zoom" }, groups.Select(g => g.Name).ToArray());
    }

    [Fact]
    public void A_packaged_app_groups_by_its_model_id_rather_than_its_host_process()
    {
        // Packaged apps all run inside ApplicationFrameHost.exe. Grouping on the path would fold
        // every unrelated store app into a single tile.
        var groups = SwitcherGrouper.Group(new[]
        {
            Window(1, @"C:\Windows\ApplicationFrameHost.exe", 20, "Mail", "Microsoft.Mail_8wekyb3d8bbwe!App"),
            Window(2, @"C:\Windows\ApplicationFrameHost.exe", 10, "Photos", "Microsoft.Photos_8wekyb3d8bbwe!App"),
        });

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void An_empty_window_list_produces_no_groups()
    {
        Assert.Empty(SwitcherGrouper.Group(Array.Empty<WindowRecord>()));
    }
}
