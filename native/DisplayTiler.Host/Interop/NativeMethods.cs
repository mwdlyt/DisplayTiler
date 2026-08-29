using System.Runtime.InteropServices;
using System.Text;

namespace DisplayTiler.Host.Interop;

internal static partial class NativeMethods
{
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmKeyUp = 0x0101;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmSysKeyUp = 0x0105;
    internal const uint WmClose = 0x0010;
    internal const int VkTab = 0x09;
    internal const int VkControl = 0x11;
    internal const int VkShift = 0x10;
    internal const int VkEscape = 0x1B;
    internal const int VkReturn = 0x0D;
    internal const int VkLeft = 0x25;
    internal const int VkUp = 0x26;
    internal const int VkRight = 0x27;
    internal const int VkDown = 0x28;
    internal const int VkDelete = 0x2E;
    internal const int VkW = 0x57;
    internal const int VkMenu = 0x12;
    internal const int VkLMenu = 0xA4;
    internal const int VkRMenu = 0xA5;
    internal const int VkF12 = 0x7B;
    internal const uint GwOwner = 4;
    internal const int GwlExStyle = -20;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const uint GaRootOwner = 3;
    internal const uint DwmwaCloaked = 14;
    internal const uint SwRestore = 9;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpShowWindow = 0x0040;
    internal static readonly nint HwndTop = 0;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint WmNull = 0x0000;
    internal const uint WmQuit = 0x0012;
    // Set on a keystroke that was synthesised by software rather than pressed on a keyboard.
    internal const uint LlkhfInjected = 0x10;
    // SetWindowPos sends WM_WINDOWPOSCHANGING to the owning thread and waits for it; the async flag
    // posts instead, so a window belonging to a hung application cannot block the caller.
    internal const uint SwpAsyncWindowPos = 0x4000;
    internal const uint SmtoAbortIfHung = 0x0002;

    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    internal delegate bool EnumWindowsProc(nint handle, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct { public uint VkCode; public uint ScanCode; public uint Flags; public uint Time; public nuint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg { public nint Handle; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize { public int Width; public int Height; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins { public int Left; public int Right; public int Top; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DwmThumbnailProperties
    {
        public uint Flags;
        public Rect Destination;
        public Rect Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint handle, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetLastActivePopup(nint handle);
    [DllImport("user32.dll")] internal static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint handle, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint handle, StringBuilder text, int capacity);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern long GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool BringWindowToTop(nint handle);
    [DllImport("user32.dll")] internal static extern nint SetActiveWindow(nint handle);
    [DllImport("user32.dll")] internal static extern nint SetFocus(nint handle);
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint handle, uint command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint handle, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint handle, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref DwmThumbnailProperties properties);
    [DllImport("dwmapi.dll")] internal static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out NativeSize size);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint handle, uint attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmExtendFrameIntoClientArea(nint handle, ref Margins margins);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] internal static extern int GetMessage(out Msg message, nint handle, uint filterMin, uint filterMax);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] internal static extern nint DispatchMessage(ref Msg message);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] internal static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] internal static extern nint SendMessageTimeout(nint handle, uint message, nuint wParam, nint lParam, uint flags, uint timeoutMilliseconds, out nuint result);
}
