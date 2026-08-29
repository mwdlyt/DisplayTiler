using DisplayTiler.Core;
using DisplayTiler.Host.Interop;

namespace DisplayTiler.Host.Services;

internal sealed class SwitcherController : IDisposable
{
    /// <summary>
    /// How long the hook may keep consuming keystrokes after asking for the overlay but before the
    /// overlay has confirmed it is on screen.
    /// </summary>
    /// <remarks>
    /// Past this deadline the hook fails open no matter what any flag says. If the UI thread is
    /// wedged the overlay will never confirm, and without a deadline the hook would go on swallowing
    /// Tab, the arrows, Escape and Enter for as long as the other application stayed stuck.
    /// </remarks>
    private const int OpeningGraceMilliseconds = 1500;

    /// <summary>
    /// How long a swallowed key-down stays willing to swallow its matching key-up.
    /// </summary>
    /// <remarks>
    /// Bounded on purpose. If a key-up never arrives - the usual cause is focus moving to another
    /// desktop or session between press and release - a permanent entry would eat the release of the
    /// *next* press of that key, whose key-down was passed through. That is precisely the stale-Tab
    /// failure this is meant to prevent, so an unclaimed entry expires instead.
    /// </remarks>
    private const int OrphanKeyUpGraceMilliseconds = 4000;

    private readonly WindowCatalog _catalog = new();
    private readonly SwitcherOverlay _overlay = new();
    private readonly KeyboardHook _hook;

    /// Non-modifier keys whose key-down was swallowed, and when that claim expires. Touched only on
    /// the hook thread, which is single-threaded, so it needs no lock.
    private readonly Dictionary<uint, long> _swallowedKeyDowns = [];

    private volatile bool _overlayVisible;
    private volatile bool _altTabSession;
    private volatile bool _isAltTabReplacementEnabled;
    private volatile bool _activateOnAltRelease = true;
    private long _openingDeadline;
    private nint _previousForegroundWindow;

    public bool IsAltTabReplacementEnabled { get => _isAltTabReplacementEnabled; set => _isAltTabReplacementEnabled = value; }
    public bool ActivateOnAltRelease { get => _activateOnAltRelease; set => _activateOnAltRelease = value; }
    public event Action<SwitcherLayoutMode>? LayoutModeChanged
    {
        add => _overlay.LayoutModeChanged += value;
        remove => _overlay.LayoutModeChanged -= value;
    }

    public SwitcherController()
    {
        _overlay.WindowActivated += (_, window) => DismissAndActivate(window);
        _overlay.SetCloseWindowHandler(window => NativeMethods.PostMessage(window.Handle, NativeMethods.WmClose, 0, 0));

        // The overlay's own visibility is the only trustworthy answer to "is the switcher up?".
        // A flag set when a switch was *requested* can outlive a UI thread that never got around to
        // honouring the request; a window that is really on screen cannot.
        _overlay.VisibleStateChanged += visible =>
        {
            _overlayVisible = visible;
            if (visible) return;
            _altTabSession = false;
            Volatile.Write(ref _openingDeadline, 0);
        };

        _hook = new KeyboardHook(ShouldConsume);
    }

    private bool IsSwitcherOnScreen => _overlayVisible || Environment.TickCount64 < Volatile.Read(ref _openingDeadline);

