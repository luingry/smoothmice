# Resolved errors

## 2026-09-22 — settings.json could silently revert to defaults, losing custom app profiles

- Symptom: user reported that scroll parameters sometimes reverted to defaults and custom
  per-app profiles they had added were gone, with no clear trigger.
- Root cause (two compounding issues in `JsonSettingsRepository`,
  `src/SmoothMice.Infrastructure/Persistence/JsonSettingsRepository.cs`):
  1. `Save` wrote directly to `settings.json` via `File.WriteAllText` (truncate then write, not
     atomic). Any interruption mid-write (crash, force-kill, power loss) left invalid/truncated
     JSON. `LoadOrCreate` caught the resulting parse exception and silently returned
     `DefaultSettings.CreateAppSettings()` with no logging or recovery attempt.
  2. `Save` had no synchronization, and the app calls it from more than one thread on the same
     shared `JsonSettingsRepository` instance: the UI's `LiveApplyTimer` (every 300 ms while the
     window is visible, `MainWindow.xaml.cs`) runs on the UI thread, while
     `App.CheckForUpdatesAsync` (`src/SmoothMice.App/App.xaml.cs`) calls `_repo.Save(...)` from a
     thread-pool thread after `ConfigureAwait(false)`. Two concurrent writers to the same path can
     interleave/truncate each other's output.
  3. Because the app persists on nearly every UI interaction, the very first save after a
     corruption event immediately overwrote the corrupted file with fresh defaults — the original
     custom profiles were gone for good by the time anyone noticed.
- Solution: `Save` now writes to a `.tmp` file first, then swaps it in with
  `File.Replace(tmp, FilePath, backupPath, ignoreMetadataErrors: true)` — atomic on NTFS, and it
  also demotes the previous good file to `settings.json.bak` in the same operation. Both `Save`
  and `LoadOrCreate` take an instance-level lock so concurrent callers can no longer interleave.
  `LoadOrCreate` tries the primary file, then `settings.json.bak`, before ever falling back to
  defaults; an unreadable primary is copied aside as `settings.json.corrupt-<timestamp>` instead
  of being silently discarded, so a bad file is never destroyed without a recoverable trace.
  Regression tests in `tests/SmoothMice.Core.Tests/JsonSettingsRepositoryTests.cs` cover
  round-tripping custom profiles, recovering from a simulated torn write via the backup, the
  quarantine behavior, and 20 concurrent `Save` calls never leaving a missing/corrupt file.
- Prevention: any settings/state file that is (a) written from more than one thread and (b)
  written frequently enough that a corruption event will be overwritten again within seconds
  needs both an atomic write (temp file + platform rename/replace) AND a same-instance lock —
  neither alone is sufficient once multiple threads can call `Save` concurrently. Also: a
  `catch { return defaults; }` around deserialization is a silent, permanent data-loss trap the
  moment the caller might write that same file again soon after — always keep a last-known-good
  backup and quarantine unreadable files instead of just discarding them.

## 2026-09-22 — Main window showed much larger side margins on some first launches

- Symptom: on some app launches (not all, and typically the very first open), the window
  appeared with noticeably larger empty space on both sides of the fixed-width content than
  normal; closing and reopening the app always fixed it.
- Root cause: `MainWindow.xaml.cs` had two independent code paths that could each measure the
  window and freeze its size — `MainWindow_OnContentRendered` called
  `SnapClientSizeToDevicePixels()` directly and immediately, while `MainWindow_OnLoaded` (via
  `RequestSnapToContentAfterLayout`) scheduled the same method at `DispatcherPriority.Loaded`.
  Whichever ran first read `ActualWidth`/`ActualHeight` and immediately flipped
  `SizeToContent` to `Manual`, permanently locking in whatever was measured — with no check that
  layout had actually settled. On a cold first paint (assembly JIT, style/resource dictionary
  resolution, font substitution all still in flight), that first reading could be a transient,
  not-yet-final measurement; once `SizeToContent` was `Manual`, the window would never re-fit to
  the correct content size. A second launch (warm JIT/disk caches) settled fast enough that both
  paths read the same, correct value, masking the bug.
