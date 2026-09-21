using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmoothMice.Tools.HidLiftProbe;

internal static class Program
{
    private const ushort RazerVendorId = 0x1532;
    private static readonly ushort[] BasiliskV3Pro35KProductIds = [0x00CC, 0x00CD];

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return args.Length == 0 || IsHelp(args[0])
                ? PrintUsage()
                : args[0].ToLowerInvariant() switch
                {
                    "enumerate" => Enumerate(),
                    "capture" => await CaptureAsync(args[1..]),
                    "compare" => Compare(args[1..]),
                    _ => PrintUsage($"Unknown command: {args[0]}")
                };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Capture cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "/?";

    private static int PrintUsage(string? error = null)
    {
        if (error is not null)
        {
            Console.Error.WriteLine(error);
        }

        Console.WriteLine("HidLiftProbe (read-only HID diagnostic; no HID writes or feature/output reports)");
        Console.WriteLine("  enumerate");
        Console.WriteLine("  capture --index <n> [--seconds <1..600>] [--label <phase>] [--out <file>]");
        Console.WriteLine("  compare <capture-a.tsv> <capture-b.tsv>");
        Console.WriteLine();
        Console.WriteLine("Run enumerate first. It lists only connected HID interfaces for VID_1532 and PID_00CC or PID_00CD.");
        Console.WriteLine("capture opens exactly one listed interface with GENERIC_READ and shared read/write access.");
        Console.WriteLine("The default output is artifacts\\hid-lift-probe (git-ignored). Ctrl+C cancels cleanly.");
        return error is null ? 0 : 1;
    }

    private static int Enumerate()
    {
        var interfaces = HidInterface.EnumerateTarget();
        Console.WriteLine($"Found {interfaces.Count} present HID interface(s) for VID_{RazerVendorId:X4} and PID_00CC/00CD.");
        foreach (var item in interfaces)
        {
            Console.WriteLine($"[{item.Index}] pid=0x{item.ProductId:X4} path={item.RedactedPath}");
            Console.WriteLine($"    usage_page=0x{item.UsagePage:X4} usage=0x{item.Usage:X4} input_report_bytes={item.InputReportLength}");
            Console.WriteLine($"    caps={item.CapsStatus}; read_open={item.ReadProbe}");
            if (item.CapsStatus.StartsWith("ok", StringComparison.Ordinal))
            {
                PrintInputCapabilities("button", item.InputButtonCaps);
                PrintInputCapabilities("value", item.InputValueCaps);
            }
        }

        return 0;
    }

    private static void PrintInputCapabilities(string kind, IReadOnlyList<HidInputCapability> capabilities)
    {
        Console.WriteLine($"    input_{kind}_caps={capabilities.Count}");
        if (capabilities.Count == 0)
        {
            return;
        }

        foreach (var capability in capabilities)
        {
            Console.WriteLine($"      {capability.Description}");
        }
    }

