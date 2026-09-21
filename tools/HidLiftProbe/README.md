# HID Lift Probe

`HidLiftProbe` is a narrow, read-only Windows diagnostic for the connected Razer Basilisk V3 Pro 35K (`VID_1532`, `PID_00CC` or `PID_00CD`). It enumerates the present HID collections, reports each collection's PID, top-level usage, input-report length, and detailed input button/value capabilities (report ID, usage/range, bit/data index and logical ranges), and can capture input reports from one collection.

It never invokes output or feature-report APIs and opens a capture target with `GENERIC_READ` only, using shared read/write access so it does not claim exclusive ownership. The output deliberately redacts the per-device instance segment of a HID path and does not persist it.

## Build and enumerate

```powershell
dotnet build tools/HidLiftProbe/HidLiftProbe.csproj -c Release
dotnet run --project tools/HidLiftProbe/HidLiftProbe.csproj -c Release -- enumerate
```

Each listed collection has a short-lived read-only open during enumeration. `ready(read-only shared)` means that open succeeded. An inaccessible collection remains listed with the concrete Win32 error, but its capabilities are unavailable because they cannot be inspected through a read handle. Enumeration does not read an input report. The PID is also embedded in the default capture filename and TSV metadata; `compare` warns when the PID/interface identity differs, while allowing different phase labels.

## Coordinated physical test

1. Run `enumerate`, then note every listed index with a nonzero `input_report_bytes` value. Start with mouse-like usages (usually usage page `0x0001`, usage `0x0002`).
2. Keep the mouse otherwise still. Capture a **desk** phase for each candidate index for 15 seconds:

   ```powershell
   dotnet run --project tools/HidLiftProbe/HidLiftProbe.csproj -c Release -- capture --index 0 --seconds 15 --label desk
   ```

3. Repeat with the mouse held about 2 cm above the same surface, without pressing buttons or moving the wheel. Use `--label lifted`. The default files go to `artifacts/hid-lift-probe`, which is git-ignored. `Ctrl+C` cancels a capture without touching the device.
4. Compare like-for-like files from the same interface:

   ```powershell
   dotnet run --project tools/HidLiftProbe/HidLiftProbe.csproj -c Release -- compare artifacts/hid-lift-probe/<desk>.tsv artifacts/hid-lift-probe/<lifted>.tsv
   ```

The comparison reports each report ID and byte positions whose observed sets differ between phases. A difference is a lead for further controlled repeats, not proof of a sensor-on-surface flag: normal motion, button state, battery/telemetry traffic, and timing can also change HID reports. If no reports arrive during either static phase, lightly move the mouse in an identical pattern in both phases and compare that separate pair.

## Scope limits

This only observes HID input exposed by Windows. Firmware-only lift-off state, a vendor feature report, or telemetry on an exclusive Razer interface may not be observable under the read-only constraints. The tool is diagnostic only; it does not change SmoothMice behavior.