- Solution: `MainWindow_OnContentRendered` now routes through the same
  `RequestSnapToContentAfterLayout` entry point instead of calling the snap directly, so there is
  one pipeline instead of two racing callers. `SnapClientSizeToDevicePixels` now also requires the
  same width/height reading twice in a row, one `DispatcherPriority.ApplicationIdle` turn apart,
  before freezing the size — reusing the existing `_snapRetryRemaining` retry budget as the safety
  net against never stabilizing.
- Prevention: never let two independently-triggered handlers both "measure once and freeze"
  the same layout-dependent state; route them through one shared, idempotent entry point, and
  require a stable (repeated) reading before committing to any size/position that becomes
  permanent — especially on a first-ever cold paint in the process, where JIT/resource-loading
  timing is not deterministic across launches.

## 2026-09-21 — Smoothing was structurally inconsistent (jump on resume, weak continuous scroll); replaced the shared-ramp engine with an independent pulse-queue model

- Symptom: three prior tuning fixes to the same engine (see the two entries below) failed to
  resolve the reported feel: scrolling again while the previous animation was still finishing
  produced a visible jump ("salto"); constant/continuous scrolling felt like each notch delivered
  much less than it should; slow, paced scrolling (a few hundred ms between notches) also showed
  visible jumps; setting `AnimationTimeMs` to 1 ms made all symptoms disappear (animations then
  always finished before the next notch could land mid-animation).
- Root cause (architectural, not a tuning bug): `SmoothScrollEngine` kept ONE shared state for
  all motion — a scalar `_remaining` (signed units left to emit) plus a persistent `_speed` ramp
  factor `[0, 1]` that only ever increased within a gesture. Each tick emitted
  `_remaining * lerp * _speed`. Because `_speed` was shared and persistent, what a NEW physical
  notch produced depended entirely on WHEN it landed relative to the previous animation's ramp
  state — a nonlinear, history-dependent system. Measured example: an identical notch landing on
  a nearly-spent tail emitted ~52 units on its first tick vs. ~13 for the same notch landing on a
  quiet engine, a ~4x spike depending purely on timing. Every attempted patch (resetting `_speed`,
  proportional blending, "negligible tail" thresholds — see the two entries below) just moved the
  inconsistency to a different trigger condition instead of removing it. Separately, `Tick`
  ignored its `nowMs` parameter and assumed a fixed 4 ms cadence, so a late or reentrancy-dropped
  timer callback (`ScrollCoordinator.Tick`'s guard skips a tick if the previous one is still
  running) silently lost animation progress instead of catching up.
- Solution: replaced the engine with a pulse-queue (superposition) model, ported from the
  algorithm of Balazs Galambosi's MIT-licensed SmoothScroll (smoothscroll.js), whose easing curve
  is credited to Michael Herf ("Stopping", stereopsis.com) — reimplemented from the public
  algorithm description, not decompiled from any binary. Every physical wheel notch becomes its
  own independent queue item with its own start timestamp and total distance; each tick, every
  item computes its progress purely from real elapsed time (`SmoothScrollEngine.Tick` now
  actually uses `nowMs`) and their contributions are SUMMED. This is linear and time-invariant:
  every notch always delivers exactly its full distance over exactly `AnimationTimeMs`, regardless
  of what else is animating (no more jump-on-resume), overlapping notches simply add up (no more
  "weak" continuous scroll), and a late/dropped tick self-corrects on the next tick since progress
  is time-based, not tick-count-based. There is deliberately NO "merge", "nudge", "reset speed", or
  "negligible tail" heuristic anywhere in the new engine — also reverted the matching
  `ScrollCoordinator.OnMouseWheel` patch (`wasEffectivelyQuiet` / `IsNegligibleTailRelativeTo`) that
  existed only to compensate for the old model. Also raised `ScrollMath.AccelerationMultiplier`'s
  floor from 0.10 to 1.0 (SmoothScroll never decelerates below the physical notch size, it only
  ever multiplies up) since the old floor was shrinking slower, evenly-paced notches to as little
  as 10% of their distance — a direct, independent cause of "paced scrolling feels weak".
