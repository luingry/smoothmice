# Resolved errors

## 2026-09-19 — Diagnostic formatter did not build on the project's net48 target

- Symptom: `dotnet test SmoothMice.sln -c Release` initially failed with `CS0234` because `System.Text.Json` was unavailable in `SmoothMice.Core`, then surfaced `CS0173` for a nullable interval inference.
- Root cause: the project-wide target framework is .NET Framework 4.8, despite the test project targeting .NET 8.
- Solution: kept the core diagnostic formatter dependency-free, used the existing Newtonsoft.Json dependency only in Infrastructure for session metadata, and declared the optional interval explicitly as `double?`.
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
