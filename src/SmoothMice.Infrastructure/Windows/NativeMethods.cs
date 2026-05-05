using System.Runtime.InteropServices;
using System.Text;

namespace SmoothMice.Infrastructure.Windows;

public static class NativeMethods
{
    public const int WhMouseLl = 14;
    public const int WmMousewheel = 0x020A;
    public const int WmMousehwheel = 0x020E;

    public const int  VkShift   = 0x10;
    public const int  VkControl = 0x11;
    public const uint MkShift   = 0x0004;
    public const uint MkControl = 0x0008;

    // MSLLHOOKSTRUCT.flags: set by Windows when the event was synthesised via SendInput.
    // We use this to skip re-processing our own injected wheel events in the hook.
    public const uint LlmhfInjected = 0x00000001;

    // SendInput mouse-event flags
    public const uint MOUSEEVENTF_WHEEL  = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x1000;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Returns true when <paramref name="hWnd"/> is a valid, existing window handle.</summary>
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    public const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// Full process query access.  Non-elevated (medium-integrity) processes CANNOT
    /// obtain this access right on elevated (high-integrity) processes — <c>OpenProcess</c>
    /// returns NULL / ERROR_ACCESS_DENIED.  We use this to detect UIPI elevation:
    /// if the open fails, the target is elevated and <c>PostMessage</c> would be silently
    /// dropped by UIPI; we must use <c>SendInput</c> instead.
    /// </summary>
    public const uint ProcessQueryInformation = 0x0400;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Returns the class name of a window (e.g. "Chrome_WidgetWin_1").</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// Returns an ancestor of a window.  <c>GA_ROOT = 2</c> returns the topmost
    /// parent in the parent chain (stopping at a top-level window, not the desktop).
    /// Useful for resolving the actual app window when <c>WindowFromPoint</c> returns
    /// a deep child (e.g. a WinUI 3 <c>DesktopChildSiteBridge</c> hosted element).
    /// </summary>
    public const uint GaRoot = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageName(
        IntPtr hProcess, uint dwFlags,
        System.Text.StringBuilder lpExeName, ref uint lpdwSize);

    // Timer resolution — call timeBeginPeriod(1) while scroll loop is active so that
    // System.Threading.Timer at 4 ms actually fires at ~4 ms instead of the Windows
    // default 15.6 ms tick, which is the primary cause of choppy animation.
    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// Mouse-specific part of the SendInput INPUT union.
    /// Sequential layout lets the CLR insert the correct platform padding before
    /// <c>ExtraInfo</c> (IntPtr), matching the native struct on both 32- and 64-bit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int    X;
        public int    Y;
        public uint   MouseData;
        public uint   Flags;
        public uint   Time;
        public IntPtr ExtraInfo;
    }

    /// <summary>
    /// INPUT structure for SendInput (mouse variant only).
    /// Sequential layout matches the native padding the C compiler inserts between
    /// the DWORD type field and the pointer-aligned union on both 32- and 64-bit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint       Type;   // 0 = INPUT_MOUSE
        public MOUSEINPUT Mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ── Process snapshot (parent-process lookup) ──────────────────────────
    public const uint TH32CS_SNAPPROCESS = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PROCESSENTRY32
    {
        public uint   dwSize;
        public uint   cntUsage;
        public uint   th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint   th32ModuleID;
        public uint   cntThreads;
        public uint   th32ParentProcessID;
        public int    pcPriClassBase;
        public uint   dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll")]
    public static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    public static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
}
