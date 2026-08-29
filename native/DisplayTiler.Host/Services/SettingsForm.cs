namespace DisplayTiler.Host.Services;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _enableAltTab = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly ComboBox _altTabBehavior = new();
    private readonly ComboBox _layout = new();
    /// Fonts created here are not owned by the controls they are assigned to, so the dialog has to
    /// release them itself; otherwise every open leaks a GDI handle.
    private readonly List<Font> _ownedFonts = [];

    public bool AltTabReplacementEnabled => _enableAltTab.Checked;
    public bool ActivateOnAltRelease => _altTabBehavior.SelectedIndex == 0;
    public bool StartWithWindows => _startWithWindows.Checked;
    public SwitcherLayoutMode LayoutMode => _layout.SelectedIndex == 1
        ? SwitcherLayoutMode.CategoryRows
        : SwitcherLayoutMode.PackedGrid;

    public SettingsForm(AppSettings settings)
    {
        Text = "DisplayTiler Settings";
        ClientSize = new Size(480, 396);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(31, 31, 38);
        ForeColor = Color.White;
        Font = Own(new Font("Segoe UI Variable Text", 10));

        var heading = new Label
        {
            AutoSize = true,
            Font = Own(new Font("Segoe UI Variable Display Semibold", 18)),
            Location = new Point(24, 20),
            Text = "DisplayTiler",
        };
        var description = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(185, 182, 198),
            Location = new Point(27, 58),
            Text = "The app has no main window. Closing settings returns it to the tray.",
        };

        _enableAltTab.AutoSize = true;
        _enableAltTab.Location = new Point(28, 101);
        _enableAltTab.Text = "Replace Windows Alt+Tab with DisplayTiler";
        _enableAltTab.Checked = settings.AltTabReplacementEnabled;

        _startWithWindows.AutoSize = true;
        _startWithWindows.Location = new Point(28, 137);
        _startWithWindows.Text = "Start DisplayTiler when I sign in to Windows";
        _startWithWindows.Checked = settings.StartWithWindows;

        var behaviorLabel = new Label
        {
            AutoSize = true,
            Location = new Point(28, 177),
            Text = "Alt+Tab behavior",
        };

        _altTabBehavior.DropDownStyle = ComboBoxStyle.DropDownList;
        _altTabBehavior.FlatStyle = FlatStyle.Flat;
        _altTabBehavior.Location = new Point(28, 201);
        _altTabBehavior.Size = new Size(417, 28);
        _altTabBehavior.Items.AddRange([
            "Hold Alt and release to switch (Windows default)",
            "Keep the switcher open until I choose or cancel",
        ]);
        _altTabBehavior.SelectedIndex = settings.ActivateOnAltRelease ? 0 : 1;

        var holdBehaviorDescription = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(165, 162, 180),
            Font = Own(new Font("Segoe UI Variable Text", 8.5f)),
            Location = new Point(31, 235),
            Text = "Sticky mode waits for a click, Enter, or Escape.",
        };

        var layoutLabel = new Label
        {
            AutoSize = true,
            Location = new Point(28, 275),
            Text = "Switcher layout",
        };
        _layout.DropDownStyle = ComboBoxStyle.DropDownList;
        _layout.FlatStyle = FlatStyle.Flat;
        _layout.Location = new Point(159, 271);
        _layout.Size = new Size(286, 28);
        _layout.Items.AddRange(["Packed grid", "Category rows"]);
        _layout.SelectedIndex = settings.LayoutMode == SwitcherLayoutMode.CategoryRows ? 1 : 0;

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(273, 338),
            Size = new Size(92, 34),
            Text = "Cancel",
        };
        var save = new Button
        {
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(374, 338),
            Size = new Size(82, 34),
            Text = "Save",
            BackColor = Color.FromArgb(112, 76, 205),
        };
        save.FlatAppearance.BorderColor = Color.FromArgb(151, 116, 244);
        cancel.FlatAppearance.BorderColor = Color.FromArgb(80, 78, 91);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.AddRange([heading, description, _enableAltTab, _startWithWindows, behaviorLabel, _altTabBehavior, holdBehaviorDescription, layoutLabel, _layout, cancel, save]);
    }

    private Font Own(Font font)
    {
        _ownedFonts.Add(font);
        return font;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        foreach (var font in _ownedFonts) font.Dispose();
        _ownedFonts.Clear();
    }
}
