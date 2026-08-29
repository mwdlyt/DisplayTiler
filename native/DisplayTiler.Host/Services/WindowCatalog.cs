using System.Diagnostics;
using System.Text;
using DisplayTiler.Core;
using DisplayTiler.Host.Interop;

namespace DisplayTiler.Host.Services;

internal sealed class WindowCatalog
{
    public IReadOnlyList<WindowRecord> Snapshot()
    {
        var rawWindows = new List<RawWindow>();
        // One process usually owns several windows, and identity is the expensive part of this walk.
        var identities = new Dictionary<uint, (string Path, string ProcessName)>();
        var newest = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var zOrder = 0;
        NativeMethods.EnumWindows((handle, _) =>
        {
            var className = ReadClassName(handle);
            var isEligible = IsEligible(handle);
            if (!isEligible && !className.Equals("Windows.UI.Core.CoreWindow", StringComparison.Ordinal)) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (!identities.TryGetValue(processId, out var identity))
            {
                identity = ReadProcessIdentity(processId);
                identities[processId] = identity;
            }
            var (path, processName) = identity;
            rawWindows.Add(new RawWindow(handle, processId, path, processName, ReadWindowText(handle), className, newest - zOrder++, isEligible));
            return true;
        }, 0);

        return rawWindows
            .Where(window => window.IsEligible)
            .Where(window => !IsInternalHostedWindow(window, rawWindows))
            .Select(window => ResolveHostedIdentity(window, rawWindows))
            .Select(window => new WindowRecord(
                window.Handle,
                window.ProcessId,
                window.ProcessPath,
                window.ProcessName,
                window.Title,
                window.ClassName,
                null,
                window.LastActivatedUnixMilliseconds))
            .ToArray();
    }

    private static RawWindow ResolveHostedIdentity(RawWindow window, IReadOnlyList<RawWindow> windows)
    {
        if (!window.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return window;

        var hostedWindow = windows.FirstOrDefault(candidate =>
            candidate.ProcessId != window.ProcessId
            && candidate.ClassName.Equals("Windows.UI.Core.CoreWindow", StringComparison.Ordinal)
            && candidate.Title.Equals(window.Title, StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(candidate.ProcessPath) || !string.IsNullOrWhiteSpace(candidate.ProcessName)));

        return hostedWindow is null
            ? window
            : window with
            {
                ProcessPath = hostedWindow.ProcessPath,
                ProcessName = hostedWindow.ProcessName,
            };
    }

    private static bool IsInternalHostedWindow(RawWindow window, IReadOnlyList<RawWindow> windows) =>
        window.ClassName.Equals("Windows.UI.Core.CoreWindow", StringComparison.Ordinal)
        && windows.Any(candidate =>
            candidate.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
            && candidate.Title.Equals(window.Title, StringComparison.OrdinalIgnoreCase));

    private static (string Path, string ProcessName) ReadProcessIdentity(uint processId)
    {
        string processName = string.Empty;
        string path = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch { /* The process may have exited between enumeration and inspection. */ }

        // Deliberately no Process.MainModule here. It walks another process's module list, which
        // throws for anything elevated or protected and can block for a long time on a busy target -
        // all of it on the thread that is trying to put the switcher on screen. The limited-
        // information query below answers the same question without opening the process for read.

        if (!string.IsNullOrWhiteSpace(path)) return (path, processName);
        var processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (processHandle == 0) return (path, processName);
        try
        {
            uint capacity = 1024;
            var builder = new StringBuilder((int)capacity);
            if (NativeMethods.QueryFullProcessImageName(processHandle, 0, builder, ref capacity)) path = builder.ToString();
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
        return (path, processName);
    }

    private static bool IsEligible(nint handle)
    {
        if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.GetWindowTextLength(handle) == 0) return false;
        if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var styles = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
        if ((styles & NativeMethods.WsExToolWindow) != 0 || (styles & NativeMethods.WsExNoActivate) != 0) return false;
        return IsAltTabRepresentative(handle);
    }

    private static bool IsAltTabRepresentative(nint handle)
    {
        var candidate = NativeMethods.GetAncestor(handle, NativeMethods.GaRootOwner);
        while (true)
        {
            var popup = NativeMethods.GetLastActivePopup(candidate);
            if (popup == candidate) break;
            candidate = popup;
            if (NativeMethods.IsWindowVisible(candidate)) break;
        }
        return candidate == handle;
    }

    private static string ReadWindowText(nint handle) { var builder = new StringBuilder(NativeMethods.GetWindowTextLength(handle) + 1); NativeMethods.GetWindowText(handle, builder, builder.Capacity); return builder.ToString(); }
    private static string ReadClassName(nint handle) { var builder = new StringBuilder(256); NativeMethods.GetClassName(handle, builder, builder.Capacity); return builder.ToString(); }

    private sealed record RawWindow(
        nint Handle,
        uint ProcessId,
        string ProcessPath,
        string ProcessName,
        string Title,
        string ClassName,
        long LastActivatedUnixMilliseconds,
        bool IsEligible);
}