- Prevention: a model whose per-event output depends on shared, persistent, monotonically-evolving
  state (a ramp/ease-in factor that is reused and only ever raised across merged inputs) cannot
  give per-event consistency — any fix to it is necessarily a threshold or heuristic that trades
  one triggering condition for another, because the underlying system is nonlinear and
  history-dependent. When an event-driven animation needs "every event behaves the same regardless
  of timing," model each event as an independent, time-driven curve and sum contributions
  (superposition) instead of mutating one shared accumulator/ramp pair — linearity is what makes
  consistency structural rather than tuned. Also: when a fix requires repeated threshold-tuning
  rounds against the same live report, treat that as a signal to question the model's shape, not
  the threshold's value.

## 2026-09-20 — Free-Spin Suppress still entered the smoothing pipeline

- Symptom: a detector decision set `MouseWheelHookEventArgs.Handled`, but the same physical wheel could still be queued and reinjected by `ScrollCoordinator`.
- Root cause: .NET multicast events continue invoking later subscribers after an earlier subscriber sets a mutable event argument; `ScrollCoordinator.OnMouseWheel` did not test `Handled` at entry.
- Solution: return before target resolution, profile lookup, queuing, or injection when the event is already handled; added a regression invoking the coordinator seam with a pre-handled wheel and asserting its smoothing engine remains quiet.
- Prevention: every subscriber after a policy/guard subscriber must treat mutable `Handled` input as an entry precondition, not merely as a final hook-callback concern.

## 2026-09-20 — Free-Spin calibration could overstate evidence or reuse stale context

- Symptom: an initial detector implementation could carry a higher Jeffreys bound from a looser cutoff into a stricter cutoff, declare readiness without enough independent legitimate captures, and retain recent movement/wheel context across a policy transition.
- Root cause: prefix-max calibration is optimistic for distinct cutoff subsets; readiness counted only inertia groups; history and overlapping asynchronous reloads had no explicit transition/generation guard.
- Solution: use suffix-min monotonic correction over exact empirical OOF score cutoffs, require five independent legitimate capture groups, keep phase-specific support, reset causal history under the policy lock, and allow only the most recent reload generation to publish its immutable model.
- Prevention: calibration safety gates must be independently evidenced at every cutoff, and every policy/reload transition must invalidate or version state that can influence the hook.

## 2026-09-19 — Diagnostic formatter did not build on the project's net48 target

- Symptom: `dotnet test SmoothMice.sln -c Release` initially failed with `CS0234` because `System.Text.Json` was unavailable in `SmoothMice.Core`, then surfaced `CS0173` for a nullable interval inference.
- Root cause: the project-wide target framework is .NET Framework 4.8, despite the test project targeting .NET 8.
- Solution: kept the core diagnostic formatter dependency-free, used the existing Newtonsoft.Json dependency only in Infrastructure for session metadata, declared the optional interval explicitly as `double?`, and used a local clamp helper instead of the unavailable `Math.Clamp` API in the scroll engine.
- Prevention: check `Directory.Build.props` before adding framework APIs to shared projects; avoid assuming the test target framework is the production target.

## 2026-09-19 — Existing scroll-engine tests were out of sync with its API

- Symptom: the same test command then failed with `CS7036` because three existing test calls omitted `nowMs` from `SmoothScrollEngine.PushPhysicalDelta`.
- Root cause: the engine API has a required compatibility parameter while its tests still used the older call shape.
- Solution: supplied deterministic `nowMs: 0` values; the engine documents that it does not use this parameter.
- Prevention: update call sites when changing a required public method signature, even for compatibility-only parameters.

## 2026-09-19 — Bounded channel API was unavailable in the production target

