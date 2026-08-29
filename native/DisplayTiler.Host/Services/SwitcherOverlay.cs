using System.Drawing.Drawing2D;
using System.Globalization;
using DisplayTiler.Core;
using DisplayTiler.Host.Interop;

namespace DisplayTiler.Host.Services;

internal enum SwitcherLayoutMode { PackedGrid, CategoryRows }

internal sealed class SwitcherOverlay : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopMost = 0x00000008;
    private const uint DwmwaUseImmersiveDarkMode = 20;
    private const uint DwmwaWindowCornerPreference = 33;
    private const uint DwmwaBorderColor = 34;
    /// <summary>One dial for the whole switcher. 1.0 is the original size.</summary>
    private const double UiScale = 0.75;
    /// <summary>
    /// Text shrinks only about a quarter as much as the chrome does, and is derived from UiScale so
    /// that the two stay in proportion when the dial moves. Scaling type one-for-one with the panel
    /// puts window titles near six points at half size, below where Segoe UI stays readable on a
    /// translucent backdrop; leaving it fixed makes the text look wrong the moment UiScale changes.
    /// </summary>
    private const float TextScale = (float)(UiScale + (1 - UiScale) * 0.24);
    /// <summary>Smallest point size any label is allowed to reach, whatever the scale says.</summary>
    private const float MinimumPointSize = 7f;

    private const int OuterPadding = (int)(42 * UiScale);
    // Scaled by TextScale rather than UiScale: this band holds two lines of type, and at half
    // size the second line ("3 windows") was being overlapped by the top of the cards below it.
    private const int GroupHeaderHeight = (int)(54 * TextScale);
    private const int BaseCardWidth = (int)(438 * UiScale);
    private const int CardHeight = (int)(305 * UiScale);
    private const int CardTitleHeight = (int)(52 * UiScale);
    private const int CardGap = (int)(23 * UiScale);
    private const int GroupGap = (int)(38 * UiScale);
    private const int ScrollStep = (int)(196 * UiScale);
    private const int CardCornerRadius = (int)(18 * UiScale);
    private const int PreviewCornerRadius = (int)(12 * UiScale);
    private const int HeaderIconSize = (int)(30 * UiScale);
    private const int CloseButtonSize = (int)(30 * UiScale);
    private const int CloseGlyphInset = (int)(8 * UiScale);
    private const int ScreenMargin = (int)(64 * UiScale);
    private const int MinimumPanelHeight = (int)(360 * UiScale);

    /// <summary>
    /// Column bounds for the panel. Half-width cards fit roughly twice as many across, and the
    /// packed layout seats whole groups side by side on a shelf, so a higher ceiling makes the panel
    /// genuinely wider rather than merely denser. The floor keeps it a wide, short panel instead of
    /// collapsing into a tall narrow column when every open app happens to have a single window.
    /// </summary>
    private const int MinColumns = 5;
    private const int MaxColumns = 8;

    private readonly Dictionary<nint, nint> _thumbnails = [];
    private readonly Dictionary<string, Icon?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Color> _accents = new(StringComparer.OrdinalIgnoreCase);
    // Built once. OnPaint runs on every selection move, hover and scroll, and constructing a Font
    // means a GDI font realisation each time - wasted work in the one place that has to feel instant.
    private readonly Font _groupFont;
    private readonly Font _countFont;
    private readonly Font _titleFont;
    // Timers polling for a window to actually close after we asked it to. Tracked so that shutting
    // down mid-poll does not leave one running against a disposed form.
    private readonly List<System.Windows.Forms.Timer> _closeWatchers = [];
    private readonly List<WindowLayout> _layouts = [];
    private readonly List<GroupHeaderLayout> _groupHeaders = [];
    private readonly List<WindowRecord> _windows = [];
    private Bitmap? _blurredBackdrop;
    private IReadOnlyList<ApplicationGroup> _groups = [];
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private int _hoveredCloseIndex = -1;
    private int _scrollOffset;
    private int _contentHeight;
    private int _contentWidth;
    private Func<WindowRecord, bool>? _closeWindowRequested;
    private Rectangle _workingArea;

    public SwitcherLayoutMode LayoutMode { get; private set; } = SwitcherLayoutMode.PackedGrid;

    public WindowRecord? SelectedWindow => _selectedIndex >= 0 && _selectedIndex < _windows.Count ? _windows[_selectedIndex] : null;
    public event EventHandler<WindowRecord>? WindowActivated;
    /// <summary>Raised whenever the overlay actually appears or disappears.</summary>
    /// <remarks>
    /// The keyboard hook decides whether to swallow a keystroke from this and nothing else. A flag
    /// saying a switch was requested can outlive a UI thread that never honoured the request; a
    /// window that is genuinely on screen cannot.
    /// </remarks>
    public event Action<bool>? VisibleStateChanged;
    public event Action<SwitcherLayoutMode>? LayoutModeChanged;

    public SwitcherOverlay()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.Black;
        ClientSize = new Size((int)(1040 * UiScale), (int)(680 * UiScale));
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "DisplayTiler Grouped Switcher";
        TopMost = true;
        _groupFont = new Font("Segoe UI Variable Text Semibold", PointSize(15f), FontStyle.Regular, GraphicsUnit.Point);
        _countFont = new Font("Segoe UI Variable Text", PointSize(10.5f), FontStyle.Regular, GraphicsUnit.Point);
        _titleFont = new Font("Segoe UI Variable Text Semibold", PointSize(12.5f), FontStyle.Regular, GraphicsUnit.Point);
        _ = Handle;
    }

    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= WsExToolWindow | WsExTopMost; return parameters; }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var enabled = 1;
        var rounded = 2;
        var borderColor = ColorTranslator.ToWin32(Color.FromArgb(205, 210, 224));
        NativeMethods.DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref borderColor, sizeof(int));
        var margins = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(Handle, ref margins);
    }

    public void ShowGroups(IReadOnlyList<ApplicationGroup> groups, int initialDirection)
    {
        _workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        _groups = groups.Where(group => group.Windows.Count > 0).ToArray();
        RebuildWindowList();
        _selectedIndex = initialDirection < 0 ? Math.Max(0, _windows.Count - 1) : _windows.Count > 1 ? 1 : 0;
        _scrollOffset = 0;
        ApplyLayoutAndSize();
        EnsureSelectionVisible();
        CaptureBlurredBackdrop();
        Show();
        Invalidate();
        SyncThumbnails();
    }

    public void MoveSelection(int delta)
    {
        if (_windows.Count == 0) return;
        _selectedIndex = (_selectedIndex + delta + _windows.Count) % _windows.Count;
        EnsureSelectionVisible();
        Invalidate();
        SyncThumbnails();
    }

    public void SetCloseWindowHandler(Func<WindowRecord, bool> handler) => _closeWindowRequested = handler;

    public void SetLayoutMode(SwitcherLayoutMode mode)
    {
        if (LayoutMode == mode) return;
        LayoutMode = mode;
        LayoutModeChanged?.Invoke(mode);
        if (_groups.Count == 0) return;
        ApplyLayoutAndSize();
        EnsureSelectionVisible();
        Invalidate();
        SyncThumbnails();
    }

    public void CloseSelected()
    {
        var window = SelectedWindow;
        if (window is null) return;
        RequestClose(window);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_groups.Count == 0) return;
        RebuildLayout();
        EnsureSelectionVisible();
        SyncThumbnails();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            ClearThumbnails();
            // A full-panel 32bpp bitmap, several megabytes at this size. The switcher spends almost
            // all of its life hidden and recaptures on every open anyway, so holding it would be
            // pure idle cost in a process that stays resident for days.
            _blurredBackdrop?.Dispose();
            _blurredBackdrop = null;
        }
        VisibleStateChanged?.Invoke(Visible);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_blurredBackdrop is not null)
            e.Graphics.DrawImageUnscaled(_blurredBackdrop, Point.Empty);
        else
            e.Graphics.Clear(Color.FromArgb(28, 27, 38));

        using var tint = new SolidBrush(Color.FromArgb(52, 20, 20, 22));
        e.Graphics.FillRectangle(tint, ClientRectangle);
        using var frost = new SolidBrush(Color.FromArgb(20, 238, 240, 244));
        e.Graphics.FillRectangle(frost, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Grayscale antialiasing, and deliberately not AntiAliasGridFit. Grid fitting rounds every
        // glyph advance to a whole pixel and snaps every stem to the pixel grid; at these point
        // sizes that reads as uneven letter spacing, and it makes one card title look bolder than
        // the next despite both using the same font, because whether a stem lands on one pixel or
        // straddles two depends on its subpixel position. ClearType is not the answer either: it
        // assumes a fixed opaque background and fringes the glyphs with colour over this
        // translucent, blurred backdrop.
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        foreach (var header in _groupHeaders)
        {
            var group = _groups[header.GroupIndex];
            var headerY = header.Bounds.Top - _scrollOffset;
            if (headerY + GroupHeaderHeight >= 0 && headerY <= ClientSize.Height)
            {
                var textLeft = header.Bounds.Left + HeaderIconSize + (int)(12 * UiScale);
                var textWidth = header.Bounds.Width - (textLeft - header.Bounds.Left);
                // Stacked from real font metrics rather than scaled magic numbers, so the second
                // line always has room for its descenders whatever UiScale is set to.
                var nameHeight = _groupFont.Height;
                DrawAppIcon(e.Graphics, group.Windows[0], new Rectangle(header.Bounds.Left, headerY + (int)(7 * UiScale), HeaderIconSize, HeaderIconSize));
                DrawLabel(e.Graphics, group.Name, _groupFont, Color.FromArgb(245, 244, 250),
                    new Rectangle(textLeft, headerY, textWidth, nameHeight));
                var count = $"{group.Windows.Count} window{(group.Windows.Count == 1 ? string.Empty : "s")}";
                DrawLabel(e.Graphics, count, _countFont, Color.FromArgb(190, 187, 204),
                    new Rectangle(textLeft, headerY + nameHeight, textWidth, _countFont.Height));
            }
        }

        foreach (var layout in _layouts)
        {
            var displayedCard = Offset(layout.CardBounds, -_scrollOffset);
            if (displayedCard.Bottom < 0 || displayedCard.Top > ClientSize.Height) continue;
            DrawCard(e.Graphics, layout);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollBy(e.Delta > 0 ? -ScrollStep : ScrollStep);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var logical = new Point(e.X, e.Y + _scrollOffset);
        var hovered = _layouts.FirstOrDefault(layout => layout.CardBounds.Contains(logical));
        var hoveredIndex = hovered?.FlatIndex ?? -1;
        var closeIndex = hovered is not null && hovered.CloseBounds.Contains(logical) ? hovered.FlatIndex : -1;
        if (hoveredIndex == _hoveredIndex && closeIndex == _hoveredCloseIndex) return;
        _hoveredIndex = hoveredIndex;
        _hoveredCloseIndex = closeIndex;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredIndex = -1;
        _hoveredCloseIndex = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var logical = new Point(e.X, e.Y + _scrollOffset);
        var layout = _layouts.FirstOrDefault(item => item.CardBounds.Contains(logical));
        if (layout is null) return;
        _selectedIndex = layout.FlatIndex;
        if (layout.CloseBounds.Contains(logical))
        {
            RequestClose(layout.Window);
            return;
        }
        WindowActivated?.Invoke(this, layout.Window);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearThumbnails();
            _blurredBackdrop?.Dispose();
            _blurredBackdrop = null;
            foreach (var timer in _closeWatchers) { timer.Stop(); timer.Dispose(); }
            _closeWatchers.Clear();
            _groupFont.Dispose();
            _countFont.Dispose();
            _titleFont.Dispose();
            foreach (var icon in _icons.Values) icon?.Dispose();
            _icons.Clear();
            _accents.Clear();
        }
        base.Dispose(disposing);
    }

    private void DrawCard(Graphics graphics, WindowLayout layout)
    {
        var selected = layout.FlatIndex == _selectedIndex;
        var hovered = layout.FlatIndex == _hoveredIndex;
        var cardBounds = Offset(layout.CardBounds, -_scrollOffset);
        var previewBounds = Offset(layout.PreviewBounds, -_scrollOffset);
        var closeBounds = Offset(layout.CloseBounds, -_scrollOffset);
        // Every card carries its application's own colour, sampled from that app's icon: File
        // Explorer reads yellow, Claude orange. The fill stays mostly dark so the title on top keeps
        // its contrast and the panel behind still shows through - the colour is carried mainly by
        // the border, which is what actually reads as "this card belongs to that app".
        var accent = AccentFor(layout.Window);

        using var cardPath = RoundedRectangle(cardBounds, CardCornerRadius);
        using var cardFill = new SolidBrush(Tint(
            selected
                ? Color.FromArgb(190, 52, 54, 60)
                : hovered
                    ? Color.FromArgb(176, 43, 44, 48)
                    : Color.FromArgb(160, 31, 32, 35),
            accent,
            selected ? 0.30f : hovered ? 0.24f : 0.17f));
        using var cardBorder = new Pen(Tint(
            selected
                ? Color.FromArgb(235, 224, 228, 238)
                : hovered
                    ? Color.FromArgb(145, 215, 215, 226)
                    : Color.FromArgb(92, 221, 221, 232),
            accent,
            selected ? 0.80f : hovered ? 0.70f : 0.62f), selected ? 1.6f : 1f);
        graphics.FillPath(cardFill, cardPath);
        graphics.DrawPath(cardBorder, cardPath);

        using var previewPath = RoundedRectangle(previewBounds, PreviewCornerRadius);
        using var previewFill = new SolidBrush(Tint(
            selected
                ? Color.FromArgb(155, 47, 49, 55)
                : Color.FromArgb(145, 27, 28, 31),
            accent,
            0.12f));
        graphics.FillPath(previewFill, previewPath);

        var titleBounds = new Rectangle(
            cardBounds.Left + (int)(17 * UiScale),
            cardBounds.Top + (int)(7 * UiScale),
            cardBounds.Width - (int)(72 * UiScale),
            CardTitleHeight - (int)(13 * UiScale));
        DrawLabel(graphics, NormalizeWindowTitle(layout.Window.Title), _titleFont, Color.FromArgb(247, 246, 251), titleBounds, verticallyCentered: true);

        var closeHovered = layout.FlatIndex == _hoveredCloseIndex;
        using var closeFill = new SolidBrush(closeHovered ? Color.FromArgb(194, 71, 82) : Color.FromArgb(57, 54, 70));
        graphics.FillEllipse(closeFill, closeBounds);
        using var closePen = new Pen(Color.FromArgb(235, 233, 242), 1.1f);
        graphics.DrawLine(closePen, closeBounds.Left + CloseGlyphInset, closeBounds.Top + CloseGlyphInset, closeBounds.Right - CloseGlyphInset, closeBounds.Bottom - CloseGlyphInset);
        graphics.DrawLine(closePen, closeBounds.Right - CloseGlyphInset, closeBounds.Top + CloseGlyphInset, closeBounds.Left + CloseGlyphInset, closeBounds.Bottom - CloseGlyphInset);
    }

    /// <summary>Draws one line of UI text, trimmed with an ellipsis when it does not fit.</summary>
    /// <remarks>
    /// Stays on GDI+ DrawString rather than TextRenderer. TextRenderer renders through GDI, which
    /// ignores the Graphics text-rendering hint and uses the system setting - ClearType on this
    /// machine - and ClearType visibly fringes these labels with colour against the blurred
    /// backdrop. The even spacing comes from the AntiAlias hint set in OnPaint, not from the
    /// drawing API.
    /// </remarks>
    private static void DrawLabel(
        Graphics graphics,
        string text,
        Font font,
        Color color,
        Rectangle bounds,
        bool verticallyCentered = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var brush = new SolidBrush(color);
        graphics.DrawString(text, font, brush, (RectangleF)bounds, verticallyCentered ? CenteredLabelFormat : TopLabelFormat);
    }

    private static readonly StringFormat TopLabelFormat = CreateLabelFormat(StringAlignment.Near);
    private static readonly StringFormat CenteredLabelFormat = CreateLabelFormat(StringAlignment.Center);

    private static StringFormat CreateLabelFormat(StringAlignment lineAlignment)
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags = StringFormatFlags.NoWrap;
        format.Trimming = StringTrimming.EllipsisCharacter;
        format.LineAlignment = lineAlignment;
        return format;
    }

    private static string NormalizeWindowTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;

        var index = 0;
        var removedSymbol = false;
        while (index < title.Length)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(title, index);
            var elementLength = char.IsHighSurrogate(title[index]) && index + 1 < title.Length && char.IsLowSurrogate(title[index + 1]) ? 2 : 1;
            var isDecorativeSymbol = category is UnicodeCategory.OtherSymbol
                or UnicodeCategory.MathSymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format;

            if (isDecorativeSymbol)
            {
                removedSymbol = true;
                index += elementLength;
                continue;
            }

            if (removedSymbol && char.IsWhiteSpace(title, index))
            {
                index += elementLength;
                continue;
            }

            break;
        }

        var normalized = title[index..].TrimStart();
        return normalized.Length == 0 ? title : normalized;
    }

    private void RebuildWindowList()
    {
        _windows.Clear();
        foreach (var group in _groups) _windows.AddRange(group.Windows);
    }

    private void ApplyLayoutAndSize()
    {
        // Width follows the total number of windows rather than the largest single group. Sizing
        // to the largest group made the panel as narrow as whichever app happened to have the most
        // windows open, which with small cards is almost always one or two.
        var totalWindows = _groups.Sum(group => group.Windows.Count);
        var desiredColumns = Math.Clamp((int)Math.Ceiling(Math.Sqrt(Math.Max(1, totalWindows)) * 1.6), MinColumns, MaxColumns);
        var maximumWidth = Math.Min(_workingArea.Width - ScreenMargin, OuterPadding * 2 + desiredColumns * BaseCardWidth + (desiredColumns - 1) * CardGap);
        var maximumHeight = Math.Min((int)Math.Round(_workingArea.Height * 0.90), _workingArea.Height - 40);

        Bounds = new Rectangle(_workingArea.Left + (_workingArea.Width - maximumWidth) / 2, _workingArea.Top + 20, maximumWidth, maximumHeight);
        RebuildLayout();

        // The column count above is only an upper bound. Groups are seated on shelves and a shelf
        // that ends early leaves an empty strip down the right-hand side of the panel, so shrink to
        // the width the layout actually used. Shrinking only, so this cannot oscillate.
        var width = Math.Clamp(_contentWidth, OuterPadding * 2 + BaseCardWidth, maximumWidth);
        if (width != maximumWidth)
        {
            Bounds = new Rectangle(_workingArea.Left + (_workingArea.Width - width) / 2, _workingArea.Top + 20, width, maximumHeight);
            RebuildLayout();
        }

        var height = Math.Min(maximumHeight, Math.Max(MinimumPanelHeight, _contentHeight));
        Bounds = new Rectangle(_workingArea.Left + (_workingArea.Width - width) / 2, _workingArea.Top + (_workingArea.Height - height) / 2, width, height);
        RebuildLayout();
    }

    private void RebuildLayout()
    {
        _layouts.Clear();
        _groupHeaders.Clear();
        if (_groups.Count == 0 || ClientSize.Width <= OuterPadding * 2) { _contentHeight = 0; return; }
        var availableWidth = ClientSize.Width - OuterPadding * 2;
        var columns = Math.Clamp((availableWidth + CardGap) / (BaseCardWidth + CardGap), 1, MaxColumns);
        var cardWidth = (availableWidth - CardGap * (columns - 1)) / columns;
        if (LayoutMode == SwitcherLayoutMode.PackedGrid) RebuildPackedLayout(columns, cardWidth);
        else RebuildCategoryRowsLayout(columns, cardWidth);
        _contentWidth = _layouts.Count == 0 ? 0 : _layouts.Max(layout => layout.CardBounds.Right) + OuterPadding;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScrollOffset);
    }

    private void RebuildCategoryRowsLayout(int columns, int cardWidth)
    {
        var y = OuterPadding;
        var flatIndex = 0;
        for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
        {
            var windows = _groups[groupIndex].Windows;
            _groupHeaders.Add(new GroupHeaderLayout(groupIndex, new Rectangle(OuterPadding, y, ClientSize.Width - OuterPadding * 2, GroupHeaderHeight)));
            var cardTop = y + GroupHeaderHeight;
            for (var windowIndex = 0; windowIndex < windows.Count; windowIndex++)
            {
                var row = windowIndex / columns;
                var column = windowIndex % columns;
                AddWindowLayout(groupIndex, flatIndex++, windows[windowIndex], new Rectangle(OuterPadding + column * (cardWidth + CardGap), cardTop + row * (CardHeight + CardGap), cardWidth, CardHeight));
            }
            var rows = (int)Math.Ceiling(windows.Count / (double)columns);
            y = cardTop + rows * CardHeight + Math.Max(0, rows - 1) * CardGap + GroupGap;
        }
        _contentHeight = Math.Max(0, y - GroupGap + OuterPadding);
    }

    private void RebuildPackedLayout(int columns, int cardWidth)
    {
        var y = OuterPadding;
        var shelfColumn = 0;
        var shelfHeight = 0;
        var flatIndex = 0;
        for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
        {
            var windows = _groups[groupIndex].Windows;
            var span = Math.Min(columns, windows.Count);
            var rows = (int)Math.Ceiling(windows.Count / (double)span);
            var blockHeight = GroupHeaderHeight + rows * CardHeight + Math.Max(0, rows - 1) * CardGap;
            if (shelfColumn > 0 && shelfColumn + span > columns)
            {
                y += shelfHeight + GroupGap;
                shelfColumn = 0;
                shelfHeight = 0;
            }
            var blockLeft = OuterPadding + shelfColumn * (cardWidth + CardGap);
            var blockWidth = span * cardWidth + Math.Max(0, span - 1) * CardGap;
            _groupHeaders.Add(new GroupHeaderLayout(groupIndex, new Rectangle(blockLeft, y, blockWidth, GroupHeaderHeight)));
            var cardTop = y + GroupHeaderHeight;
            for (var windowIndex = 0; windowIndex < windows.Count; windowIndex++)
            {
                var row = windowIndex / span;
                var column = windowIndex % span;
                AddWindowLayout(groupIndex, flatIndex++, windows[windowIndex], new Rectangle(blockLeft + column * (cardWidth + CardGap), cardTop + row * (CardHeight + CardGap), cardWidth, CardHeight));
            }
            shelfColumn += span;
            shelfHeight = Math.Max(shelfHeight, blockHeight);
            if (shelfColumn == columns)
            {
                y += shelfHeight + GroupGap;
                shelfColumn = 0;
                shelfHeight = 0;
            }
        }
        if (shelfColumn > 0) y += shelfHeight;
        else y -= GroupGap;
        _contentHeight = Math.Max(0, y + OuterPadding);
    }

    private void AddWindowLayout(int groupIndex, int flatIndex, WindowRecord window, Rectangle card)
    {
        var previewInset = (int)(10 * UiScale);
        var preview = new Rectangle(card.Left + previewInset, card.Top + CardTitleHeight, card.Width - previewInset * 2, card.Height - CardTitleHeight - previewInset);
        var close = new Rectangle(card.Right - CloseButtonSize - (int)(12 * UiScale), card.Top + (int)(11 * UiScale), CloseButtonSize, CloseButtonSize);
        _layouts.Add(new WindowLayout(groupIndex, flatIndex, window, card, preview, close));
    }

    private void EnsureSelectionVisible()
    {
        var layout = _layouts.FirstOrDefault(item => item.FlatIndex == _selectedIndex);
        if (layout is null) return;
        var groupTop = _groupHeaders.First(header => header.GroupIndex == layout.GroupIndex).Bounds.Top;
        if (groupTop < _scrollOffset) _scrollOffset = groupTop;
        else if (layout.CardBounds.Bottom > _scrollOffset + ClientSize.Height) _scrollOffset = layout.CardBounds.Bottom - ClientSize.Height + OuterPadding;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScrollOffset);
    }

    private void ScrollBy(int delta)
    {
        var next = Math.Clamp(_scrollOffset + delta, 0, MaxScrollOffset);
        if (next == _scrollOffset) return;
        _scrollOffset = next;
        Invalidate();
        SyncThumbnails();
    }

    private int MaxScrollOffset => Math.Max(0, _contentHeight - ClientSize.Height);

    private void RemoveWindow(nint handle)
    {
        if (_thumbnails.Remove(handle, out var thumbnail)) NativeMethods.DwmUnregisterThumbnail(thumbnail);
        _groups = _groups.Select(group =>
        {
            var windows = group.Windows.Where(window => window.Handle != handle).ToArray();
            return new ApplicationGroup(group.Key, group.Name, windows, group.LastActivatedUnixMilliseconds);
        }).Where(group => group.Windows.Count > 0).ToArray();
        RebuildWindowList();
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _windows.Count - 1));
        ApplyLayoutAndSize();
        EnsureSelectionVisible();
        Invalidate();
        SyncThumbnails();
        if (_windows.Count == 0) Hide();
    }

    private void RequestClose(WindowRecord window)
    {
        if (_closeWindowRequested?.Invoke(window) != true) return;
        var attempts = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        _closeWatchers.Add(timer);
        timer.Tick += (_, _) =>
        {
            attempts++;
            var closed = !NativeMethods.IsWindow(window.Handle);
            if (!closed && attempts < 10) return;
            timer.Stop();
            _closeWatchers.Remove(timer);
            timer.Dispose();
            if (closed) RemoveWindow(window.Handle);
        };
        timer.Start();
    }

    private void SyncThumbnails()
    {
        if (!Visible || !IsHandleCreated) return;
        var activeHandles = _layouts.Select(layout => layout.Window.Handle).ToHashSet();
        foreach (var staleHandle in _thumbnails.Keys.Where(handle => !activeHandles.Contains(handle)).ToArray())
        {
            NativeMethods.DwmUnregisterThumbnail(_thumbnails[staleHandle]);
            _thumbnails.Remove(staleHandle);
        }

        foreach (var layout in _layouts)
        {
            if (!_thumbnails.TryGetValue(layout.Window.Handle, out var thumbnail))
            {
                if (NativeMethods.DwmRegisterThumbnail(Handle, layout.Window.Handle, out thumbnail) != 0) continue;
                _thumbnails[layout.Window.Handle] = thumbnail;
            }
            var destination = new Rectangle(layout.PreviewBounds.X, layout.PreviewBounds.Y - _scrollOffset, layout.PreviewBounds.Width, layout.PreviewBounds.Height);
            var fullyVisible = destination.Top >= 0 && destination.Bottom <= ClientSize.Height;
            var flags = 0x01u | 0x04u | 0x08u | 0x10u;
            var source = new NativeMethods.Rect();
            if (fullyVisible && NativeMethods.DwmQueryThumbnailSourceSize(thumbnail, out var sourceSize) == 0)
            {
                source = CenterCrop(sourceSize.Width, sourceSize.Height, destination.Width, destination.Height);
                flags |= 0x02;
            }
            var properties = new NativeMethods.DwmThumbnailProperties
            {
                Flags = flags,
                Destination = new NativeMethods.Rect { Left = destination.Left, Top = destination.Top, Right = destination.Right, Bottom = destination.Bottom },
                Source = source,
                Opacity = 255,
                Visible = fullyVisible,
                SourceClientAreaOnly = false,
            };
            NativeMethods.DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }
    }

    private void ClearThumbnails()
    {
        foreach (var thumbnail in _thumbnails.Values) NativeMethods.DwmUnregisterThumbnail(thumbnail);
        _thumbnails.Clear();
    }

    private void CaptureBlurredBackdrop()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        // Wait until DWM has removed the previous hidden switcher frame, otherwise a
        // rapid reopen can capture its old labels into the blur and make text look doubled.
        NativeMethods.DwmFlush();

        var blurred = new Bitmap(ClientSize.Width, ClientSize.Height);
        try
        {
            using var capture = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (var captureGraphics = Graphics.FromImage(capture))
                captureGraphics.CopyFromScreen(Bounds.Location, Point.Empty, ClientSize);

            var blurWidth = Math.Max(1, ClientSize.Width / 10);
            var blurHeight = Math.Max(1, ClientSize.Height / 10);
            using var reduced = new Bitmap(blurWidth, blurHeight);
            using (var reducedGraphics = Graphics.FromImage(reduced))
            {
                reducedGraphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                reducedGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                reducedGraphics.DrawImage(capture, new Rectangle(0, 0, blurWidth, blurHeight));
            }

            using var blurredGraphics = Graphics.FromImage(blurred);
            blurredGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            blurredGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            blurredGraphics.DrawImage(reduced, ClientRectangle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            // A GDI screen grab can fail outright - most often on a monitor composed by a second
            // adapter. Losing the frost is a cosmetic downgrade; letting this escape would take the
            // process down from inside a posted callback, so fall back to the flat panel colour.
            blurred.Dispose();
            _blurredBackdrop?.Dispose();
            _blurredBackdrop = null;
            return;
        }

        _blurredBackdrop?.Dispose();
        _blurredBackdrop = blurred;
    }

    private void DrawAppIcon(Graphics graphics, WindowRecord window, Rectangle bounds)
    {
        var icon = IconFor(window.ApplicationIconPath);
        if (icon is not null) graphics.DrawIcon(icon, bounds);
        else { using var fallback = new SolidBrush(AccentFor(window)); graphics.FillEllipse(fallback, bounds); }
    }

    private Icon? IconFor(string key)
    {
        if (_icons.TryGetValue(key, out var cached)) return cached;
        Icon? icon;
        try { icon = string.IsNullOrWhiteSpace(key) ? null : Icon.ExtractAssociatedIcon(key); } catch { icon = null; }
        _icons[key] = icon;
        return icon;
    }

    /// <summary>The application's own colour, sampled once from its icon and cached by path.</summary>
    private Color AccentFor(WindowRecord window)
    {
        var key = window.ApplicationIconPath;
        if (_accents.TryGetValue(key, out var cached)) return cached;
        var accent = SampleAccent(IconFor(key)) ?? NeutralAccent;
        _accents[key] = accent;
        return accent;
    }

    /// <summary>Picks the colour a person would name if asked what colour an icon is.</summary>
    /// <remarks>
    /// Averaging a whole icon returns mud, and its single most common colour is usually the neutral
    /// the glyph sits on rather than the colour anyone would name. So transparent, near-black,
    /// near-white and nearly grey pixels are all discarded, what survives is bucketed by hue and
    /// weighted by how colourful it is, and the winning bucket's mean becomes the accent. Icons with
    /// no colourful pixels at all - plenty of terminal and utility icons - return null so the caller
    /// keeps the neutral card rather than tinting it with noise.
    /// </remarks>
    private static Color? SampleAccent(Icon? icon)
    {
        if (icon is null) return null;
        using var bitmap = icon.ToBitmap();

        const int buckets = 24;
        var weights = new double[buckets];
        var reds = new double[buckets];
        var greens = new double[buckets];
        var blues = new double[buckets];

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 128) continue;
                int max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                int min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                if (max < 40 || min > 225) continue;
                var saturation = (max - min) / (double)max;
                if (saturation < 0.25) continue;

                var bucket = Math.Clamp((int)(pixel.GetHue() / 360.0 * buckets), 0, buckets - 1);
                var weight = saturation * (pixel.A / 255.0);
                weights[bucket] += weight;
                reds[bucket] += pixel.R * weight;
                greens[bucket] += pixel.G * weight;
                blues[bucket] += pixel.B * weight;
            }
        }

        var best = 0;
        for (var i = 1; i < buckets; i++) if (weights[i] > weights[best]) best = i;
        if (weights[best] <= 0) return null;

        return Brighten(Color.FromArgb(
            (int)Math.Round(reds[best] / weights[best]),
            (int)Math.Round(greens[best] / weights[best]),
            (int)Math.Round(blues[best] / weights[best])));
    }

    /// <summary>
    /// Lifts a sampled colour to full brightness, so a dark navy icon and a bright yellow one tint
    /// their cards with the same strength and only the hue differs.
    /// </summary>
    private static Color Brighten(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        if (max == 0) return color;
        var scale = 255.0 / max;
        return Color.FromArgb(
            (int)Math.Round(Math.Min(255, color.R * scale)),
            (int)Math.Round(Math.Min(255, color.G * scale)),
            (int)Math.Round(Math.Min(255, color.B * scale)));
    }

    /// <summary>Blends an accent into a base colour, keeping the base's alpha so cards stay glassy.</summary>
    private static Color Tint(Color baseColor, Color accent, float amount) => Color.FromArgb(
        baseColor.A,
        (int)Math.Round(baseColor.R + (accent.R - baseColor.R) * amount),
        (int)Math.Round(baseColor.G + (accent.G - baseColor.G) * amount),
        (int)Math.Round(baseColor.B + (accent.B - baseColor.B) * amount));

    private static NativeMethods.Rect CenterCrop(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            return new NativeMethods.Rect { Right = Math.Max(1, sourceWidth), Bottom = Math.Max(1, sourceHeight) };
        var sourceAspect = sourceWidth / (double)sourceHeight;
        var targetAspect = targetWidth / (double)targetHeight;
        if (sourceAspect > targetAspect)
        {
            var cropWidth = Math.Max(1, (int)Math.Round(sourceHeight * targetAspect));
            var left = (sourceWidth - cropWidth) / 2;
            return new NativeMethods.Rect { Left = left, Top = 0, Right = left + cropWidth, Bottom = sourceHeight };
        }
        var cropHeight = Math.Max(1, (int)Math.Round(sourceWidth / targetAspect));
        var top = (sourceHeight - cropHeight) / 2;
        return new NativeMethods.Rect { Left = 0, Top = top, Right = sourceWidth, Bottom = top + cropHeight };
    }

    private static float PointSize(float unscaled) => Math.Max(MinimumPointSize, unscaled * TextScale);

    /// <summary>Used for applications whose icon has no colour worth naming.</summary>
    private static readonly Color NeutralAccent = Color.FromArgb(126, 93, 245);

    private static Rectangle Offset(Rectangle bounds, int yOffset) => new(bounds.X, bounds.Y + yOffset, bounds.Width, bounds.Height);

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed record WindowLayout(int GroupIndex, int FlatIndex, WindowRecord Window, Rectangle CardBounds, Rectangle PreviewBounds, Rectangle CloseBounds);
    private sealed record GroupHeaderLayout(int GroupIndex, Rectangle Bounds);
}