    private static async Task<int> CaptureAsync(string[] args)
    {
        var options = CaptureOptions.Parse(args);
        var interfaces = HidInterface.EnumerateTarget();
        var target = interfaces.SingleOrDefault(item => item.Index == options.Index)
            ?? throw new ArgumentException($"No currently connected target interface has index {options.Index}. Run enumerate again.");

        if (target.InputReportLength == 0)
        {
            throw new InvalidOperationException($"Interface {target.Index} reports input_report_bytes=0; it cannot be captured.");
        }

        var outputPath = options.OutputPath ?? DefaultCapturePath(target.Index, target.ProductId, options.Label);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        Console.WriteLine($"Capturing index {target.Index}, pid=0x{target.ProductId:X4}: {target.RedactedPath}");
        Console.WriteLine($"usage_page=0x{target.UsagePage:X4} usage=0x{target.Usage:X4} input_report_bytes={target.InputReportLength}");
        Console.WriteLine($"Duration: {options.Duration.TotalSeconds:0.###} s. Output: {outputPath}");

        using var cancellation = new CancellationTokenSource(options.Duration);
        Console.CancelKeyPress += CancelCapture;
        try
        {
            await using var writer = new StreamWriter(new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            await writer.WriteLineAsync("# HidLiftProbe capture v1; reports are input only; path intentionally redacted");
            await writer.WriteLineAsync($"# vid=0x{RazerVendorId:X4}\tpid=0x{target.ProductId:X4}\tindex={target.Index}\tusage_page=0x{target.UsagePage:X4}\tusage=0x{target.Usage:X4}\tinput_report_bytes={target.InputReportLength}\tlabel={SanitizeMetadata(options.Label)}");
            await writer.WriteLineAsync("utc\telapsed_ms\treport_id\tbytes_hex");

            await using var stream = target.OpenReadStream();
            var report = new byte[target.InputReportLength];
            var stopwatch = Stopwatch.StartNew();
            var reportCount = 0;
            while (!cancellation.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(report.AsMemory(), cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }
                if (read == 0)
                {
                    throw new IOException("The HID interface returned end-of-stream.");
                }

                var timestamp = DateTimeOffset.UtcNow;
                var bytes = report.AsSpan(0, read).ToArray();
                var reportId = bytes[0];
                await writer.WriteLineAsync($"{timestamp:O}\t{stopwatch.Elapsed.TotalMilliseconds:F3}\t0x{reportId:X2}\t{Convert.ToHexString(bytes)}");
                reportCount++;
            }

            await writer.FlushAsync(CancellationToken.None);
            Console.WriteLine($"Captured {reportCount} input report(s). No HID writes were performed.");
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= CancelCapture;
        }

        void CancelCapture(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }
    }

    private static int Compare(string[] args)
    {
        if (args.Length != 2)
        {
            return PrintUsage("compare requires exactly two capture files.");
        }

        var first = CaptureSummary.Load(args[0]);
        var second = CaptureSummary.Load(args[1]);
        Console.WriteLine($"A: {first.Path} ({first.ReportCount} report(s); {first.Metadata})");
        Console.WriteLine($"B: {second.Path} ({second.ReportCount} report(s); {second.Metadata})");
        if (!string.Equals(first.Identity, second.Identity, StringComparison.Ordinal))
        {
            Console.WriteLine("WARNING: capture PID/interface identity differs; compare like-for-like phases before treating byte differences as sensor-state evidence.");
        }
        Console.WriteLine("Per report ID, changed byte positions are shown as byte_index: A-distinct-values -> B-distinct-values.");

        foreach (var reportId in first.ReportIds.Union(second.ReportIds).Order())
        {
            Console.WriteLine($"report_id=0x{reportId:X2}");
            var changes = ZipLongest(first.DistinctBytes(reportId), second.DistinctBytes(reportId), (left, right) => (left, right))
                .Select((pair, index) => (pair, index))
                .Where(entry => !entry.pair.left.SetEquals(entry.pair.right))
                .ToArray();

            Console.WriteLine(changes.Length == 0
                ? "  no distinct-byte differences observed"
                : string.Join(Environment.NewLine, changes.Select(change =>
                    $"  byte[{change.index}]: {FormatValues(change.pair.left)} -> {FormatValues(change.pair.right)}")));
        }

        return 0;
    }

    private static string FormatValues(ISet<byte> values) => values.Count == 0
        ? "(none)"
        : string.Join(',', values.Order().Select(value => $"{value:X2}"));

    private static string DefaultCapturePath(int index, ushort productId, string label)
    {
        var suffix = string.IsNullOrWhiteSpace(label) ? "capture" : SanitizeFileName(label);
        return Path.GetFullPath(Path.Combine("artifacts", "hid-lift-probe", $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-pid-{productId:X4}-{suffix}-interface-{index}.tsv"));
    }

    private static string SanitizeMetadata(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "capture" : safe;
    }

    private sealed record CaptureOptions(int Index, TimeSpan Duration, string Label, string? OutputPath)
    {
        public static CaptureOptions Parse(string[] args)
        {
            int? index = null;
            var duration = TimeSpan.FromSeconds(15);
            var label = "";
            string? outputPath = null;

            for (var position = 0; position < args.Length; position += 2)
            {
                if (position + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {args[position]}.");
                }

                var value = args[position + 1];
                switch (args[position])
                {
                    case "--index" when int.TryParse(value, out var parsedIndex) && parsedIndex >= 0:
                        index = parsedIndex;
                        break;
                    case "--seconds" when double.TryParse(value, out var seconds) && seconds is >= 1 and <= 600:
                        duration = TimeSpan.FromSeconds(seconds);
                        break;
                    case "--label":
                        label = value;
                        break;
                    case "--out":
                        outputPath = Path.GetFullPath(value);
                        break;
                    default:
                        throw new ArgumentException($"Invalid option or value: {args[position]} {value}");
                }
            }

            return new CaptureOptions(index ?? throw new ArgumentException("capture requires --index <n>."), duration, label, outputPath);
        }
    }

    private sealed class CaptureSummary
    {
        private readonly Dictionary<byte, List<byte[]>> _reports = [];

        private CaptureSummary(string path) => Path = System.IO.Path.GetFullPath(path);

        public string Path { get; }
        public string Metadata { get; private set; } = "metadata-unavailable";
        public string Identity { get; private set; } = "metadata-unavailable";
        public int ReportCount => _reports.Values.Sum(reports => reports.Count);
        public IEnumerable<byte> ReportIds => _reports.Keys;

        public static CaptureSummary Load(string path)
        {
            var summary = new CaptureSummary(path);
            foreach (var line in File.ReadLines(summary.Path))
            {
                if (line.StartsWith("# vid=", StringComparison.Ordinal))
                {
                    summary.Metadata = line[2..];
                    summary.Identity = string.Join('\t', summary.Metadata.Split('\t').Where(field => !field.StartsWith("label=", StringComparison.Ordinal)));
                    continue;
                }

                if (line.StartsWith('#') || line.StartsWith("utc\t", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var fields = line.Split('\t');
                if (fields.Length != 4 || !fields[2].StartsWith("0x", StringComparison.Ordinal) || !byte.TryParse(fields[2][2..], System.Globalization.NumberStyles.HexNumber, null, out var reportId))
                {
                    throw new InvalidDataException($"Invalid capture row in {summary.Path}: {line}");
                }

                var bytes = Convert.FromHexString(fields[3]);
                if (bytes.Length == 0 || bytes[0] != reportId)
                {
                    throw new InvalidDataException($"Invalid report ID/bytes in {summary.Path}: {line}");
                }

                if (!summary._reports.TryGetValue(reportId, out var reports))
                {
                    reports = [];
                    summary._reports.Add(reportId, reports);
                }

                reports.Add(bytes);
            }

            return summary;
        }

        public IReadOnlyList<ISet<byte>> DistinctBytes(byte reportId)
        {
            if (!_reports.TryGetValue(reportId, out var reports))
            {
                return [];
            }

            var length = reports.Max(report => report.Length);
            var values = Enumerable.Range(0, length).Select(_ => (ISet<byte>)new SortedSet<byte>()).ToArray();
            foreach (var report in reports)
            {
                for (var index = 0; index < report.Length; index++)
                {
                    values[index].Add(report[index]);
                }
            }

            return values;
        }
    }

    private sealed class HidInterface
    {
        private HidInterface(int index, string path, ushort productId, ushort usagePage, ushort usage, ushort inputReportLength, string capsStatus, string readProbe, IReadOnlyList<HidInputCapability>? inputButtonCaps = null, IReadOnlyList<HidInputCapability>? inputValueCaps = null)
        {
            Index = index;
            Path = path;
            ProductId = productId;
            UsagePage = usagePage;
            Usage = usage;
            InputReportLength = inputReportLength;
            CapsStatus = capsStatus;
            ReadProbe = readProbe;
            InputButtonCaps = inputButtonCaps ?? [];
            InputValueCaps = inputValueCaps ?? [];
        }

        public int Index { get; }
        public string Path { get; }
        public ushort ProductId { get; }
        public ushort UsagePage { get; }
        public ushort Usage { get; }
        public ushort InputReportLength { get; }
        public string CapsStatus { get; }
        public string ReadProbe { get; }
        public IReadOnlyList<HidInputCapability> InputButtonCaps { get; }
        public IReadOnlyList<HidInputCapability> InputValueCaps { get; }
        public string RedactedPath => RedactPath(Path);

        public static List<HidInterface> EnumerateTarget()
        {
            HidD_GetHidGuid(out var hidGuid);
            using var deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (deviceInfoSet.IsInvalid)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed");
            }

            var targets = new List<HidInterface>();
            for (uint memberIndex = 0; ; memberIndex++)
            {
                var interfaceData = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, memberIndex, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new System.ComponentModel.Win32Exception(error, "SetupDiEnumDeviceInterfaces failed");
                }

                var path = GetDevicePath(deviceInfoSet, ref interfaceData);
                if (!BasiliskV3Pro35KProductIds.Any(productId => path.Contains($"vid_{RazerVendorId:X4}&pid_{productId:X4}", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                using var handle = OpenReadHandle(path);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    targets.Add(new HidInterface(
                        targets.Count,
                        path,
                        ProductIdFromPath(path),
                        0,
                        0,
                        0,
                        $"unavailable(read open win32={error})",
                        $"unavailable(win32={error})"));
                    continue;
                }

                if (!HidD_GetAttributes(handle, out var attributes))
                {
                    var error = Marshal.GetLastWin32Error();
                    targets.Add(new HidInterface(targets.Count, path, ProductIdFromPath(path), 0, 0, 0, $"unavailable(HidD_GetAttributes win32={error})", "ready(read-only shared)"));
                    continue;
                }

                if (attributes.VendorId != RazerVendorId || !BasiliskV3Pro35KProductIds.Contains(attributes.ProductId))
                {
                    continue;
                }

                var caps = GetCaps(handle);
                var readProbe = caps.InputReportLength == 0 ? "not-applicable(input length 0)" : "ready(read-only shared)";
                targets.Add(new HidInterface(targets.Count, path, attributes.ProductId, caps.UsagePage, caps.Usage, caps.InputReportLength, caps.Status, readProbe, caps.ButtonCapabilities, caps.ValueCapabilities));
            }

            return targets;
        }

        public FileStream OpenReadStream()
        {
            var handle = OpenReadHandle(Path);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new System.ComponentModel.Win32Exception(error, $"Cannot open interface {Index} for read-only capture");
            }

            return new FileStream(handle, FileAccess.Read, InputReportLength, isAsync: true);
        }

        private static HidCapsInfo GetCaps(SafeFileHandle handle)
        {
            if (!HidD_GetPreparsedData(handle, out var preparsedData))
            {
                return new HidCapsInfo(0, 0, 0, $"unavailable(HidD_GetPreparsedData win32={Marshal.GetLastWin32Error()})", [], []);
            }

            try
            {
                var status = HidP_GetCaps(preparsedData, out var caps);
                if (status != HidpStatusSuccess)
                {
                    return new HidCapsInfo(0, 0, 0, $"unavailable(HidP_GetCaps status=0x{status:X8})", [], []);
                }

                var buttons = GetInputButtonCaps(preparsedData, caps.NumberInputButtonCaps, out var buttonStatus);
                var values = GetInputValueCaps(preparsedData, caps.NumberInputValueCaps, out var valueStatus);
                var detailStatus = buttonStatus == HidpStatusSuccess && valueStatus == HidpStatusSuccess
                    ? "ok"
                    : $"ok; input_detail_status(button=0x{buttonStatus:X8},value=0x{valueStatus:X8})";
                return new HidCapsInfo(caps.UsagePage, caps.Usage, caps.InputReportByteLength, detailStatus, buttons, values);
            }
            finally
            {
                HidD_FreePreparsedData(preparsedData);
            }
        }

        private static IReadOnlyList<HidInputCapability> GetInputButtonCaps(IntPtr preparsedData, ushort capCount, out int status)
        {
            if (capCount == 0)
            {
                status = HidpStatusSuccess;
                return [];
            }

            var nativeCaps = new HidpButtonCaps[capCount];
            var returnedCount = capCount;
            status = HidP_GetButtonCaps(HidpReportType.Input, nativeCaps, ref returnedCount, preparsedData);
            return status == HidpStatusSuccess
                ? nativeCaps.Take(returnedCount).Select(Describe).ToArray()
                : [];
        }

        private static IReadOnlyList<HidInputCapability> GetInputValueCaps(IntPtr preparsedData, ushort capCount, out int status)
        {
            if (capCount == 0)
            {
                status = HidpStatusSuccess;
                return [];
            }

            var nativeCaps = new HidpValueCaps[capCount];
            var returnedCount = capCount;
            status = HidP_GetValueCaps(HidpReportType.Input, nativeCaps, ref returnedCount, preparsedData);
            return status == HidpStatusSuccess
                ? nativeCaps.Take(returnedCount).Select(Describe).ToArray()
                : [];
        }

        private static HidInputCapability Describe(HidpButtonCaps caps) => new(
            $"report_id=0x{caps.ReportId:X2} usage_page=0x{caps.UsagePage:X4} usage={FormatUsage(caps.IsRange, caps.Usage, caps.UsageMin, caps.UsageMax)} " +
            $"bit_field=0x{caps.BitField:X4} data_index={FormatDataIndex(caps.IsRange, caps.DataIndex, caps.DataIndexMin, caps.DataIndexMax)} " +
            $"link_usage_page=0x{caps.LinkUsagePage:X4} link_usage=0x{caps.LinkUsage:X4} absolute={caps.IsAbsolute != 0}");

        private static HidInputCapability Describe(HidpValueCaps caps) => new(
            $"report_id=0x{caps.ReportId:X2} usage_page=0x{caps.UsagePage:X4} usage={FormatUsage(caps.IsRange, caps.Usage, caps.UsageMin, caps.UsageMax)} " +
            $"bit_size={caps.BitSize} report_count={caps.ReportCount} logical=[{caps.LogicalMin},{caps.LogicalMax}] physical=[{caps.PhysicalMin},{caps.PhysicalMax}] " +
            $"null={caps.HasNull != 0} absolute={caps.IsAbsolute != 0} link_usage_page=0x{caps.LinkUsagePage:X4} link_usage=0x{caps.LinkUsage:X4}");

        private static string FormatUsage(byte isRange, ushort usage, ushort usageMin, ushort usageMax) => isRange != 0
            ? $"0x{usageMin:X4}-0x{usageMax:X4}"
            : $"0x{usage:X4}";

        private static string FormatDataIndex(byte isRange, ushort dataIndex, ushort dataIndexMin, ushort dataIndexMax) => isRange != 0
            ? $"{dataIndexMin}-{dataIndexMax}"
            : dataIndex.ToString();

        private static string GetDevicePath(SafeDeviceInfoSetHandle deviceInfoSet, ref SpDeviceInterfaceData interfaceData)
        {
            _ = SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
            var expected = Marshal.GetLastWin32Error();
            if (expected != ErrorInsufficientBuffer)
            {
                throw new System.ComponentModel.Win32Exception(expected, "SetupDiGetDeviceInterfaceDetail size query failed");
            }

            var detailData = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailData, requiredSize, out _, IntPtr.Zero))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetDeviceInterfaceDetail failed");
                }

                return Marshal.PtrToStringUni(IntPtr.Add(detailData, 4))
                    ?? throw new InvalidDataException("SetupAPI returned an empty HID interface path.");
            }
            finally
            {
                Marshal.FreeHGlobal(detailData);
            }
        }

        private static SafeFileHandle OpenReadHandle(string path) => CreateFile(
            path,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);

        private static string RedactPath(string path)
        {
            var parts = path.Split('#');
            return parts.Length >= 4 ? $"{parts[0]}#{parts[1]}#<instance>#{parts[^1]}" : "<unrecognized-hid-path>";
        }

        private static ushort ProductIdFromPath(string path)
        {
            foreach (var productId in BasiliskV3Pro35KProductIds)
            {
                if (path.Contains($"pid_{productId:X4}", StringComparison.OrdinalIgnoreCase))
                {
                    return productId;
                }
            }

            return 0;
        }
    }