    /// <summary>Decides the fate of one keystroke. Runs on the hook thread; must not block.</summary>
    private bool ShouldConsume(nint wParam, NativeMethods.KbdLlHookStruct data)
    {
        var key = data.VkCode;
        var message = wParam.ToInt32();
        var isKeyDown = message == NativeMethods.WmSysKeyDown || message == NativeMethods.WmKeyDown;
        var isKeyUp = message == NativeMethods.WmSysKeyUp || message == NativeMethods.WmKeyUp;
        if (!isKeyDown && !isKeyUp) return false;

        // The release half of a press this hook already took. Take it too, so no application is left
        // holding a key-up for a key it was never told had gone down.
        if (isKeyUp && _swallowedKeyDowns.Remove(key, out var claimExpiry))
            return Environment.TickCount64 < claimExpiry;

        var altIsDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkMenu) & 0x8000) != 0;
        var controlIsDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
        var shiftIsDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkShift) & 0x8000) != 0;

        // Emergency fail-open. Settled entirely on this thread and never behind a post to the UI
        // thread, because the moment you reach for it is the moment the UI thread is what is stuck.
        if (isKeyDown && key == NativeMethods.VkF12 && controlIsDown && shiftIsDown)
        {
            IsAltTabReplacementEnabled = false;
            _altTabSession = false;
            Volatile.Write(ref _openingDeadline, 0);
            Post(() => Dismiss(false));
            return Swallow(key);
        }

        if (IsAltTabReplacementEnabled && isKeyDown && key == NativeMethods.VkTab && altIsDown)
        {
            var direction = shiftIsDown ? -1 : 1;
            if (IsSwitcherOnScreen)
            {
                Post(() => _overlay.MoveSelection(direction));
                return Swallow(key);
            }

            var activateOnAltRelease = ActivateOnAltRelease;
            _altTabSession = activateOnAltRelease;
            Volatile.Write(ref _openingDeadline, Environment.TickCount64 + OpeningGraceMilliseconds);
            if (Post(() => OpenSwitcher(activateOnAltRelease, direction))) return Swallow(key);

            // The UI thread is gone (shutting down). Give Alt+Tab back to Windows rather than
            // swallowing it into a switcher that will never appear.
            _altTabSession = false;
            Volatile.Write(ref _openingDeadline, 0);
            return false;
        }

        if (_overlayVisible && isKeyDown)
        {
            if (key == NativeMethods.VkTab) { var delta = shiftIsDown ? -1 : 1; Post(() => _overlay.MoveSelection(delta)); return Swallow(key); }
            if (key == NativeMethods.VkRight || key == NativeMethods.VkDown) { Post(() => _overlay.MoveSelection(1)); return Swallow(key); }
            if (key == NativeMethods.VkLeft || key == NativeMethods.VkUp) { Post(() => _overlay.MoveSelection(-1)); return Swallow(key); }
            if (key == NativeMethods.VkDelete || (key == NativeMethods.VkW && controlIsDown)) { Post(_overlay.CloseSelected); return Swallow(key); }
            if (key == NativeMethods.VkEscape) { Post(() => Dismiss(false)); return Swallow(key); }
            if (key == NativeMethods.VkReturn) { Post(() => Dismiss(true)); return Swallow(key); }
        }

        if (_altTabSession && IsSwitcherOnScreen && isKeyUp && IsAltKey(key))
        {
            Post(() => Dismiss(true));
            // Deliberately falls through to "not consumed". Dismissing the switcher is our business;
            // the release of Alt is the whole system's. A swallowed Alt key-up leaves every
            // application believing Alt is still held: letters stop typing because they become menu
            // accelerators, and mouse clicks turn into Alt+clicks, which is why a double-click on a
            // folder opens Properties instead of the folder.
        }

        return false;
    }

    /// <summary>Consumes a key-down, and arranges for its key-up to be consumed with it.</summary>
    private bool Swallow(uint key)
    {
        // Modifiers are never recorded, so the branch that eats a matching key-up can never reach
        // one. Their release must always get through - see the note in ShouldConsume.
        if (!IsModifierKey(key)) _swallowedKeyDowns[key] = Environment.TickCount64 + OrphanKeyUpGraceMilliseconds;
        return true;
    }

    private static bool IsModifierKey(uint key) =>
        key is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    private static bool IsAltKey(uint key) => key is NativeMethods.VkMenu or NativeMethods.VkLMenu or NativeMethods.VkRMenu;

    /// <summary>Hands work to the UI thread without waiting for it. Returns false if it is gone.</summary>
    private bool Post(Action action)
    {
        try
        {
            if (_overlay.IsDisposed || !_overlay.IsHandleCreated) return false;
            _overlay.BeginInvoke(action);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return false; // the overlay was torn down underneath us during shutdown
        }
    }

    public void ShowPreview()
    {
        if (_overlayVisible) return;
        OpenSwitcher(false, 1);
    }

    private void OpenSwitcher(bool altTabSession, int initialDirection)
    {
        var groups = SwitcherGrouper.Group(_catalog.Snapshot());
        if (groups.Count == 0)
        {
            // Nothing to show: drop the grace window now so the hook stops consuming immediately
            // rather than eating keys for the rest of it.
            _altTabSession = false;
            Volatile.Write(ref _openingDeadline, 0);
            return;
        }

        _previousForegroundWindow = NativeMethods.GetForegroundWindow();
        _altTabSession = altTabSession;
        _overlay.ShowGroups(groups, initialDirection);
    }

    private void Dismiss(bool activate)
    {
        var window = _overlay.SelectedWindow;
        _overlay.Hide(); // raises VisibleStateChanged(false), so the hook stops consuming right here
        if (!activate || window is null)
        {
            if (_previousForegroundWindow != 0 && NativeMethods.IsWindow(_previousForegroundWindow))
                WindowActivator.Activate(_previousForegroundWindow);
            _previousForegroundWindow = 0;
            return;
        }
        _previousForegroundWindow = 0;
        WindowActivator.Activate(window.Handle);
    }

    private void DismissAndActivate(WindowRecord window)
    {
        _overlay.Hide();
        _previousForegroundWindow = 0;
        WindowActivator.Activate(window.Handle);
    }

    public void Dispose()
    {
        _hook.Dispose();
        _overlay.Dispose();
    }

    public void SetLayoutMode(SwitcherLayoutMode mode) => _overlay.SetLayoutMode(mode);
}