- Symptom: the full solution build failed because `System.Threading.Channels` is not referenced by the .NET Framework 4.8 Infrastructure project.
- Root cause: the asynchronous queue design assumed a modern BCL API that is not part of this project target.
- Solution: used a bounded `BlockingCollection` over `ConcurrentQueue`; hook enqueue remains nonblocking via `TryAdd`, while file I/O remains on its background task.
- Prevention: validate the full production solution build after adding runtime/concurrency APIs; unit-test compilation alone may only build the Core dependency graph.

## 2026-09-20 — Global game-bypass checkbox made the WPF window fail to compile

- Symptom: `dotnet build SmoothMice.sln -c Release` failed with `MC3089`, reporting that the startup settings `Border` had more than one child.
- Root cause: the new global checkbox was inserted beside the existing checkbox directly inside a WPF `Border`, which supports exactly one child element.
- Solution: wrapped both related controls in a `StackPanel` inside the existing `Border`.
- Prevention: when adding controls to a WPF content control, preserve its one-child rule and introduce an explicit layout panel for sibling elements.

## 2026-09-20 — Foreground FreeSpin scrolling could stall the low-level mouse hook

- Symptom: extremely fast FreeSpin scrolling caused system beeps and brief stalls only while Chrome was foreground.
- Root cause: the foreground route uses `SendInput`; the 4 ms animation tick held the same coordinator lock while invoking it, so the `WH_MOUSE_LL` callback could queue behind native injection during a burst. Pending smooth motion was also unbounded, allowing a pathological burst to create oversized delayed deltas.
- Solution: release the coordinator lock before either injection route, retain the session-generation cancellation check, and bound queued and per-tick wheel units in the smooth-scroll engine.
- Prevention: never perform native injection while holding a lock acquired by a low-level input callback; cap queue depth and per-tick output at the input-to-render boundary.

## 2026-09-20 — Free-Spin window crashed while parsing read-only phase bindings

- Symptom: opening Free-Spin raised a `XamlParseException` wrapping `MS.Internal.Data.PropertyPathWorker.CheckReadOnly` / `InvalidOperationException` in the window constructor.
- Root cause: each phase `RadioButton.IsChecked` binding inherited the target property's default `TwoWay` mode while its `Is*Selected` source property is intentionally getter-only.
- Solution: explicitly set those four presentation-state bindings to `Mode=OneWay` and added an STA constructor regression test.
- Prevention: explicitly declare `OneWay` for WPF bindings whose source is computed/read-only, especially on selector and toggle properties with TwoWay defaults.

## 2026-09-20 — Game bypass did not recognize the captured Techland game window and the installed preference was false

- Symptom: with “Não ativar em jogos” reportedly enabled, Dying Light: The Beast exposed root class `techland_game_class` while foreground and fullscreen/borderless, but smoothing continued; the installed `settings.json` recorded `doNotActivateInGames: false`.
- Root cause: `techland_game_class` was absent from the classifier’s strong signals. Separately, the recorded false preference is confirmed, and the prior ViewModel setter is confirmed not to invoke its persistence callback; the exact WPF event ordering or user-flow cause of that installed false value was not reproduced and is not asserted.
- Solution: accept the specific Techland root class only with the existing foreground-or-fullscreen signal and all exclusions; persist directly from the ViewModel setter after updating `ProfileManager`, instead of relying on `Checked`/`Unchecked` ordering.
- Prevention: capture real root window classes when extending conservative classifiers, and make global settings that must survive an immediate UI interaction invoke their explicit persistence seam rather than depend solely on routed UI events.

## 2026-09-20 — Free-Spin window could not construct in isolated WPF tests after theme styling

- Symptom: the Free-Spin STA construction test threw `XamlParseException` because `SecondaryButton` could not be found.
- Root cause: the window-level `ActionButton` used `BasedOn="{StaticResource SecondaryButton}"`, while the isolated test creates `App` without loading application XAML resources.
- Solution: the window owns the small themed action-button template and references only dynamic semantic brushes.
- Prevention: window-local styles used by isolated WPF construction tests must not require an application resource to resolve statically.

