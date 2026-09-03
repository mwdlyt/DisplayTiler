# DisplayTiler native host

Developer notes. For what the application does and how to install it, see the
[root README](../README.md).

```powershell
dotnet run --project native/DisplayTiler.Host
```

The process has no main window and runs in the notification area, so the terminal staying open is
expected. `Ctrl`+`Shift`+`F12` is the emergency fail-open chord, worth knowing before you run a
debug build that owns `Alt`+`Tab`.

## Projects

| | |
|---|---|
| `DisplayTiler.Core` | `WindowRecord`, `ApplicationGroup`, `SwitcherGrouper`. Pure logic, no Windows dependencies, so grouping can be reasoned about on its own. |
| `DisplayTiler.Host` | Tray host, keyboard hook, window catalog, switcher overlay, settings. |

## Files worth reading first

| | |
|---|---|
| `Services/KeyboardHook.cs` | Owns the `WH_KEYBOARD_LL` hook on a dedicated thread. Read the class comment before changing anything here: this is the code that can freeze the whole desktop. |
| `Services/SwitcherController.cs` | Decides which keystrokes are consumed, and when the hook must fail open. |
| `Services/WindowActivator.cs` | Raising a window without blocking on an application that has stopped pumping messages. |
| `Services/SwitcherOverlay.cs` | Layout, painting, DWM thumbnails, per-application accent colours. `UiScale` is the single size dial. |
| `Services/WindowCatalog.cs` | Enumerates eligible top-level windows and resolves their owning process. |

## Things that will bite you

- **The executable locks itself while running.** `dotnet publish -o dist/win-x64` fails with an
  access-denied error on `DisplayTiler.exe` if an instance is live. Stop it first, or you will keep
  testing the previous binary while believing you rebuilt it.
- **`dotnet build` does not update `dist/`.** Only `dotnet publish -o dist/win-x64` does.
- **Never consume a modifier key-up** in the hook, and never do slow work on the hook thread.
- **`DisplayTiler.ico` is generated**, not hand-maintained. Change `assets/DisplayTiler.png` and run
  `tools/Build-Icon.ps1`; CI fails if the committed `.ico` does not match the artwork.

## Not implemented yet

Per-monitor taskbars, window-placement rules, virtual-desktop tracking, and a `SetWinEventHook`
activity catalog to replace the snapshot-time ordering.
