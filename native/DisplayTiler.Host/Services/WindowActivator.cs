using DisplayTiler.Host.Interop;

namespace DisplayTiler.Host.Services;

internal static class WindowActivator
{
    /// <summary>How long to wait for a window's owner to acknowledge a trivial message.</summary>
    /// <remarks>
    /// Short on purpose: this runs while the user is waiting to be dropped into the window they
    /// picked, and the only question being asked is "are you alive".
    /// </remarks>
    private const uint ResponsivenessProbeMilliseconds = 250;

    public static bool Activate(nint windowHandle)
    {
        if (windowHandle == 0 || !NativeMethods.IsWindow(windowHandle)) return false;

        if (NativeMethods.IsIconic(windowHandle))
            NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.SwRestore);

        // Ensure the topmost switcher has left the composed frame before raising the target.
        NativeMethods.DwmFlush();

        var currentThread = NativeMethods.GetCurrentThreadId();
        var targetThread = NativeMethods.GetWindowThreadProcessId(windowHandle, out _);
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundThread = foregroundWindow == 0
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);

        // AttachThreadInput merges our input queue with the other thread's. If that thread has
        // stopped pumping messages - routine for Electron applications such as Cursor or the ChatGPT
        // desktop app, which block for seconds at a time - then everything inside the try below
        // blocks with it, and we stay merged to a dead queue for the whole duration. So probe first
        // and skip the attach for an application that is not answering: a slightly less reliable
        // raise beats an input queue wedged to somebody else's hang.
        var attachedToTarget = targetThread != 0
            && targetThread != currentThread
            && IsResponding(windowHandle)
            && NativeMethods.AttachThreadInput(currentThread, targetThread, true);
        var attachedToForeground = foregroundThread != 0
            && foregroundThread != currentThread
            && foregroundThread != targetThread
            && IsResponding(foregroundWindow)
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            NativeMethods.BringWindowToTop(windowHandle);
            NativeMethods.SetWindowPos(
                windowHandle,
                NativeMethods.HwndTop,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow | NativeMethods.SwpAsyncWindowPos);
            NativeMethods.SetForegroundWindow(windowHandle);
            NativeMethods.SetActiveWindow(windowHandle);
            NativeMethods.SetFocus(windowHandle);
        }
        finally
        {
            if (attachedToForeground) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            if (attachedToTarget) NativeMethods.AttachThreadInput(currentThread, targetThread, false);
        }

        if (NativeMethods.GetForegroundWindow() == windowHandle) return true;

        // One final foreground request after detaching handles apps that create their
        // activation target during restore (common with packaged Windows applications).
        NativeMethods.BringWindowToTop(windowHandle);
        NativeMethods.SetForegroundWindow(windowHandle);
        return NativeMethods.GetForegroundWindow() == windowHandle;
    }

    /// <summary>True if the window's owning thread is still servicing its message queue.</summary>
    private static bool IsResponding(nint windowHandle) =>
        NativeMethods.SendMessageTimeout(
            windowHandle,
            NativeMethods.WmNull,
            0,
            0,
            NativeMethods.SmtoAbortIfHung,
            ResponsivenessProbeMilliseconds,
            out _) != 0;
}