## 2026-09-21 — Scrolling into a still-finishing animation produced a bigger jump (RESOLVED, took 2 fixes)

- UPDATE: the first fix below (engine-side `_speed` reset) was NOT sufficient on its own — user
  reported "continua a mesma coisa" (still the same) after installing it. A second, independent
  mechanism in `ScrollCoordinator` was inflating the same scenario; see the second entry after this
  one for the full picture. Read both entries together.

- Symptom: scrolling again while the previous scroll's animation was still finishing (its "tail")
  produced a visibly bigger jump than an isolated scroll. Reducing `AnimationTimeMs` to 1 ms made
  the symptom disappear (animations finish before the next physical notch can ever land mid-tail).
  This was initially misreported/misdiagnosed as "first scroll bigger than subsequent ones" — that
  framing was a symptom of the user's test pattern (a quick overlapping second scroll looking
  bigger than later, fully-isolated ones), not the actual trigger condition. The user's own
  isolation test (disabling AnimationEasing had no effect; disabling SmoothMice entirely removed
  the symptom; reducing AnimationTimeMs to 1 ms removed it too) pinpointed the real trigger.
- Two earlier attempts before this one did NOT fix it and are kept below for context:
  1. Reset the acceleration EWMA when the engine goes quiet (`ScrollMath.UpdateSmoothedInterval`,
     `ScrollCoordinator.UpdateEwma`) — a real fix for a different inconsistency (paced notches
     under the 1500 ms EWMA reset timeout), but not what the user was hitting.
  2. Hypothesized hardware/driver or `AnimationEasing`-off causes — both ruled out by the user
     directly (disabling SmoothMice removes the symptom, so it's not hardware; AnimationEasing was
     confirmed ON).
- Root cause (confirmed): `SmoothScrollEngine.PushPhysicalDelta` merges a new physical push into
  `_remaining` whenever the sign matches the existing motion, but only ever *raises* `_speed`
  (the ease-in ramp, a persistent [0,1] value that only increases within a gesture and is reset to
  0 exactly when the animation fully flushes). Near the end of an animation, `_remaining` has
  decayed to a tiny tail but `_speed` is still near 1.0 (it had already ramped all the way up
  earlier in that same gesture). If a new, full-size physical push lands while the engine is in
  that state (`_remaining != 0`, i.e. not yet `IsQuiet()`), the new units get added on top of the
  tiny leftover `_remaining`, but the already-ramped `_speed` is reused unchanged. On the very next
  tick, `delta = _remaining * lerp * _speed` multiplies the freshly-added, full-size push by a
  speed near 1.0 instead of easing it in from 0 — an oversized single-tick jump.
- Fix: in `SmoothScrollEngine.PushPhysicalDelta` (`src/SmoothMice.Core/Scrolling/
  SmoothScrollEngine.cs`), when merging same-direction motion, reset `_speed` to 0 whenever the
  leftover `_remaining` is under 10% of the incoming push's magnitude (a nearly-spent tail being
  topped up by a full-size new push — treat it as that new push's own gesture). Left the existing
  "nudge speed to ≥0.3 when merging while still meaningfully in motion" branch alone for legitimate
  continuous/rapid scrolling, where the incoming push is comparable to or smaller than the
  remaining — that case still benefits from staying responsive instead of re-easing every notch.
- Regression test: `Engine_scrolling_into_a_still_finishing_tail_does_not_spike` in
  `tests/SmoothMice.Core.Tests/ScrollMathTests.cs` — pushes a notch, runs it into its tail, pushes
  a second identical notch on top of the tail, and asserts the tick right after the merge stays
  close to a fresh gesture's first tick. Verified this test fails without the fix (52 vs. an
  expected ≤26, i.e. a ~4x spike) and passes with it.
