# DisplayTiler — product and technical specification (v0.1)

## Product promise

DisplayTiler is a Windows 11–only window-management companion. Its defining feature is a visual `Alt` + `Tab` switcher that keeps windows from the same application together, so a person can scan by application first and select the exact window second. It also adds per-monitor taskbars and deterministic window-placement rules.

## Product scope

### Current alpha implementation (August 2026)

The working alpha is in `native/` and publishes as a self-contained Windows executable. It currently includes the grouped switcher, packed-grid and category-row layouts, DWM live previews, click/keyboard activation, close controls, focus restoration on cancel, a single-instance tray host, persistent JSON settings, and per-user start-on-sign-in registration.

The alpha keeps the low-latency Win32/DWM path for window discovery, input, and previews. Its glass layer captures the actual desktop region once as the switcher opens, downsamples it into a blur, and applies a dark Fluent tint. This is intentionally a snapshot: an `Alt`+`Tab` session is short, and avoiding continuous capture keeps invocation fast and avoids the large Windows App SDK runtime in the first distributable executable. A later WinUI/Windows Composition renderer remains the recommended long-term accessibility and animation path.

Layout selection is only exposed in the tray and Settings UI. No unmodified letter key is registered globally or consumed by the switcher.

### 1. Grouped window switcher (MVP)

- Intercept configurable switcher shortcuts, initially `Alt` + `Tab` and `Alt` + `Shift` + `Tab`.
- Enumerate eligible top-level windows; exclude hidden, tool, cloaked, owned, and DisplayTiler windows.
- Group by stable application identity: packaged app AUMID when available, then executable path, then process ID fallback.
- Preserve application groups together; sort groups by the most-recent member. Sort members by most-recent activation.
- Show app icon, app name, count, title, live thumbnail where DWM permits, and monitor/desktop badges.
- Keyboard-first navigation: left/right selects windows, up/down selects groups, Enter activates, Delete/`Ctrl`+`W` closes when permitted, Escape dismisses; mouse remains fully supported.
- Support search, pinned groups, excluded apps, “one item per app” collapsed mode, compact density, and per-monitor or all-monitor scope.
- Guarantee a fast, non-blocking overlay. First visible frame target: under 100 ms after the chord; selection feedback target: under 16 ms.

### 2. Per-monitor taskbars (v1)

- Optional overlay taskbar on every display edge; never patch or replace Explorer binaries.
- Per-monitor filtering: current monitor, all windows, or a user-selected monitor set.
- Group/ungroup buttons, show labels, combine policy, clock/tray controls, auto-hide, and primary-taskbar coexistence policy.
- Taskbar buttons activate, minimize, close, drag-reorder, and expose a group flyout.

### 3. Window rules (v1)

- Match by process path, executable name, AUMID, window class, title regex, monitor count, virtual desktop, and launch context.
- Actions: move to monitor, set normalized zone/rect, maximize/minimize, assign virtual desktop, set always-on-top, apply opacity, and delay/retry until the window is ready.
- Rule ordering, enable toggle, dry-run preview, conflict explanation, import/export, and event history.

## Non-goals

- No macOS/Linux support.
- No Explorer patching, injection into other processes, or UI automation as the primary control path.
- No attempt to read private browser tab contents; the switcher operates on top-level Windows windows. Browser tab grouping is only possible where the browser exposes separate top-level windows.

## Recommended implementation

Use **C# on .NET 10**. Build the settings application and switcher UI in **WinUI 3 / Windows App SDK**, with a dedicated native-interoperability layer. This is the best fit for Windows 11 visuals, accessibility, packaging, DPI, app identity, notifications, and long-term Win32 support. Use Rust only for an optional isolated low-level helper if profiling proves C# interop cannot meet latency targets; it is not the default.

