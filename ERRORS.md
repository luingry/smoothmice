# Resolved errors

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