- Prevention: any ease-in/ramp state that persists across merged inputs within one continuous
  motion model must be re-evaluated (not just monotonically preserved) whenever new input arrives
  that's disproportionate to the leftover state it would otherwise inherit — a ramp value earned by
  a nearly-finished small motion is not a valid ramp value for a fresh, much larger one. Also: when
  a live report contradicts a fix, get the user's own isolation results (what changes/doesn't
  change the symptom) before hypothesizing further — their AnimationTimeMs experiment pointed
  straight at the actual mechanism.

## 2026-09-21 — Same bug, second mechanism: acceleration EWMA also stayed inflated mid-tail

- Symptom: after the engine-side `_speed` fix above, the user reported the exact same behavior
  persisting ("continua a mesma coisa"). They asked, reasonably, why not just always start fresh
  instead of trying to merge with whatever's running — which is close to what was still missing.
- Root cause: `ScrollCoordinator.OnMouseWheel` computes `wasQuiet = _vertical.IsQuiet() &&
  _horizontal.IsQuiet()` (strict, `< 0.1` units) and only resets the acceleration EWMA
  (`_smoothedIntervalMs`) to the neutral reference when `wasQuiet` is true. But a scroll resuming
  mid-tail is, by construction, NOT `IsQuiet()` (there's still a nonzero — if tiny — `_remaining`).
  So even after the engine-side ramp fix, `UpdateEwma` kept computing from the short real
  wall-clock interval since the tail's own last physical event, instead of resetting — and a short
  interval against the 400 ms reference produces an inflated multiplier (worked example in the
  regression test below: ≈2.24× instead of neutral 1.0× for an 80 ms gap). That inflated
  accel-derived `units` value is what `PushPhysicalDelta` receives — the ramp fix alone had nothing
  inflated left to protect against by the time it saw the push.
- Fix: added `SmoothScrollEngine.IsNegligibleTailRelativeTo(incomingMagnitude)` — true when leftover
  `_remaining` is under 10% of an incoming push's base (pre-acceleration) size, i.e. a nearly-spent
  tail rather than motion still meaningfully in flight (same 10% concept the engine's own `_speed`
  reset already used — extracted to a shared, named method instead of a duplicated magic number).
  `ScrollCoordinator.OnMouseWheel` now computes this per-axis using the raw `rawDelta *
  StepScale(StepSizePx)` (no accel — avoids a chicken-and-egg dependency, since accel is what this
  decision feeds into) and ORs it into the EWMA-reset signal: `wasEffectivelyQuiet = wasQuiet ||
  engine.IsNegligibleTailRelativeTo(baseUnits)`. Timer arming still uses the strict `wasQuiet` —
  unrelated concern, the timer is already running when the tail is still ticking.
- Regression test:
  `CoordinatorSequence_scroll_into_a_still_finishing_tail_stays_neutral_even_with_a_short_real_interval`
  in `ScrollMathTests.cs` — replays `ScrollCoordinator`'s own push+EWMA sequence in pure code
  (engine isn't Win32-dependent, but `ScrollCoordinator` itself is, so this simulates its exact
  decision order rather than testing it directly): pushes a notch, runs the engine into a
  not-fully-quiet tail, resumes 80 ms later, and asserts the resulting multiplier is exactly 1.0
  instead of the ≈2.24× the old, `wasQuiet`-only logic would have produced.
- Why the "just always start fresh" question was right in spirit: the fix does exactly that for
  the specific case that matters (a negligible tail), while still letting genuinely fast continuous
  scrolling (where leftover motion is NOT negligible relative to the next push) accumulate and
  accelerate as designed — a truly unconditional reset on every event would have reintroduced the
  "overlapping step curves" jerkiness this engine's `_remaining`-accumulation design was originally
  built to avoid (see the class-level docstring in `SmoothScrollEngine.cs`).
- Prevention: when a state machine exposes an "am I settled?" signal (`IsQuiet()`) that only
  transitions at a strict cutoff, every OTHER piece of state that logically depends on "did the
  previous gesture actually end" (here: two independent things — the engine's own ramp, and a
  completely separate EWMA living one layer up in `ScrollCoordinator`) needs to agree on the SAME
  underlying signal. Fixing one consumer of a "was this a fresh start" decision without auditing
  every other consumer of the same decision leaves the bug half-fixed and looks, from the outside,
  exactly like "the fix didn't work."

