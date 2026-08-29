using System.Runtime.InteropServices;
using DisplayTiler.Host.Interop;

namespace DisplayTiler.Host.Services;

/// <summary>
/// Owns the low-level keyboard hook on a thread of its own.
/// </summary>
/// <remarks>
/// Windows runs a <c>WH_KEYBOARD_LL</c> callback on the thread that installed it, and it holds every
/// keyboard and mouse event for the entire machine until that callback returns. Installing the hook
/// on the UI thread therefore lets ordinary UI work freeze input system-wide: capturing the screen
/// for the blurred backdrop, enumerating processes, or raising a window owned by an application that
/// has stopped pumping messages. This thread does nothing but classify keystrokes, so it is always
/// free to answer immediately, and a wedged UI thread can no longer take the machine down with it.
/// </remarks>
internal sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.HookProc _callback; // held in a field so the GC cannot collect it
    private readonly Func<nint, NativeMethods.KbdLlHookStruct, bool> _shouldConsume;
    private readonly ManualResetEventSlim _installed = new(false);
    private readonly Thread _thread;
    private uint _threadId;
    private nint _hook;
    private Exception? _installFailure;

    /// <param name="shouldConsume">
    /// Runs on the hook thread for every keystroke. Return true to swallow it. Must be fast and must
    /// never block on another thread: whatever time it takes is time the whole machine has no input.
    /// </param>
    public KeyboardHook(Func<nint, NativeMethods.KbdLlHookStruct, bool> shouldConsume)
    {
        _shouldConsume = shouldConsume;
        _callback = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "DisplayTiler keyboard hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _installed.Wait();
        if (_installFailure is not null) throw _installFailure;
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _callback, 0, 0);
        if (_hook == 0)
            _installFailure = new InvalidOperationException($"Could not install DisplayTiler keyboard hook ({Marshal.GetLastWin32Error()}).");
        _installed.Set();
        if (_hook == 0) return;

        // A low-level hook is only ever dispatched to a thread that is pumping messages, so this
        // loop is not idle bookkeeping - it is what makes the hook fire at all.
        while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);

            // Synthetic keystrokes come from accessibility tools, remote desktop, macro utilities,
            // and from anything trying to clear a modifier that got stuck. Swallowing those is how a
            // "release all modifiers" helper silently does nothing, so they always pass through.
            if ((data.Flags & NativeMethods.LlkhfInjected) != 0)
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);

            if (_shouldConsume(wParam, data)) return 1;
        }
        catch
        {
            // An exception must never escape into the hook chain: it would tear the process down
            // while Windows is holding the machine's input queue. Fail open and pass the key on.
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, NativeMethods.WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _installed.Dispose();
    }
}