| Layer | Choice | Responsibility |
| --- | --- | --- |
| UI shell | C#, WinUI 3, Windows App SDK | Settings, onboarding, tray, theme, accessibility |
| Overlay renderer | WinUI 3 composition / Windows Composition | Non-activating, per-monitor switcher and taskbar overlays |
| Window engine | C# + source-generated P/Invoke | Enumeration, activation, placement, event tracking |
| Windows integrations | Win32, DWM, UIA only when necessary, virtual desktop interfaces | Hotkeys, thumbnails, display topology, desktop state |
| Persistence | SQLite + JSON export | Profiles, rules, diagnostics, migrations |
| Installer | MSIX + optional unpackaged bootstrapper | Updates, startup registration, recovery |
| Tests | xUnit, WinAppDriver/Playwright-style UI automation, dedicated multi-monitor lab | Engine, UI, compatibility, accessibility |

## Architecture

1. **Host** owns lifecycle, single-instance behavior, startup, logging, profile loading, and the notification-area icon.
2. **Input service** observes keyboard state with `SetWindowsHookEx(WH_KEYBOARD_LL)` or a carefully scoped equivalent. It must detect the chord, suppress only the configured chord while active, and immediately yield unhandled input.
3. **Window catalog** subscribes to `SetWinEventHook` for create, destroy, foreground, name-change, location-change, and minimize events. It maintains a normalized immutable snapshot indexed by HWND and app identity.
4. **Switcher controller** takes a snapshot at invocation, builds groups, owns focus selection, and tells the overlay to render. Activation uses `SetForegroundWindow` with documented foreground-permission handling and graceful fallback.
5. **Overlay host** creates a topmost, DPI-aware tool window on the invoking monitor. The alpha briefly becomes the active switcher and restores the previous foreground window on cancel; it is never included in the catalog.
6. **Taskbar host** shares the catalog and overlay technology but has an independent lifetime and edge-layout system.
7. **Rules engine** consumes window events, evaluates deterministic ordered rules, queues actions, and records an explainable audit trail.

## Data model

`WindowRecord`: HWND, process ID/path, AUMID, class, title, display ID, virtual desktop ID, bounds, state, z-order hint, last activation UTC, icon key, eligibility reason.

`ApplicationGroup`: stable key, display name, icon key, ordered `WindowRecord` collection, last activation UTC, pin state.

`Rule`: id, enabled, priority, match expression, actions, retry policy, profile id, timestamps. Rules are evaluated in ascending priority; later actions may only override an earlier action when explicitly marked as an override.

## Safety, privacy, and reliability

- Require no administrator rights for ordinary use. Detect elevated windows and explain when Windows prevents activation or manipulation.
- Store configuration locally; collect diagnostics only after opt-in. Redact paths/titles in shared reports by default.
- Use crash-safe logging, an emergency “disable on next start” flag, and a tray-menu kill switch for the switcher hook.
- Respect Windows accessibility: keyboard-only operation, high contrast, reduced motion, screen-reader labels, scalable type, and per-monitor DPI.

## Delivery plan

1. **Foundation (2–3 weeks):** solution skeleton, host, settings shell, catalog, multi-monitor test harness, telemetry-free logs.
2. **Switcher alpha (4–6 weeks):** hotkey, grouped model, overlay, keyboard navigation, thumbnails, exclusions, performance instrumentation.
3. **Switcher beta (3–4 weeks):** profiles, search, virtual desktops, accessibility, compatibility matrix, installer/update path.
4. **Taskbars (5–7 weeks):** overlay bars, button policy, grouped flyouts, monitor filtering, conflict handling with Explorer.
5. **Rules (4–6 weeks):** matcher/action engine, editor, dry runs, retries, history, import/export.

## Acceptance criteria for the first shippable switcher

- On Windows 11 23H2+ with one to four mixed-DPI displays, all eligible windows appear in their application group after `Alt` + `Tab`.
- Repeated `Tab` and `Shift` + `Tab` navigation is predictable and never loses selection when catalog events arrive.
- Releasing Alt activates the highlighted window; Escape restores prior focus with no visible flash.
- The overlay appears on the intended monitor, restores the previous foreground window when canceled, and does not show itself as a switchable item.
- The app survives Explorer restart, monitor hot-plug, sleep/resume, virtual desktop transitions, and an inaccessible/elevated target window.
- 95th-percentile invocation-to-first-frame is below 100 ms on the supported hardware baseline.