    private sealed record HidCapsInfo(ushort UsagePage, ushort Usage, ushort InputReportLength, string Status, IReadOnlyList<HidInputCapability> ButtonCapabilities, IReadOnlyList<HidInputCapability> ValueCapabilities);

    private sealed record HidInputCapability(string Description);

    private static IEnumerable<TResult> ZipLongest<TLeft, TRight, TResult>(IReadOnlyList<TLeft> left, IReadOnlyList<TRight> right, Func<TLeft, TRight, TResult> selector)
    {
        var count = Math.Max(left.Count, right.Count);
        for (var index = 0; index < count; index++)
        {
            yield return selector(index < left.Count ? left[index] : default!, index < right.Count ? right[index] : default!);
        }
    }

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int HidpStatusSuccess = 0x00110000;

    private enum HidpReportType
    {
        Input = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpButtonCaps
    {
        public ushort UsagePage;
        public byte ReportId;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public uint[] Reserved;
        public HidpCapsUnion Union;

        public ushort Usage => Union.NotRange.Usage;
        public ushort UsageMin => Union.Range.UsageMin;
        public ushort UsageMax => Union.Range.UsageMax;
        public ushort DataIndex => Union.NotRange.DataIndex;
        public ushort DataIndexMin => Union.Range.DataIndexMin;
        public ushort DataIndexMax => Union.Range.DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpValueCaps
    {
        public ushort UsagePage;
        public byte ReportId;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public HidpCapsUnion Union;

        public ushort Usage => Union.NotRange.Usage;
        public ushort UsageMin => Union.Range.UsageMin;
        public ushort UsageMax => Union.Range.UsageMax;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct HidpCapsUnion
    {
        [FieldOffset(0)] public HidpNotRange NotRange;
        [FieldOffset(0)] public HidpRange Range;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpNotRange
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort StringIndex;
        public ushort DesignatorIndex;
        public ushort DataIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpRange
    {
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort UsagePageMin;
        public ushort UsagePageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, out HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [DllImport("hid.dll")]
    private static extern int HidP_GetButtonCaps(HidpReportType reportType, [Out] HidpButtonCaps[] buttonCaps, ref ushort buttonCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetValueCaps(HidpReportType reportType, [Out] HidpValueCaps[] valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(SafeDeviceInfoSetHandle deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(SafeDeviceInfoSetHandle deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    private sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeDeviceInfoSetHandle() : base(true) { }

        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }
}