## 2026-09-21 — Third mechanism: the 10% hard cutoff itself was miscalibrated, dropped medium-speed responsiveness

- Symptom: after fix #2 above, user reported improvement but a NEW regression: "sinto que os
  pulsos intermediários não são enviados... 1 a cada dois ticks refletem na rolagem" (every other
  pulse doesn't seem to register), while the physical-pulse logger showed every pulse captured
  correctly — i.e. the regression was on the injected/smoothed output side, not input capture.
- Root cause: a probe (`Probe_decay_curve`, temporary, not committed) against the exact default
  profile (`StepSizePx=80, AnimationTimeMs=150, TailToHeadRatio=3`) showed
  `IsNegligibleTailRelativeTo(480 units)` — the 10% cutoff added for fix #1 — becomes true at only
  ~t=100ms into a single notch's ~336ms total animation lifetime, and stays true for the remaining
  ~70% of it. In other words: ANY notch-to-notch gap beyond ~100ms (which covers virtually all
  normal, deliberate, "medium speed" scrolling — only genuinely rapid FreeSpin-style bursts stay
  under 100ms between notches) tripped the same hard reset (`_speed = 0`) that fix #1 introduced
  specifically for the pathological "stale tail" case. Every ordinary notch was being forced
  through a full ease-in restart instead of blending into the ongoing motion.
- Fix: replaced the hard `IsNegligibleTailRelativeTo` cutoff inside `SmoothScrollEngine
  .PushPhysicalDelta`'s merge branch with proportional blending:
  `_speed *= Math.Abs(_remaining) / (Math.Abs(_remaining) + Math.Abs(units))`, then still floor to
  ≥0.3 for responsiveness. This has no cliff: a push landing while `_remaining` is still
  substantial (genuine sustained FreeSpin burst, or rapid successive notches) barely touches
  `_speed`, preserving the "ramp to and cruise at full speed" behavior the engine was designed for;
  a push landing on a truly negligible tail still gets scaled toward the 0.3 floor, avoiding the
  original spike. `IsNegligibleTailRelativeTo` is kept, but only for the EWMA/accel decision in
  `ScrollCoordinator` (a one-shot neutral-vs-inflated choice, where the same broad cutoff is much
  less consequential than it is for a ramp value that then multiplies every subsequent tick).
