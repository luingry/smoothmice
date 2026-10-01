param([double]$Minutes = 10, [string]$OutPath = "$env:LOCALAPPDATA\SmoothMice\Diagnostics\raw-session.ndjson")
# Passive raw input recorder: WH_MOUSE_LL + WH_KEYBOARD_LL. Never blocks mouse input.
# Labels (keys are swallowed so apps don't react): F6 = inertia block, F7 = legit-scroll block, F9 = neutral/pause.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public static class RawRecorder {
  delegate IntPtr Proc(int code, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
  [StructLayout(LayoutKind.Sequential)] struct MS { public POINT pt; public uint data, flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] struct KB { public uint vk, scan, flags, time; public IntPtr extra; }
  [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, Proc p, IntPtr h, uint t);
  [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
  [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
  [DllImport("kernel32.dll")] static extern bool Beep(int f, int d);
  static Proc _m, _k; static IntPtr _mh, _kh;
  static readonly BlockingCollection<string> Q = new BlockingCollection<string>(new ConcurrentQueue<string>(), 200000);
  static Thread _writer; static string _label = "none"; public static int Dropped;
  public static void Start(string path) {
    _writer = new Thread(() => {
      using (var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16)) {
        w.WriteLine("{\"kind\":\"session\",\"freq\":" + Stopwatch.Frequency + ",\"utc\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
        var last = Stopwatch.GetTimestamp();
        foreach (var s in Q.GetConsumingEnumerable()) {
          w.WriteLine(s);
          if (Stopwatch.GetTimestamp() - last > Stopwatch.Frequency / 4) { w.Flush(); last = Stopwatch.GetTimestamp(); }
        }
      }
    }) { IsBackground = false };
    _writer.Start();
    _m = MouseProc; _k = KeyProc;
    var mod = GetModuleHandle(null);
    _mh = SetWindowsHookEx(14, _m, mod, 0);
    _kh = SetWindowsHookEx(13, _k, mod, 0);
  }
  public static void Stop() {
    if (_mh != IntPtr.Zero) UnhookWindowsHookEx(_mh);
    if (_kh != IntPtr.Zero) UnhookWindowsHookEx(_kh);
    Q.CompleteAdding(); _writer.Join(5000);
  }
  static void Add(string s) { if (!Q.TryAdd(s)) Interlocked.Increment(ref Dropped); }
  static IntPtr MouseProc(int code, IntPtr w, IntPtr l) {
    if (code >= 0) {
      var t = Stopwatch.GetTimestamp();
      var i = (MS)Marshal.PtrToStructure(l, typeof(MS));
      int msg = w.ToInt32();
      string kind = msg == 0x200 ? "move" : msg == 0x20A ? "wheel" : msg == 0x20E ? "hwheel" : "btn" + msg.ToString("X");
      int delta = (msg == 0x20A || msg == 0x20E) ? (short)(i.data >> 16) : 0;
      Add("{\"t\":" + t + ",\"k\":\"" + kind + "\",\"x\":" + i.pt.x + ",\"y\":" + i.pt.y + ",\"d\":" + delta + ",\"f\":" + i.flags + ",\"e\":" + (i.extra.ToInt64() & 0xFFFFFFFF) + "}");
    }
    return CallNextHookEx(_mh, code, w, l);
  }
  static IntPtr KeyProc(int code, IntPtr w, IntPtr l) {
    if (code >= 0 && (w.ToInt32() == 0x100 || w.ToInt32() == 0x104)) {
      var k = (KB)Marshal.PtrToStructure(l, typeof(KB));
      string label = k.vk == 0x75 ? "inertia" : k.vk == 0x76 ? "legit" : k.vk == 0x78 ? "none" : null;
      if (label != null) {
        _label = label;
        Add("{\"t\":" + Stopwatch.GetTimestamp() + ",\"k\":\"label\",\"label\":\"" + label + "\"}");
        int f = label == "inertia" ? 1200 : label == "legit" ? 600 : 300;
        ThreadPool.QueueUserWorkItem(_ => Beep(f, 120));
        return (IntPtr)1;
      }
    }
    if (code >= 0 && (w.ToInt32() == 0x101 || w.ToInt32() == 0x105)) {
      var k = (KB)Marshal.PtrToStructure(l, typeof(KB));
      if (k.vk == 0x75 || k.vk == 0x76 || k.vk == 0x78) return (IntPtr)1;
    }
    return CallNextHookEx(_kh, code, w, l);
  }
}
'@
New-Item -ItemType Directory -Force (Split-Path $OutPath) | Out-Null
[RawRecorder]::Start($OutPath)
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = [int]($Minutes * 60 * 1000)
$timer.add_Tick({ $timer.Stop(); [System.Windows.Forms.Application]::ExitThread() })
$timer.Start()
[System.Windows.Forms.Application]::Run()
[RawRecorder]::Stop()
"stopped; dropped=$([RawRecorder]::Dropped) file=$OutPath"

