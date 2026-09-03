namespace DisplayTiler.Core.Tests;

// ApplicationKey decides what counts as one application and ApplicationName is the label the user
// reads on the tile. Both degrade through a chain of fallbacks for windows that report almost
// nothing about themselves, which is exactly where the behaviour is easy to break unnoticed.
public class WindowRecordTests
{
    private static WindowRecord Record(
        string processPath = "",
        string processName = "",
        string? appUserModelId = null,
        string title = "",
        string className = "",
        uint processId = 4321) =>
        new(1, processId, processPath, processName, title, className, appUserModelId, 0);

    [Theory]
    [InlineData("Microsoft.Mail_8wekyb3d8bbwe!App", @"C:\Windows\ApplicationFrameHost.exe", "notepad", "aumid:Microsoft.Mail_8wekyb3d8bbwe!App")]
    [InlineData(null, @"C:\apps\notepad.exe", "notepad", @"path:C:\apps\notepad.exe")]
    [InlineData(null, "", "notepad", "process:notepad")]
    [InlineData(null, "", "", "pid:4321")]
    public void ApplicationKey_prefers_the_most_specific_identifier_available(
        string? appUserModelId, string processPath, string processName, string expected)
    {
        var record = Record(processPath, processName, appUserModelId);
        Assert.Equal(expected, record.ApplicationKey);
    }

    [Theory]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("msedge", "Microsoft Edge")]
    [InlineData("explorer", "File Explorer")]
    [InlineData("code", "Visual Studio Code")]
    [InlineData("Taskmgr", "Task Manager")]
    public void Known_executables_get_their_real_product_name(string executable, string expected)
    {
        Assert.Equal(expected, Record($@"C:\apps\{executable}.exe").ApplicationName);
    }

    [Fact]
    public void An_unknown_executable_is_capitalised_rather_than_left_bare()
    {
        Assert.Equal("Slack", Record(@"C:\apps\slack.exe").ApplicationName);
    }

    [Fact]
    public void ApplicationFrameHost_falls_back_to_the_window_title()
    {
        // The host process name would label every packaged app "Applicationframehost", so the title
        // is the only thing that tells Mail from Photos.
        var record = Record(@"C:\Windows\ApplicationFrameHost.exe", title: "Mail");
        Assert.Equal("Mail", record.ApplicationName);
    }

    [Fact]
    public void A_window_with_no_title_falls_back_to_its_class_name()
    {
        var record = Record(@"C:\Windows\ApplicationFrameHost.exe", className: "Windows.UI.Core.CoreWindow");
        Assert.Equal("Windows.UI.Core.CoreWindow", record.ApplicationName);
    }

    [Fact]
    public void A_window_that_reports_nothing_still_gets_a_readable_label()
    {
        Assert.Equal("Unknown app", Record().ApplicationName);
    }

    [Fact]
    public void A_packaged_app_with_no_executable_is_labelled_by_its_model_id()
    {
        var record = Record(appUserModelId: "Microsoft.Mail_8wekyb3d8bbwe!App");
        Assert.Equal("Microsoft.Mail_8wekyb3d8bbwe!App", record.ApplicationName);
    }
}