- Verification: manually compared "threshold" vs "proportional" merge strategies across notch
  cadences from 60ms–300ms via a temporary probe (not committed) — proportional blending keeps
  FreeSpin-style rapid bursts (<100ms gaps) at high speed while still suppressing the original
  spike scenario. Added `Engine_moderate_paced_scrolling_does_not_drop_notch_output` as a permanent
  regression test: 6 notches at a realistic 150ms cadence must emit ≥95% of the pushed total.
  IMPORTANT CAVEAT: this total-conservation test does NOT actually discriminate between the
  reverted hard-cutoff approach and the new proportional one — both conserve the total once given
  enough ticks to catch up (only immediate, tick-by-tick responsiveness differs, which a
  total-sum assertion can't see). Treat it as a baseline sanity check, not proof the perceptual
  regression is fixed.
- Also added: `ScrollCoordinator.GetTickDiagnostics()` (`_diagTicksScheduled` /
  `_diagTicksSkippedReentrancy`, both `Interlocked`-updated) exposing how often the 4ms tick
  timer's reentrancy guard (`Tick()`, guards against SendInput/PostMessage taking >4ms) skips a
  scheduled callback outright — a DIFFERENT possible explanation for "every other pulse does
  nothing" that the engine-math fix above does not address at all. Surfaced live in the "Scroll
  logs" window (`ScrollPulseMonitorWindow`, now also takes a `ScrollCoordinator` reference) as
  "N ticks scheduled, M skipped (previous tick still running)". NOT YET CONFIRMED OR RULED OUT —
  next step is to have the user reproduce the medium-speed scroll with this window open and check
  whether the skipped count climbs meaningfully during the symptom.
- Prevention: a magnitude-ratio or time-based "was this effectively a fresh start" cutoff is only
  as good as its calibration against REALISTIC cadences for the feature in question — derive the
  actual decay curve (or equivalent) for default settings and check where a proposed cutoff falls
  on it before trusting it, rather than picking a round number (10%) that sounds conservative.
  Also: when a fix for one symptom (a spike) risks suppressing responsiveness broadly, verify
  against a *realistic sustained cadence*, not just the isolated pathological case being fixed —
  the FreeSpin burst test already in the suite would NOT have caught this, because it uses
  `AnimationEasing=false` (a different code path) and a single unbroken burst (no repeated merges
  at a human cadence).

## 2026-09-21 — Scroll-log tooltip showed the row's class name instead of its data

- Symptom: hovering/inspecting a row in the "Scroll logs" (`ScrollPulseMonitorWindow`) showed
  literally `SmoothMice.App.ViewModels.ScrollPulseMonitorRow` instead of the row's per-column
  values.
- Root cause: `ScrollPulseMonitorRow` never overrode `object.ToString()`. The `GridView` columns
  use `DisplayMemberBinding` per column (correct for the grid itself), but nothing set an
  explicit `AutomationProperties.Name`/tooltip source for the row as a whole, so WPF's default
  fallback (`ToString()` of the bound item) surfaced the bare type name.
- Solution: added a `ToString()` override on `ScrollPulseMonitorRow` that formats all of its
  columns into one line, and bound `AutomationProperties.Name` on the `ListViewItem` style to
  `{Binding}` so it uses that formatted text instead of falling back to the type name.
- Prevention: any presentation row type exposed to WPF's default automation/tooltip fallback
  needs its own `ToString()` — don't rely on individual column bindings to cover the "whole item"
  fallback case.

## 2026-09-21 — Scroll-log GridView columns had no visible divider, and no pixel value

- Symptom (follow-up to the ToString() fix above): the "Scroll logs" GridView columns had no
  visible separator between adjacent cell values (only the header row draws separators by
  default), and there was no column showing the pixel-equivalent size of each physical event —
  only the raw wheel-unit delta (`PULSE`).
- Solution: converted every `GridViewColumn` from `DisplayMemberBinding` to a `CellTemplate`
  wrapping the text in a `Border` with a right-side divider (`MonitorCell` style in
  `ScrollPulseMonitorWindow.xaml`), and added a `PIXELS` column showing
  `pulse.Delta * ScrollMath.StepScale(activeGlobalProfile.StepSizePx)` — the base,
  pre-acceleration pixel equivalent of the raw pulse. `ScrollPulseMonitorWindow` now takes a
  `ProfileManager` (passed from `App.xaml.cs`) to read the active global profile's `StepSizePx`
  once per drain batch (not per pulse, since `ProfileManager.Snapshot()` clones the whole
  settings tree).
- Prevention: `PIXELS` here is deliberately the *base* scale only (no acceleration) — acceleration
  is animated across many ticks and isn't a stable per-event number, so don't try to show the
  "actual injected" amount per raw pulse without also exposing which ticks it was spread across.

## 2026-09-20 — Dynamic theme brushes cannot be mutated in place

- Symptom: the semantic palette test threw `InvalidOperationException` when `ApplyTheme` assigned `SolidColorBrush.Color` after the brush was placed in the WPF resource dictionary.
- Root cause: WPF can freeze resource-backed `Freezable` brushes, making their color immutable even when a dynamic resource originally created them.
- Solution: replace each semantic brush resource with a new brush and keep every theme-sensitive consumer on `DynamicResource`, which propagates the replacement to open windows.
- Prevention: never rely on mutating resource-backed WPF brushes for live theming; replace resources and use dynamic lookup instead.
