# SmoothMice — release notes

Read this file before changing `<Version>` in `Directory.Build.props`. Every new version gets **its own section** (newest on top) summarizing the real changes (pending diff or commit) as bullets. Everything in this file is written in English.

---

## 2.2.13 — 2026-10-04

### Startup

- Faster start at Windows sign-in: SmoothMice records the code it compiles while starting and, on the next start, compiles it ahead of time on spare CPU cores (.NET multicore JIT). The profile lives in `%LOCALAPPDATA%\SmoothMice\Jit`; if it is missing or cannot be written, startup behaves exactly as before.

## 2.2.12 — 2026-10-01

### In-app updates

- Fixed in-app updates that could fail with "OTA_SETUP_FAILED code %ERRORLEVEL%" and only work on a second try. The installer could start while SmoothMice was still closing (the wait before installing was skipped on some machines), so its files were still locked.
- If the installer still fails, it is retried automatically up to 3 times. A failure message now shows the installer's real exit code and the path to its log.
- An incomplete download is now reported as a download error instead of running a damaged installer.
- Note: these fixes apply to updates started from 2.2.12 onward; the update to 2.2.12 itself still uses the previous version's installer launcher.

## 2.2.11 — 2026-10-01

### Profile icons found automatically

- Profile icons are found automatically each time the window opens. App profiles whose executable path is unknown, or no longer exists (e.g. an app updated into a new versioned folder), are located off the UI thread from a running process, or from the executables Windows recorded as run by this user (Explorer's MuiCache and the Program Compatibility Assistant store). The newest existing copy wins, and the path is saved, so the icon stays after the app closes. Apps that were uninstalled get no icon.
- Saving an edited profile no longer overwrites a path learned after the editor copied the profile.

### Repository

- The repository is English-only, as `AGENTS.md` now states: the remaining Portuguese docs, comments, installer messages, and older changelog entries were translated. GitHub release notes are written in English from this version on.
- Removed the Cursor rules (`.cursor/rules`).

## 2.2.10 — 2026-10-01

### Fixed — "Do not activate in games" missed games on proprietary engines

- Game detection only recognized engine window classes (Unity, Unreal, SDL, Godot, GLFW, Techland), so games on in-house engines such as God of War Ragnarök and The Witcher 3 kept being smoothed.
- An executable now also counts as a game when Windows registered it as one (Game Bar's `GameConfigStore`) or when it is installed in a Steam library (`steamapps\common`). The window still has to be foreground or fullscreen/borderless, and exclusions (browsers, Explorer, launchers) still win. `LeagueClientUx.exe` and `EADesktop.exe` were added to the launcher exclusions.
- The game list is read on a background timer every 2 minutes; the mouse hook only does a hash-set lookup.

### Profiles

- The global profile is now named **All applications** (saved settings are renamed on load) and has no icon.
- The profile list shows each app's icon. New profiles store the executable path for this; older profiles use a running process with the same name, or learn their path when picked again. Icons are re-resolved each time the list opens, so an app started later gets its icon.
- **Add app profile**: the two choices sit side by side with icons and no descriptions; the stray access-key underscores are gone.
- **Select running window**: one entry per executable (the first titled window wins), each with its icon.
- **Reset All** is now **Reset all profiles**, and its tooltip and confirmation say what it does: delete every app profile and restore the All applications profile and app options to defaults.

### Interface

- Dark mode is a sun/moon icon button in the top-left corner (opposite Help) instead of a checkbox.
- The divider between the Animation and Acceleration columns was removed.
- Shared button templates now honor `HorizontalContentAlignment`/`VerticalContentAlignment` (centered by default), so custom button content can be laid out.

## 2.2.9 — 2026-10-01

### Free-Spin Inertia Suppression rebuilt

- Inertia is now filtered by a rule set measured on labelled raw input sessions (all movement and wheel input), replacing the calibration-sample model. That model never suppressed live: its scaling and tie-breaking were broken, it discarded the wheel direction, it saw only ~24 ms of movement, and one lift-like legitimate sample disabled it entirely.
- Lifting a free-spin mouse rocks the wheel. Dropped: a first pulse 10–300 ms after a sharp movement (≥250 px/s), an opposite pulse within 350 ms, slow rocking after a reversal, and one slow one-way follow-up. Scrolling while the cursor moves, or after it decelerates to a stop, always passes.
- Every pulse is decided the moment it arrives. No pulse is held or delayed.
- On two recorded sessions: 88% of inertia pulses dropped; 37 of 255 legitimate scrolls touched (usually only their first notch).
- The Free-Spin window was simplified to the module switch, the Read-only/Suppress mode, live decisions, and a "Record decision log" switch. That switch only appears while the module is on and is off by default.
- The F8 sample capture, calibration targets, confidence threshold and sample-based model were removed. Old `settings.json` files still load; the obsolete fields are ignored.
- `tools/free-spin-raw-recorder.ps1` records a labelled two-stage session for future tuning.

### Fixed

- **Settings could revert after an install or exit.** Only one SmoothMice runs per user session now. A second launch brings the running window to the front and exits before loading settings. Before, two instances each installed a mouse hook, and the stale one overwrote `settings.json` when it exited.

## 2.2.8 — 2026-09-24

- Uses a dedicated worker and native high-resolution waitable timer for the 4 ms scroll cadence, with a compatibility fallback and no periodic idle work.
- Limits each pulse's animation advance to 8 ms per tick after a scheduler stall. Distance is retained, with extra completion time instead of a catch-up jump.
- Tags injected ticks with per-axis epochs and rejects obsolete output in the mouse hook after a reversal or target change. Background PostMessage admission is serialized without holding the hook lock across SendInput.

## 2.2.7-test.1 — 2026-09-24 (local test build, not released)

- Tray Enable/Disable now synchronizes the selected global profile before saving, so persistence cannot undo the toggle.
- Numeric live apply now debounces edits for 300 ms instead of saving every 300 ms while the settings window is idle. Enter, focus loss and closing still commit edits.
- Valid JSON containing null profile entries now uses backup/default recovery instead of crashing at startup.
- New wheel input over another control drops the previous target's queued motion and acceleration history, including both axes and already-calculated ticks that have not yet injected.

## 2.2.6 — 2026-09-24

### Fixed — Wheel inside a game leaked to apps on the second monitor

- **Scrolling in a game could scroll windows on another monitor instead.** Games hide the pointer
  without confining it, so the invisible cursor drifts onto a second monitor and Windows' "scroll
  inactive windows when hovering" delivers the wheel to the app there (this happened even with
  SmoothMice off). While a fullscreen/borderless window with a hidden pointer is in the foreground
  and the cursor is over another window, SmoothMice now switches wheel routing to the focused
  window at runtime and restores it as soon as that no longer holds (and on exit). The persisted
  Windows setting is never changed.
- **An animation no longer finishes on another window if the cursor leaves the target mid-scroll**:
  remaining steps are posted to the original target instead of following the cursor.

## 2.2.5 — 2026-09-24

### Fixed — Winput LAN wheel still ignored in 2.2.4

- **2.2.4 compared the full 64-bit Winput LAN tag, but Windows hands `WH_MOUSE_LL` only the low
  32 bits of `dwExtraInfo`** (`0x4E505554`), so the remote wheel was still dropped. The hook now
  compares the low 32 bits.

## 2.2.4 — 2026-09-24

### Fixed — Wheel from Winput LAN was not smoothed

- **Remote wheel input from Winput LAN bypassed smoothing.** The mouse hook ignored every
  `LLMHF_INJECTED` event to avoid re-processing its own output, which also dropped the wheel
  Winput LAN replays from a controlling PC via `SendInput`. Injected events carrying Winput LAN's
  `dwExtraInfo` tag (`0x57494E505554`) are now treated as physical input: smoothed, shown in
  **Monitorar scroll** and visible to calibration. SmoothMice's own injections and input
  from any other tool remain ignored.

## 2.2.3 — 2026-09-23

### Fixed — "Start smoothing" / animation easing curve and settings

- **High "Start smoothing" values made scrolls stop dead.** The easing curve is cut at the end of
  the animation time, and its deceleration tail only reached near-zero velocity when the
  acceleration phase was ≤ ¼ of the animation. With Start smoothing ≥ Animation time the curve
  was pure acceleration and ended at *peak* velocity (≈37% of peak at half the time). The
  acceleration phase is now capped at half the animation time, and short tails decay faster
  (velocity stays continuous) so every scroll eases out to ≤ ~5% of peak. Curves for the default
  settings (and any Start smoothing ≤ ¼ of Animation time) are unchanged.
- **Scrolling across windows with different profiles could jerk or briefly scroll backwards.**
  Every tick reshaped all in-flight notches with the settings of the *latest* notch; each notch
  now keeps the animation time, easing and curve it was pushed with.
- **Settings UI:** "Start smoothing" is disabled when Animation easing is off, and "Tail / head
  ratio" is disabled when it has no effect (easing off, or Start smoothing set explicitly).

## 2.2.2 — 2026-09-22

### Fixed — 2.2.1's fix for the oversized first-launch window was wrong; root cause and real fix

- 2.2.1 claimed to fix the main window sometimes showing much wider on first open, but the
  actual reported repro path (app auto-starts hidden in the tray, then the user opens it from
  the tray icon for the first time in that session) still showed the bug afterward.
- Root cause confirmed with live measurements against the running app (`GetWindowRect` /
  `GetWindowPlacement`, and a real STA-process repro of the exact `/tray` startup sequence): a
  window first created while minimized/hidden (the normal case, since the app launches with
  `/tray` on login) never gets a real layout pass against actual content — WPF does not resize
  the underlying HWND while minimized. Windows leaves the HWND with an arbitrary large default
  "restore" rect (`GetWindowPlacement` showed `rcNormalPosition` as wide as a full monitor width
  in one capture). When the tray's "Open" handler later flips `WindowState` to `Normal`, that
  stale rect becomes `ActualWidth`/`ActualHeight` — consistently, not transiently, so 2.2.1's
  "wait for a stable reading" approach could not catch it (the wrong value was already stable).
- Fix: `SnapClientSizeToDevicePixels` (`MainWindow.xaml.cs`) now forces a fresh
  `InvalidateMeasure`/`InvalidateArrange`/`UpdateLayout` pass once the window is genuinely Normal
  and visible, before trusting `ActualWidth`/`ActualHeight` — this makes WPF actually resync the
  HWND from real content instead of leaving the OS's stale restore rect in place. Verified with a
  real-process repro: without this fix the window settles at 468px wide (matching the exact width
  independently measured on the real installed app); with it, ~324px (matching the fixed 288px
  content + margins + chrome).

## 2.2.1 — 2026-09-22

### Fixed — settings could silently revert to defaults, losing custom app profiles

- `JsonSettingsRepository.Save` wrote `settings.json` directly with `File.WriteAllText`
  (truncate-then-write, not atomic). A crash, force-kill, or power loss mid-write left a
  truncated/invalid file; `LoadOrCreate` caught the parse failure and silently fell back to
  hard defaults — and since the app persists very frequently (every UI change, every 300 ms
  while the window is visible, on deactivate, after update checks), the very next save then
  overwrote the corrupted file with those defaults, permanently erasing any custom per-app
  profiles.
- Separately, `Save` had no locking: the UI's live-apply timer (UI thread) and a background
  update-check (thread-pool thread, via `CheckForUpdatesAsync`) could both call it on the same
  `JsonSettingsRepository` instance at the same time, racing on the same file.
- `Save` now writes to a temp file first and swaps it in with `File.Replace` (atomic on NTFS,
  and keeps the previous good file as `settings.json.bak` in the same operation), and both
  `Save`/`LoadOrCreate` take an instance lock so concurrent writers can no longer interleave.
  `LoadOrCreate` now falls back to `settings.json.bak` before ever returning defaults, and if a
  primary file is genuinely unreadable it is copied aside as `settings.json.corrupt-<timestamp>`
  instead of being silently discarded.

### Fixed — first launch could show much wider side margins than normal

- The main window's fixed-width content (288px) could end up centered in a wider-than-intended
  window on some launches, showing large empty gaps on both sides; closing and reopening the app
  always fixed it. Root cause: two independent code paths (`ContentRendered` and the `Loaded`
  handler) both raced to measure the window and freeze its size (`SizeToContent` → `Manual`) —
  on a cold first paint (JIT, style/resource resolution still settling) one of them could freeze
  the window at a transient, not-yet-final measurement.
- The window now requires the same width/height reading twice in a row (an `ApplicationIdle`
  turn apart) before freezing the size, and both callers go through one shared request path
  instead of racing each other.

## 2.2.0 — 2026-09-21

### Changed — smoothing engine rewritten as an independent pulse queue

- **Consistency:** every wheel notch now animates as its own independent pulse, with its own start
  time and distance, and overlapping pulses are summed. Each notch always delivers exactly its
  full distance over exactly `animationTime`, regardless of what else is animating.
- **Fixes the jump on resume:** scrolling again while the previous animation was still finishing
  used to produce a visibly bigger jump. The old engine kept a single shared ease-in ramp, so a
  new notch inherited whatever ramp state the previous motion had built up — measured as a ~4x
  first-tick spike purely depending on timing. There is no shared state to inherit any more.
- **Fixes weak continuous scrolling:** overlapping notches now add up instead of interfering, so
  sustained scrolling reaches the speed it should.
- **Dropped frames self-correct:** animation progress is a function of real elapsed time rather
  than tick count, so a late or skipped 4 ms timer callback no longer loses motion.
- The easing is the Michael Herf "pulse" curve ("Stopping", stereopsis.com), as used by Balazs
  Galambosi's MIT-licensed SmoothScroll — reimplemented from the public algorithm.

### New — Start smoothing (ms)

- A per-profile **Start smoothing (ms, 0 = auto)** field sets the ease-in duration of each pulse
  directly, instead of only indirectly through Tail / head ratio. `0` keeps the previous
  behaviour, so existing profiles are unchanged. Requires Animation easing to be on.

### Changed — acceleration no longer shrinks slow scrolling

- The acceleration multiplier used to fall as low as 0.10x, shrinking slower, evenly paced
  scrolling to a fraction of its distance. It now only ever multiplies up, never attenuates.

### Fixed — Scroll logs rendered every row as one string

- The shared `ListViewItem` template used a plain `ContentPresenter`, which silently ignores a
  `GridView`'s columns and falls back to `ToString()`. Rows now render as real columns with
  dividers aligned to their headers.
- Added a **PIXELS** column showing each raw pulse's pixel equivalent, and a live readout of
  scheduled vs. skipped animation ticks.

### New — Free-Spin inertia detection and calibration

- Detection and calibration for free-spin wheel inertia, with a guided calibration window and
  per-phase targets. Ships **off by default** (`read-only` detection mode, suppression disabled);
  existing settings files are unaffected until explicitly enabled.

### UI

- Selects, checkboxes and checkbox labels now show the hand cursor on hover.

> **Upgrade note:** `Animation time (ms)` now means the full duration of each pulse, which is a
> different meaning from previous versions. Existing values will feel noticeably faster — expect
> to retune. As a reference point, 300 ms with Tail / head ratio 2 approximates SmoothScroll's
> default feel.

---

## 2.1.6 — 2026-09-20

### New — persistent live dark mode

- **Dark mode:** a global **Dark mode** control now switches between the existing light palette and a sober neutral dark palette immediately, without restarting or recreating windows.
- **Coverage:** the shared semantic theme updates the main window plus profile, running-window, scroll-log, and Free-Spin surfaces already open; newly opened windows inherit the selected palette.
- **Persistence:** the setting is saved in `%APPDATA%\SmoothMice\settings.json` and starts in light mode when absent from older settings files.
- **UI polish:** main section wrappers are borderless, spacing before Updates is restored, and checkbox indicators retain their full geometry.

---

## 2.1.5 — 2026-09-20

### New — profile sources, controls, and English UI

- **Profile picker:** add an app profile from an executable path or a running window, with an initial window selected automatically when one is available.
- **Profile controls:** the **+** and **—** buttons now sit beside the profile selector.
- **UI:** reorganized the footer, translated visible application UI to English, and added the **Free-Spin Inertia Suppression (beta)** entry point.
- **Consolidation:** this release also includes the 2.1.3 and 2.1.4 fixes documented below.

---

## 2.1.4 — 2026-09-20

### Fixed — game bypass in Dying Light: The Beast + immediate persistence

- **Techland detection:** the real root class `techland_game_class` is now recognized as a strong game signal, still requiring a focused or fullscreen/borderless window and keeping the explicit exclusions.
- **Preference:** toggling **Do not activate in games** saves the global option immediately through the persistence flow, instead of depending on the order of WPF `Checked`/binding events.
- **Regression:** covered with the real signals captured from Dying Light: The Beast, fail-open negatives, and `ViewModel → snapshot → JSON` persistence.

---

## 2.1.3 — 2026-09-20

### New — conservative global bypass for games

- **Global option:** added **Do not activate in games**, persisted in JSON and off by default; when on, the physical wheel passes natively to conservatively identified games.
- **Classifier/cache:** classification by strong engine classes, root window, and focus/fullscreen/borderless signals, with exclusions for browsers, players, presentations, shell, and launchers. The per-root/PID cache has a short TTL and fails open.
- Pending animations for a game target are cancelled; normal apps and uncertain signals keep the existing smoothing.

---

## 2.1.2 — 2026-09-19

### Live scroll monitor + optional NDJSON diagnostics

- **New:** a scroll monitor button opens a modeless window with the raw physical pulses before smoothing: relative/UTC time, axis, delta/direction, interval, and burst/reversal markers.
- **Window:** keeps up to 500 rows, reports drops under overload, and offers **Clear** to start a controlled measurement.
- **Optional NDJSON:** `--scroll-log` still writes the persistent capture in parallel; opening the monitor creates no file or writer.
- Events flagged as injected (`LLMHF_INJECTED`) stay excluded. Diagnostics only observe input and do not change smoothing; the markers are heuristics, not proof of a hardware defect.

---

## 2.1.1 — 2026-05-05

### Fixed — per-app Enabled now applies to subprocesses (Steam, Electron, etc.)

- **Root cause:** apps such as Steam render content in child processes (`steamwebhelper.exe`, CEF/Electron helpers). Profile matching only checked the window's own executable and ignored the parent process, so a `steam.exe` profile with `Enabled = false` had no effect on windows rendered by the helpers.
- **Fix:** `ActiveAppResolver.QueryWindow` now looks up the parent process via `CreateToolhelp32Snapshot`. When there is no profile for the direct executable, `ProfileManager` tries the parent's. A profile for `steam.exe` (or any launcher) now applies to all its subprocesses.
- The result is cached per HWND — no extra overhead during a scroll session.

---

## 2.1.0 — 2026-05-05

### Per-app Enabled + Behaviour block restructure

- **Per-profile "Enabled":** the option moved from a global switch (`AppSettings`) to `ScrollProfileSettings.Enabled`, configurable on each profile (global and per app).
- **Behaviour block:** "Enabled" is now the block's first option (no section title). On the global profile it means "smooth unmapped apps"; on app profiles it controls only that app.
- **Removed:** the "Enable for all apps by default" checkbox (replaced by `Enabled` on the global profile).
- **Tray:** the tray Enable/Disable toggle still works — it flips the global profile's `Enabled`.
- **Hook:** now always installed; per-profile `Enabled` decides whether an event is intercepted, with no overhead when inactive.

---

## 2.0.7 — 2026-05-05

### Fixed — browser crash/erratic behaviour when opening SmoothMice or changing parameters

- **Root cause — stale focus:** `_cachedUseSendInput` was decided once in `OnMouseWheel` and never re-evaluated. If the user opened the settings window (or switched windows) during an animation, the remaining ticks kept sending `SendInput` to the newly focused window (SmoothMice or another), possibly injecting events into the wrong browser or an unexpected state.
- **Fix:** `TickCore` now calls `GetAncestor` + `GetForegroundWindow` on every tick. The `SendInput` vs `PostMessage` strategy is now dynamic; only process elevation (stable per session) stays cached in `_cachedIsElevated`.
- **Secondary cause — recycled HWND:** if the browser navigated during the animation, `_cachedHwnd` could be destroyed and its number reused by another window in another process. `PostMessage` to that recycled handle delivered events to an unintended target.
- **Fix:** `ScrollInjector.TryPostWheel` validates the handle with `IsWindow(hwnd)` before each `PostMessage` and silently drops invalid handles.

---

## 2.0.6 — 2026-05-05

### Fixed — Explorer "stall then jump" (native pass-through)

- **Root cause (confirmed by runtime logs):** Explorer's `DirectUIHWND` and `SysListView32`/`SysTreeView32` controls accumulate `WM_MOUSEWHEEL` internally and only react visually when the total reaches ±120 (a full WHEEL_DELTA). Our 1–11 unit ticks filled that accumulator slowly → silence → a 3-line jump when crossing 120.
- **Fix:** `ActiveAppResolver` detects the target HWND's class via `GetClassName`. For a legacy control (`DirectUIHWND`, `SysListView32`, `SysTreeView32`, `ListBox`), `ScrollCoordinator` **passes through** — it does not intercept the event, so native scrolling is intact.
- Normal Win32 apps (browsers, settings apps, etc.) keep smooth scrolling.

---

## 2.0.5 — 2026-05-05

### Fixed — injection strategy by focus (SendInput / PostMessage)

- **Two problems identified:**
  1. **Task Manager / modern apps in focus:** `PostMessage(WM_MOUSEWHEEL)` is not enough — modern apps (WinUI 3, DirectUI, shell controls) respond better to the real hardware input generated by `SendInput`.
  2. **Explorer "stuttering":** Explorer's `DirectUIHWND` does not accumulate sub-`WHEEL_DELTA` input received via `PostMessage`.
- **New `_cachedUseSendInput` selection rule:**
  | Scenario | Method | Reason |
  |---|---|---|
  | **Focused** window | `SendInput` | Real hardware input → correct WM_MOUSEWHEEL + WM_POINTER |
  | **Background** window | `PostMessage(hwnd)` | Bypasses "Scroll inactive windows" → direct delivery to the HWND |
  | **Elevated** process | `SendInput` (override) | UIPI blocks PostMessage from non-elevated processes |
  Focus detection: `GetAncestor(hwndTarget, GA_ROOT) == GetForegroundWindow()`.
- **`NativeMethods`:** added `GetClassName` and `GetAncestor` P/Invokes.

---

## 2.0.4 — 2026-05-05

### Fixed — smooth scrolling in elevated windows (Task Manager, regedit, …)

- **Root cause:** `PostMessage(hwnd, WM_MOUSEWHEEL)` to a window of an **elevated** (High integrity) process is silently dropped by Windows' **UIPI** (User Interface Privilege Isolation) when the sender is not elevated. SmoothMice suppressed the original scroll (the hook returns 1) but the smoothed event never arrived — so Task Manager did not scroll at all.
- **`ActiveAppResolver`:** elevation detection added to the process query: it tries `OpenProcess(PROCESS_QUERY_INFORMATION)` — if that fails (ERROR_ACCESS_DENIED / UIPI), the process is elevated.
- **`ScrollCoordinator`:** adaptive injection strategy:
  - **Non-elevated process** → `PostMessage(hwnd, WM_MOUSEWHEEL)`
  - **Elevated process** → `SendInput(MOUSEEVENTF_WHEEL)` — bypasses UIPI entirely.

---

## 2.0.3 — 2026-05-05

### Fixed — smooth scrolling in background windows (2nd attempt)

- **Root cause identified:** `SendInput(MOUSEEVENTF_WHEEL)` leaves routing to the OS. With "Scroll inactive windows when I hover over them" off, the OS delivers the event to the focused window instead of the window under the cursor.
- **`ScrollInjector`:** replaced `SendInput` with a direct `PostMessage(hwnd, WM_MOUSEWHEEL, ...)` to the target HWND. `PostMessage` bypasses OS routing completely.
- **`ScrollCoordinator`:** `_cachedHwnd` and `_cachedScreenPt` are stored in `OnMouseWheel` (hook time).
- **`PostMessage` does not loop back:** `WH_MOUSE_LL` only intercepts hardware input.

---

## 2.0.2 — 2026-05-05

### Fixed — scrolling in background windows

- **`ActiveAppResolver`:** profile resolution now uses the window under the cursor (`WindowFromPoint`) instead of the focused window (`GetForegroundWindow`).
- **`ScrollCoordinator.OnMouseWheel`:** uses `e.ScreenPoint` to find the target window via `WindowFromPoint`.

---

## 2.0.1 — 2026-05-05

### Performance — zero overhead when idle

- **On-demand timer:** the 4 ms timer and the 1 ms scheduler resolution are now enabled only when a scroll event arrives and disabled right after the animation ends. When idle: **0 Win32 calls per second**.
  - Removes the impact of a permanent `timeBeginPeriod(1)`, which affected the scheduler of every process (browsers included) and directly caused the reported FPS drop.
- **Settings cache in `TickCore`:** removes `GetForegroundWindow`, `ResolveForExecutable`, and `ProfileManager.Snapshot` from the 4 ms loop.
- **Injection outside the lock:** `SendInput` runs after releasing `_gate`, so the hook thread is not blocked.

---

## 2.0.0 — 2026-05-05

### Full refactor — new injection architecture

- **ScrollInjector:** moved from `PostMessage(WM_MOUSEWHEEL)` to `SendInput(MOUSEEVENTF_WHEEL/HWHEEL)`.
  - `SendInput` uses the OS's native routing (DWM/compositor): the window under the cursor receives the event correctly, including system overlays such as the Windows 11 **Snap Layout** panel.
  - Keyboard modifiers (Ctrl, Shift) are read from the real keyboard state by the receiving app.
- **ScrollCoordinator:** added Ctrl pass-through — while Ctrl is held, the event passes without interception. Fixes **Ctrl+scroll** (zoom in Explorer and browsers, volume adjustment, etc.).
- **MouseHookService:** events flagged `LLMHF_INJECTED` are ignored by the hook — prevents re-processing (a double-smoothing loop).

---

## 1.0.1 — 2026-05-05

### Improved — smoother scroll easing

- **SmoothScrollEngine:** replaced the step-queue model (piecewise cubic) with a **velocity-lerp model with a speed ramp**.
  - `_remaining` accumulates every event in the same direction — no overlapping steps with independent ease-in phases creating velocity "cliffs".
  - Ease-in via the `_speed` ramp (exponential towards 1.0); ease-out via natural exponential decay.
  - C¹ and C² continuous: no jerk at the inflection.

---

## 1.0.0 — 2026-04-18

### Stable milestone — first production version

- **Hook:** delegate pre-JIT before `SetWindowsHookEx` (`RuntimeHelpers.PrepareDelegate`) — removes the stutter on the first mouse event caused by JIT inside the native callback.
- **Startup:** hook installation deferred to `DispatcherPriority.Normal`.
- **UI:** auto-apply timer (300 ms, `Background` priority).
- **Publish:** `PublishReadyToRun=true`.

---

## 0.3.13 — 2026-04-18

### Critical fix — crash on open (every version ≥ 0.3.9)

- **XAML:** `ProgressBar.Value` was bound to `UpdateBannerProgress` without `Mode=OneWay` — WPF uses `BindsTwoWayByDefault` for `RangeBase.Value`, tries to write back to the read-only property, and throws an unhandled `InvalidOperationException` at startup.
  - Fixed: `Value="{Binding UpdateBannerProgress, Mode=OneWay}"`.

## 0.3.12 — 2026-04-18

### Critical startup fixes

- **Stuck cursor / app does not open:** `ActiveAppResolver` replaced `Process.MainModule.FileName` with `QueryFullProcessImageName` (native, < 1 ms).
- **HWND cache:** result cached while the foreground window does not change.
- **Timer reentrancy:** reentrancy guard with `Interlocked` in `Tick()`.

## 0.3.11 — 2026-04-18

- **Startup:** `SetWindowsHookEx(WH_MOUSE_LL)` passes `hMod = NULL` — avoids the hook failing with the apphost.

## 0.3.9 — 2026-04-18

- **OTA:** start with `/postota`, progress bar, automatic restart.
- **UI:** larger `MinHeight` and size snapping.
- **Installer:** `SetupIconFile` with `SmoothMice.ico`.

## 0.3.8 — 2026-04-18

- **Distribution:** GitHub Release with the 0.3.8 installer.

## 0.3.7 — 2026-04-18

- **UI:** settings window fixed after starting with `/tray`.
- **OTA:** install batch with wait + taskkill.

## 0.3.6 — 2026-04-18

- **Docs:** README — updated preview.

## 0.3.5 — 2026-04-18

- **UI:** Enter in numeric fields clears focus after saving.
- **In-app updates:** Inno with `CloseApplications=yes`; improved OTA batch.

## 0.3.4 — 2026-04-18

- **UI:** visual refinement and `MainWindow` structure.
- **Core / infra:** removed unused APIs; compact comments.

## 0.3.3 — 2026-04-18

- **Updates:** check against GitHub releases; silent download and install.
- **UI:** «UPDATES» section; multi-resolution icon.

## 0.3.2 — 2026-04-18

- **UI:** discreet app version in the footer; `SizeToContent` and automatic height.

## 0.3.1 — 2026-04-18

- **Publish:** `SmoothMice-{Version}.exe` executable; `build-installer.ps1` passes `MyPublishedExe`.

## 0.3.0 — 2026-04-18

- **Installer / version:** `Directory.Build.props` as the source of `<Version>`; `build-installer.ps1`.
- **Startup:** `Run` registration with `/tray`.

## 0.1.0 — 2026-04-18

- Windows (x64) utility: smoother mouse-wheel scrolling, per-application profiles, a tray icon, and JSON settings in `%AppData%\SmoothMice\settings.json`.
