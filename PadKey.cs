// PadKey - HID gamepad button -> keyboard key mapper (Raw Input based)
// Build: build.cmd   (csc.exe, .NET Framework 4.x, no SDK needed)
//
//   padkey.exe            -> background mode + tray icon + settings window
//   padkey.exe list       -> console dump of every HID device (diagnostics)
//   padkey.exe learn VID  -> console live view of HID reports (diagnostics)
//
// Source file is intentionally ASCII-only so csc never guesses the wrong codepage.
// UI strings use a small lookup table (T.S) so they can carry Turkish characters.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PadKey
{
    #region Win32 interop

    internal static class Native
    {
        public const int WM_INPUT = 0x00FF;
        public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;

        public const uint RIDEV_REMOVE = 0x00000001;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_DEVNOTIFY = 0x00002000;

        public const uint RID_INPUT = 0x10000003;
        public const uint RIDI_PREPARSEDDATA = 0x20000005;
        public const uint RIDI_DEVICENAME = 0x20000007;
        public const uint RIDI_DEVICEINFO = 0x2000000b;

        public const uint RIM_TYPEHID = 2;

        public const int HIDP_STATUS_SUCCESS = 0x00110000;
        public const int HidP_Input = 0;
        public const ushort HID_USAGE_PAGE_BUTTON = 0x09;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO_HID
        {
            public uint dwVendorId;
            public uint dwProductId;
            public uint dwVersionNumber;
            public ushort usUsagePage;
            public ushort usUsage;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct RID_DEVICE_INFO
        {
            [FieldOffset(0)] public uint cbSize;
            [FieldOffset(4)] public uint dwType;
            [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
            [FieldOffset(28)] public uint pad1; // keeps the struct 32 bytes (keyboard union member)
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
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
        public struct HIDP_BUTTON_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
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
            public ushort UsageMin;
            public ushort UsageMax;
            public ushort StringMin;
            public ushort StringMax;
            public ushort DesignatorMin;
            public ushort DesignatorMax;
            public ushort DataIndexMin;
            public ushort DataIndexMax;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceList([In, Out] RAWINPUTDEVICELIST[] pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

        [DllImport("hid.dll")]
        public static extern int HidP_GetButtonCaps(int reportType, [In, Out] HIDP_BUTTON_CAPS[] caps, ref ushort capsLength, IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern int HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
            [In, Out] ushort[] usageList, ref int usageLength, IntPtr preparsed, byte[] report, int reportLength);

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL, wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

        // --- direct HID access (some vendor collections only stream once a client opens them) ---

        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToRead,
            out int lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToWrite,
            out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetNumInputBuffers(IntPtr hFile, uint numberBuffers);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetOutputReport(IntPtr hFile, byte[] buffer, int bufferLength);

        // Read-only probes: these ask the device for a report, they never write configuration.
        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(IntPtr hFile, byte[] buffer, int bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetInputReport(IntPtr hFile, byte[] buffer, int bufferLength);

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        [DllImport("kernel32.dll")] public static extern bool AllocConsole();
        [DllImport("kernel32.dll")] public static extern bool FreeConsole();
        [DllImport("kernel32.dll")] public static extern bool AttachConsole(int dwProcessId);
        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool SetConsoleTitleW(string title);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int HWND_BROADCAST = 0xFFFF;
        public const uint IMAGE_ICON = 1;
        public const uint LR_SHARED = 0x8000;
        public const int IDI_APPLICATION_RES = 32512;   // id csc gives to /win32icon

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadImageW(IntPtr hinst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll")]
        public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // --- low level keyboard hook, used only to verify our injected keys are visible
        //     to exactly the mechanism Steam uses to catch its screenshot hotkey ---

        public const int WH_KEYBOARD_LL = 13;
        public const uint LLKHF_INJECTED = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);
    }

    #endregion

    #region Model

    internal class HidDevice
    {
        public IntPtr Handle;
        public string Path = "";
        public ushort Vid, Pid, UsagePage, Usage;
        public IntPtr Preparsed = IntPtr.Zero;
        public ushort[] UsageBuf = new ushort[0];
        public int InputReportLength;
        public int OutputReportLength;

        public byte[] Last;
        public HashSet<int> LastButtons = new HashSet<int>();
        public bool DirectRead;   // reports come from our own ReadFile loop, not from Raw Input

        public bool IsVendorPage { get { return UsagePage >= 0xF0; } }

        public string Ident
        {
            get { return string.Format("VID_{0:X4}&PID_{1:X4} (usage {2:X2}:{3:X2})", Vid, Pid, UsagePage, Usage); }
        }

        public string ShortId
        {
            get
            {
                string iface = "";
                int mi = Path.IndexOf("MI_", StringComparison.OrdinalIgnoreCase);
                if (mi >= 0) iface = Path.Substring(mi, Math.Min(5, Path.Length - mi));
                else
                {
                    int ig = Path.IndexOf("IG_", StringComparison.OrdinalIgnoreCase);
                    if (ig >= 0) iface = Path.Substring(ig, Math.Min(5, Path.Length - ig));
                }
                return string.Format("{0:X4}:{1:X4}{2}", Vid, Pid, iface.Length > 0 ? " " + iface : "");
            }
        }
    }

    internal class Trigger
    {
        public int Vid = -1, Pid = -1, UsagePage = -1, Usage = -1;
        public int Button = -1;       // parsed HID button number (1-based)
        public int ByteIndex = -1;    // raw report byte index
        public int Mask = 0;          // which bits of that byte carry the signal
        public int Value = -1;        // masked value that means "pressed"; -1 == Mask (rising bits)
        public string DevLabel = "";

        // A device can answer several kinds of request on one pipe. This pad replies to the
        // 3 s keepalives with 0xCD/0xA9 reports whose byte 10 means something else entirely;
        // reading those as button state made a held button look briefly released, which then
        // fired a second time. The gate restricts a rule to one report type.
        public int GateByte = -1;
        public int GateMask = 0xFF;
        public int GateValue = 0;

        public int ActiveValue { get { return Value >= 0 ? Value : Mask; } }

        /// <summary>False for reports this rule must ignore completely - not "released".</summary>
        public bool Applies(byte[] report)
        {
            if (GateByte < 0) return true;
            if (GateByte >= report.Length) return false;
            return (report[GateByte] & GateMask) == GateValue;
        }

        public bool IsSet { get { return Button > 0 || ByteIndex >= 0; } }

        public bool MatchesDevice(HidDevice d)
        {
            if (Vid >= 0 && d.Vid != Vid) return false;
            if (Pid >= 0 && d.Pid != Pid) return false;
            if (UsagePage >= 0 && d.UsagePage != UsagePage) return false;
            if (Usage >= 0 && d.Usage != Usage) return false;
            return true;
        }

        public bool Evaluate(byte[] report, HashSet<int> buttons)
        {
            if (Button > 0) return buttons.Contains(Button);
            if (ByteIndex >= 0 && ByteIndex < report.Length) return (report[ByteIndex] & Mask) == ActiveValue;
            return false;
        }

        public string Describe()
        {
            if (!IsSet) return "(not assigned)";
            if (Button > 0) return string.Format("{0} - HID button {1}", DevLabel, Button);
            return string.Format("{0} - byte {1}, mask 0x{2:X2} = 0x{3:X2}", DevLabel, ByteIndex, Mask, ActiveValue);
        }

        public Trigger Clone()
        {
            var t = new Trigger();
            t.Vid = Vid; t.Pid = Pid; t.UsagePage = UsagePage; t.Usage = Usage;
            t.GateByte = GateByte; t.GateMask = GateMask; t.GateValue = GateValue;
            t.Button = Button; t.ByteIndex = ByteIndex; t.Mask = Mask; t.Value = Value; t.DevLabel = DevLabel;
            return t;
        }
    }

    internal class Rule
    {
        public string Name = "Kural";
        public Trigger Trig = new Trigger();
        public List<Trigger> Alternatives = new List<Trigger>();

        public ushort[] Mods = new ushort[0];
        public ushort Key = 0x7B; // F12
        public bool Hold = false;
        public int HoldMs = 45;
        public int CooldownMs = 250;
        public int DebounceMs = 130;   // ~3 stream frames: one dropped report must not read as a release

        // The pipe carries occasional reports of other kinds whose bytes happen to satisfy a
        // rule. Measured: every false trigger lasted a single frame, every real press lasted
        // 48 ms or more. Requiring the signal to hold across two frames removes them without
        // needing to know what those other reports are.
        public int ArmMs = 35;
        public DateTime PendingSince = DateTime.MinValue;
        public bool Enabled = true;

        public bool Active;
        public DateTime LastFire = DateTime.MinValue;
        public DateTime LastActiveSeen = DateTime.MinValue;
        public int LastByte = -1;        // raw value of the watched byte at the last transition
        public DateTime PressedAt = DateTime.MinValue;

        public string KeyText
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var m in Mods) sb.Append(Keys_.Name(m)).Append("+");
                sb.Append(Keys_.Name(Key));
                return sb.ToString();
            }
        }
    }

    internal static class Keys_
    {
        public static string Name(ushort vk)
        {
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
            if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
            switch (vk)
            {
                case 0x1B: return "ESC";
                case 0x0D: return "ENTER";
                case 0x20: return "SPACE";
                case 0x09: return "TAB";
                case 0x08: return "BACKSPACE";
                case 0x2D: return "INSERT";
                case 0x2E: return "DELETE";
                case 0x24: return "HOME";
                case 0x23: return "END";
                case 0x21: return "PGUP";
                case 0x22: return "PGDN";
                case 0x26: return "UP";
                case 0x28: return "DOWN";
                case 0x25: return "LEFT";
                case 0x27: return "RIGHT";
                case 0x2C: return "PRINTSCREEN";
                case 0x13: return "PAUSE";
                case 0x14: return "CAPSLOCK";
                case 0x90: return "NUMLOCK";
                case 0x10: case 0xA0: case 0xA1: return "SHIFT";
                case 0x11: case 0xA2: case 0xA3: return "CTRL";
                case 0x12: case 0xA4: case 0xA5: return "ALT";
                case 0x5B: case 0x5C: return "WIN";
                case 0xC0: return "TILDE";
                case 0xBD: return "MINUS";
                case 0xBB: return "EQUALS";
                case 0xBC: return "COMMA";
                case 0xBE: return "PERIOD";
                case 0xBF: return "SLASH";
                case 0xDC: return "BACKSLASH";
                case 0xBA: return "SEMICOLON";
                case 0xDE: return "QUOTE";
                case 0xDB: return "LBRACKET";
                case 0xDD: return "RBRACKET";
            }
            if (vk >= 0x60 && vk <= 0x69) return "NUM" + (vk - 0x60);
            switch (vk)
            {
                case 0x6A: return "NUMMULT";
                case 0x6B: return "NUMPLUS";
                case 0x6D: return "NUMMINUS";
                case 0x6E: return "NUMDOT";
                case 0x6F: return "NUMDIV";
            }
            return "0x" + vk.ToString("X2");
        }

        public static ushort FromName(string p)
        {
            p = p.Trim().ToUpperInvariant();
            if (p.StartsWith("0X")) return (ushort)int.Parse(p.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (p.Length > 1 && p[0] == 'F')
            {
                int n;
                if (int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24) return (ushort)(0x70 + n - 1);
            }
            if (p.Length > 3 && p.StartsWith("NUM"))
            {
                int n;
                if (int.TryParse(p.Substring(3), out n) && n >= 0 && n <= 9) return (ushort)(0x60 + n);
            }
            if (p.Length == 1)
            {
                char c = p[0];
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return (ushort)c;
            }
            for (int vk = 1; vk < 0xFF; vk++)
                if (Name((ushort)vk) == p) return (ushort)vk;
            throw new Exception("unknown key: " + p);
        }
    }

    #endregion

    #region Capture (auto-detect which bit a paddle toggles)

    internal class Candidate
    {
        public HidDevice Dev;
        public int Button = -1;
        public int ByteIndex = -1;
        public int Mask;
        public int Value = -1;
        public bool Released;
        public int HeaderByte = -1;   // report[1] of the report the change was seen in

        public string Key { get { return Dev.Handle + "|" + Button + "|" + ByteIndex + "|" + Mask + "|" + Value; } }

        public Trigger ToTrigger()
        {
            var t = new Trigger();
            t.Vid = Dev.Vid; t.Pid = Dev.Pid; t.UsagePage = Dev.UsagePage; t.Usage = Dev.Usage;
            t.Button = Button; t.ByteIndex = ByteIndex; t.Mask = Mask; t.Value = Value;
            t.DevLabel = Dev.ShortId;

            // Several report types share one vendor endpoint, so pin the rule to the type
            // the signal was actually seen in. Ordinary gamepad collections are not like
            // that, so only do it for vendor pages.
            if (Dev.IsVendorPage && HeaderByte >= 0 && ByteIndex >= 0)
            {
                t.GateByte = 1;
                t.GateMask = 0xFF;
                t.GateValue = HeaderByte;
            }
            return t;
        }

        public int Score()
        {
            int s = 0;
            if (!Released) s += 1000;                 // a real button goes back up
            if (!Dev.IsVendorPage) s += 100;          // paddles live on the vendor interface
            if (Button > 0) s += 50;                  // prefer a raw bit over a possibly-mirrored button
            s += BitCount(Mask);                      // a single bit beats a whole byte
            return s;
        }

        private static int BitCount(int v)
        {
            int n = 0;
            while (v != 0) { n += v & 1; v >>= 1; }
            return n;
        }
    }

    /// <summary>
    /// Opens a HID collection with CreateFile and pumps ReadFile on its own thread.
    /// Raw Input only listens; it never opens the device. Vendor collections commonly
    /// stay silent until a client actually opens them - which is what the vendor's own
    /// configuration tool does. This is how we see the paddles with no firmware mapping.
    /// </summary>
    internal class DirectReader
    {
        private readonly string _path;
        private readonly int _reportLen;
        private readonly Action<byte[]> _onReport;
        private IntPtr _handle = Native.INVALID_HANDLE;
        private IntPtr _writeHandle = Native.INVALID_HANDLE;
        private System.Threading.Thread _thread;
        private volatile bool _stop;

        public string LastError = "";
        public string OpenMode = "";
        public bool Failed { get { return !string.IsNullOrEmpty(LastError); } }
        public int ReportCount;
        public int PollCount;
        public int ZeroReads;

        private readonly List<byte[]> _pollData = new List<byte[]>();
        private readonly List<int> _pollPeriod = new List<int>();
        private readonly List<int> _pollSilentOnly = new List<int>();
        private readonly List<DateTime> _pollNext = new List<DateTime>();
        private DateTime _lastReportAt = DateTime.MinValue;
        private System.Threading.Thread _pollThread;

        public DirectReader(string path, int reportLen, Action<byte[]> onReport)
        {
            _path = path;
            _reportLen = reportLen > 0 ? reportLen : 65;
            _onReport = onReport;
        }

        public bool Start()
        {
            _handle = Native.CreateFileW(_path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);

            if (_handle == Native.INVALID_HANDLE)
            {
                int err = Marshal.GetLastWin32Error();
                // Read-write can be refused when another process holds it exclusively; read-only often still works.
                _handle = Native.CreateFileW(_path, Native.GENERIC_READ,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                OpenMode = "salt-okunur (rw err=" + err + ")";
            }
            else OpenMode = "oku-yaz";

            if (_handle == Native.INVALID_HANDLE)
            {
                LastError = "CreateFile err=" + Marshal.GetLastWin32Error();
                return false;
            }

            Native.HidD_SetNumInputBuffers(_handle, 64);

            _thread = new System.Threading.Thread(Pump);
            _thread.IsBackground = true;
            _thread.Start();

            if (_pollData.Count > 0)
            {
                // A second handle just for writing. Windows serialises I/O on a synchronous
                // file handle, so with one handle the poll write blocks behind the pending
                // ReadFile forever - exactly one request got through and then nothing.
                _writeHandle = Native.CreateFileW(_path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (_writeHandle == Native.INVALID_HANDLE)
                {
                    LastError = "could not open write handle, err=" + Marshal.GetLastWin32Error();
                }
                else
                {
                    _pollThread = new System.Threading.Thread(PollLoop);
                    _pollThread.IsBackground = true;
                    _pollThread.Start();
                }
            }
            return true;
        }

        /// <summary>
        /// Some vendor collections answer only when asked. Repeatedly writing the request
        /// report is what keeps the replies coming - this is a read request, not a config write.
        /// </summary>
        public void SetPoll(byte[] report, int periodMs) { AddPoll(report, periodMs, 0); }

        /// <summary>
        /// Queues a report to be resent on its own interval. With silentOnlyMs > 0 it is sent
        /// only while the device has been quiet that long - so a wake-up request goes out when
        /// the stream is dead and never while it is healthy. That matters: the device answers
        /// these requests on the same pipe, and those answers are not state reports.
        /// </summary>
        public void AddPoll(byte[] report, int periodMs, int silentOnlyMs)
        {
            _pollData.Add(report);
            _pollPeriod.Add(periodMs);
            _pollSilentOnly.Add(silentOnlyMs);
            _pollNext.Add(DateTime.MinValue);
        }

        private void PollLoop()
        {
            // WriteFile targets the interrupt OUT endpoint and blocks indefinitely on this
            // device. HidD_SetOutputReport sends the same report over the control pipe
            // (SET_REPORT) and returns straight away, which is what the vendor tool does.
            // WriteFile goes out the interrupt OUT endpoint, which is where the vendor tool
            // sends everything; HidD_SetOutputReport would use a control transfer instead.
            bool useCtrl = false;
            while (!_stop)
            {
                var now = DateTime.UtcNow;
                for (int i = 0; i < _pollData.Count && !_stop; i++)
                {
                    if (now < _pollNext[i]) continue;
                    _pollNext[i] = now.AddMilliseconds(_pollPeriod[i]);

                    if (_pollSilentOnly[i] > 0 &&
                        (now - _lastReportAt).TotalMilliseconds < _pollSilentOnly[i]) continue;

                    byte[] rep = _pollData[i];
                    bool ok = useCtrl
                        ? Native.HidD_SetOutputReport(_writeHandle, rep, rep.Length)
                        : WriteViaFile(rep);
                    if (!ok)
                    {
                        if (!useCtrl && PollCount == 0) { useCtrl = true; continue; }
                        LastError = (useCtrl ? "SetOutputReport" : "WriteFile") + " err=" + Marshal.GetLastWin32Error();
                        return;
                    }
                    PollCount++;
                }

                // Sleep until the next item is actually due instead of spinning. With only
                // the 3 s keepalives queued this is ~1 wake-up per 3 s rather than 500/s.
                int wait = 1000;
                for (int i = 0; i < _pollNext.Count; i++)
                {
                    double dueMs = (_pollNext[i] - DateTime.UtcNow).TotalMilliseconds;
                    int due = dueMs < 0 ? 0 : (dueMs > 1000 ? 1000 : (int)dueMs);
                    if (due < wait) wait = due;
                }
                System.Threading.Thread.Sleep(wait < 1 ? 1 : wait);
            }
        }

        /// <summary>Sends a single report on the write handle (used for the keepalive).</summary>
        public bool SendOnce(byte[] report)
        {
            if (_writeHandle == Native.INVALID_HANDLE) return false;
            return Native.HidD_SetOutputReport(_writeHandle, report, report.Length);
        }

        private bool WriteViaFile(byte[] rep)
        {
            int written;
            return Native.WriteFile(_writeHandle, rep, rep.Length, out written, IntPtr.Zero);
        }

        private void Pump()
        {
            var buf = new byte[_reportLen];
            while (!_stop)
            {
                int read;
                if (!Native.ReadFile(_handle, buf, buf.Length, out read, IntPtr.Zero))
                {
                    LastError = "ReadFile err=" + Marshal.GetLastWin32Error();
                    break;
                }
                if (read <= 0) { ZeroReads++; continue; }
                ReportCount++;
                _lastReportAt = DateTime.UtcNow;
                var copy = new byte[read];
                Buffer.BlockCopy(buf, 0, copy, 0, read);
                try { _onReport(copy); } catch { }
            }
        }

        public void Stop()
        {
            _stop = true;
            if (_writeHandle != Native.INVALID_HANDLE)
            {
                Native.CancelIoEx(_writeHandle, IntPtr.Zero);
                Native.CloseHandle(_writeHandle);
                _writeHandle = Native.INVALID_HANDLE;
            }
            if (_handle != Native.INVALID_HANDLE)
            {
                Native.CancelIoEx(_handle, IntPtr.Zero);
                Native.CloseHandle(_handle);
                _handle = Native.INVALID_HANDLE;
            }
        }
    }

    internal class CaptureSession
    {
        public DateTime BaselineUntil;
        public DateTime? Deadline;
        public readonly Dictionary<IntPtr, byte[]> Base = new Dictionary<IntPtr, byte[]>();
        public readonly Dictionary<IntPtr, HashSet<int>> Noisy = new Dictionary<IntPtr, HashSet<int>>();
        public readonly Dictionary<IntPtr, HashSet<int>> BaseButtons = new Dictionary<IntPtr, HashSet<int>>();
        public readonly Dictionary<string, Candidate> Found = new Dictionary<string, Candidate>();
        public readonly Dictionary<IntPtr, int> Counts = new Dictionary<IntPtr, int>();
        public bool BaselineDone;
        public DateTime GiveUpAt;
        public Action<List<Candidate>> Done;
        public Action<string> Status;
        public Action<string> Failed;

        public int TotalReports
        {
            get { int n = 0; foreach (var v in Counts.Values) n += v; return n; }
        }
    }

    #endregion

    internal class PadKeyForm : Form
    {
        public readonly Dictionary<IntPtr, HidDevice> Devices = new Dictionary<IntPtr, HidDevice>();
        public readonly List<Rule> Rules = new List<Rule>();
        public readonly List<string> RegisteredPairs = new List<string>();
        private readonly HashSet<uint> _registered = new HashSet<uint>();   // notify (+maybe sink)
        private readonly HashSet<uint> _sinking = new HashSet<uint>();      // of those, receiving input

        private DateTime _lastVendorReport = DateTime.UtcNow;
        private bool _vendorWarned;
        private readonly Dictionary<IntPtr, DirectReader> _readers = new Dictionary<IntPtr, DirectReader>();
        private readonly Queue<KeyValuePair<HidDevice, byte[]>> _inbox = new Queue<KeyValuePair<HidDevice, byte[]>>();
        private readonly object _inboxLock = new object();

        private readonly List<KeyValuePair<DateTime, Rule>> _pendingUp = new List<KeyValuePair<DateTime, Rule>>();
        private CaptureSession _cap;

        private IntPtr _buf = IntPtr.Zero;
        private int _bufSize;
        private readonly int _headerSize = 8 + 2 * IntPtr.Size;

        private readonly bool _learn;
        private readonly int _learnVidFilter = -1;
        private DateTime _learnBaselineUntil;
        private readonly Dictionary<IntPtr, HashSet<int>> _learnNoisy = new Dictionary<IntPtr, HashSet<int>>();

        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _timer;
        private SettingsForm _settings;
        public string LastEvent = "-";

        /// <summary>Posted by a second instance to bring this one's settings window up.</summary>
        public static readonly uint WM_SHOW_SETTINGS = Native.RegisterWindowMessageW("PadKey.ShowSettings");

        private readonly bool _startInTray;

        public PadKeyForm(bool learn, int learnVidFilter) : this(learn, learnVidFilter, false) { }

        public PadKeyForm(bool learn, int learnVidFilter, bool startInTray)
        {
            _learn = learn;
            _learnVidFilter = learnVidFilter;
            _startInTray = startInTray;

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-4000, -4000);
            Size = new Size(1, 1);
            Text = "PadKey";

            CreateHandle();
            RefreshDevices();
            RegisterRawInput();
            StartDirectReaders();

            if (_learn)
            {
                _learnBaselineUntil = DateTime.UtcNow.AddSeconds(2.5);
                Log("");
                Log("=== LEARN MODE ===");
                Log("1) Touch nothing for 2.5 s (measuring stick noise).");
                Log("2) Then press the back buttons one at a time.");
                Log("3) Ctrl+C to quit. Output also goes to padkey-log.txt.");
                Log("");
            }
            else
            {
                Profiles.Init();
                Config.LoadFrom(Profiles.ActivePath, Rules);
                Autostart.EnsureFlag();
                SetupTray();
                if (!_startInTray) BeginInvoke(new MethodInvoker(ShowSettings));
            }

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 10;
            _timer.Tick += OnTimer;
            _timer.Start();
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopDirectReaders();
            base.OnFormClosed(e);
        }

        #region Raw input plumbing

        private void RegisterRawInput()
        {
            var pairs = new HashSet<uint>();
            foreach (var d in Devices.Values)
            {
                if (d.UsagePage == 1 && (d.Usage == 2 || d.Usage == 6)) continue; // never sink mouse/keyboard
                if (d.Usage == 0) continue;                                       // usage 0 is not registerable

                // Outside learn/capture, subscribe only to what a rule actually reads, and
                // never to a device we already read directly. The pad's gamepad collection
                // streams ~1100 reports a second and each one costs a syscall to resolve.
                if (!_learn && _cap == null)
                {
                    if (d.DirectRead || !DeviceIsInteresting(d)) continue;
                }
                pairs.Add(((uint)d.UsagePage << 16) | d.Usage);
            }
            if (_learn || _cap != null)
            {
                pairs.Add((1u << 16) | 4);
                pairs.Add((1u << 16) | 5);
            }

            // Always keep a notify-only subscription on the gamepad usages. Without it, a pad
            // that is not plugged in at startup matches no rule, nothing gets registered, and
            // the arrival notification never comes - so it is never picked up at all.
            var notify = new HashSet<uint>();
            notify.Add((1u << 16) | 4);
            notify.Add((1u << 16) | 5);
            foreach (var p in pairs) notify.Add(p);

            // Registering re-arms RIDEV_DEVNOTIFY, which sends WM_INPUT_DEVICE_CHANGE right
            // back to us. Without this guard that loops forever on every hot-plug notification.
            if (_registered.SetEquals(notify) && _sinking.SetEquals(pairs)) return;

            // Drop subscriptions we no longer want (RIDEV_REMOVE needs a null target).
            foreach (var p in _registered)
            {
                if (notify.Contains(p)) continue;
                var rem = new Native.RAWINPUTDEVICE();
                rem.usUsagePage = (ushort)(p >> 16);
                rem.usUsage = (ushort)(p & 0xFFFF);
                rem.dwFlags = Native.RIDEV_REMOVE;
                rem.hwndTarget = IntPtr.Zero;
                Native.RegisterRawInputDevices(new Native.RAWINPUTDEVICE[] { rem }, 1,
                    (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
            }

            // One call per pair: RegisterRawInputDevices rejects the WHOLE array if a single
            // entry is invalid, which would silently leave us receiving nothing at all.
            int ok = 0;
            RegisteredPairs.Clear();
            _registered.Clear();
            _sinking.Clear();
            uint cb = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE));
            foreach (var p in notify)
            {
                bool sink = pairs.Contains(p);
                var rid = new Native.RAWINPUTDEVICE();
                rid.usUsagePage = (ushort)(p >> 16);
                rid.usUsage = (ushort)(p & 0xFFFF);
                // DEVNOTIFY alone costs nothing: it only asks for arrival/removal messages.
                rid.dwFlags = Native.RIDEV_DEVNOTIFY | (sink ? Native.RIDEV_INPUTSINK : 0u);
                rid.hwndTarget = Handle;
                var one = new Native.RAWINPUTDEVICE[] { rid };
                if (Native.RegisterRawInputDevices(one, 1, cb))
                {
                    ok++;
                    _registered.Add(p);
                    if (sink) _sinking.Add(p);
                    RegisteredPairs.Add(string.Format("{0:X2}:{1:X2}{2}", rid.usUsagePage, rid.usUsage, sink ? "" : "(n)"));
                }
                else if (_learn)
                    Log(string.Format("register FAILED for usage {0:X2}:{1:X2}, err={2}",
                        rid.usUsagePage, rid.usUsage, Marshal.GetLastWin32Error()));
            }
            if (_learn) Log(string.Format("registered {0}/{1} usage pairs: {2}", ok, notify.Count, string.Join(", ", RegisteredPairs.ToArray())));
            if (ok == 0) Log("NO usage registered - raw input is not working.");
        }

        public void RefreshDevices()
        {
            uint count = 0;
            uint sz = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            if (Native.GetRawInputDeviceList(null, ref count, sz) == unchecked((uint)-1)) return;
            if (count == 0) return;

            var arr = new Native.RAWINPUTDEVICELIST[count];
            if (Native.GetRawInputDeviceList(arr, ref count, sz) == unchecked((uint)-1)) return;

            var seen = new HashSet<IntPtr>();
            for (int i = 0; i < count; i++)
            {
                if (arr[i].dwType != Native.RIM_TYPEHID) continue;
                seen.Add(arr[i].hDevice);
                if (Devices.ContainsKey(arr[i].hDevice)) continue;
                var d = Describe(arr[i].hDevice);
                if (d != null) Devices[arr[i].hDevice] = d;
            }

            var dead = new List<IntPtr>();
            foreach (var k in Devices.Keys) if (!seen.Contains(k)) dead.Add(k);
            foreach (var k in dead)
            {
                if (Devices[k].Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(Devices[k].Preparsed);
                Devices.Remove(k);
            }
        }

        /// <summary>
        /// Opens the vendor collections that belong to a device which also exposes a
        /// gamepad/joystick collection. That narrow rule keeps us off unrelated hardware
        /// (keyboards, mice, RGB controllers) while still catching the pad's config pipe.
        /// </summary>
        private void StartDirectReaders()
        {
            // A vendor collection is worth opening when it belongs to a device that also
            // exposes a gamepad collection, OR when its vendor id is a known pad maker.
            // The pad enumerates under more than one VID depending on connection mode, and
            // in some modes it exposes the vendor pipe alone - the sibling rule alone missed it.
            var padVids = new HashSet<int>();
            foreach (var d in Devices.Values)
                if (d.UsagePage == 1 && (d.Usage == 4 || d.Usage == 5)) padVids.Add((d.Vid << 16) | d.Pid);

            foreach (var d in Devices.Values)
            {
                if (!d.IsVendorPage) continue;
                if (!padVids.Contains((d.Vid << 16) | d.Pid) && !IsKnownPadVendor(d.Vid)) continue;
                if (_readers.ContainsKey(d.Handle)) continue;
                if (string.IsNullOrEmpty(d.Path)) continue;

                var dev = d;
                int len = d.InputReportLength > 0 ? d.InputReportLength : 65;
                var rdr = new DirectReader(d.Path, len, delegate (byte[] report)
                {
                    lock (_inboxLock) _inbox.Enqueue(new KeyValuePair<HidDevice, byte[]>(dev, report));
                });

                // The pad ignores everything until it has been greeted, then subscribed to.
                // Sent only while it is silent: once the stream runs we stay off the wire,
                // because the replies to these requests travel on the same pipe and are not
                // state reports. Reading them as state made a held button flicker.
                byte[] wake = RequestFor(d, CMD_WAKE);
                byte[] sub = RequestFor(d, CMD_SUBSCRIBE, 0x08, 0xA8);
                if (wake != null) rdr.AddPoll(wake, 700, 1500);
                if (sub != null) rdr.AddPoll(sub, 700, 1500);

                if (rdr.Start())
                {
                    _readers[d.Handle] = rdr;
                    d.DirectRead = true;
                    Log(string.Format("direct read OPEN: {0} ({1} bytes)", d.Ident, len));
                }
                else
                {
                    _readers[d.Handle] = rdr;   // remember the failure so we do not retry in a loop
                    Log(string.Format("direct read FAILED: {0} - {1}", d.Ident, rdr.LastError));
                }
            }
        }

        /// <summary>
        /// Betop/Beitong pads (VID 0x20BC) never report on their own: the vendor tool polls
        /// them with a "get key event" request and the pad answers. Report id 0x02, command
        /// byte 0x25 = cmd 0x5 (status) | subcmd 0x2 (key report) &lt;&lt; 4. Sending this
        /// ourselves is what frees us from having to keep the vendor tool running.
        /// </summary>
        /// <summary>Betop/Beitong vendor ids seen from this pad across connection modes.</summary>
        private static bool IsKnownPadVendor(ushort vid)
        {
            return vid == 0x20BC || vid == 0x20DD;
        }

        // Captured off the wire from the vendor tool's connect sequence. The tool never uses
        // the JS-level 0x25 poll on this pad; it sends 0xCD, then 0xA9, after which the pad
        // streams 0x6D state reports on its own - byte 10 of those carries M1/M2.
        public const byte CMD_WAKE = 0xCD;        // connect / hello
        public const byte CMD_SUBSCRIBE = 0xA9;   // start the 0x6D state stream

        /// <summary>
        /// Builds a request in the pad's wire format: report id 0x02, command byte, optional
        /// arguments, and 0x08 everywhere else - the tool pads with 0x08, not zeroes.
        /// </summary>
        private static byte[] RequestFor(HidDevice d, byte cmd, params byte[] tail)
        {
            if (!IsKnownPadVendor(d.Vid)) return null;
            int len = d.OutputReportLength > 0 ? d.OutputReportLength : 65;
            var buf = new byte[len];
            for (int i = 0; i < len; i++) buf[i] = 0x08;
            buf[0] = 0x02;                       // CONFIG_REPORT_ID
            if (len > 1) buf[1] = cmd;
            for (int i = 0; i < tail.Length && 2 + i < len; i++) buf[2 + i] = tail[i];
            return buf;
        }

        private void StopDirectReaders()
        {
            foreach (var r in _readers.Values) r.Stop();
            _readers.Clear();
        }

        private void DrainInbox()
        {
            while (true)
            {
                KeyValuePair<HidDevice, byte[]> item;
                lock (_inboxLock)
                {
                    if (_inbox.Count == 0) return;
                    item = _inbox.Dequeue();
                }
                HandleReport(item.Key, item.Value);
            }
        }

        public string DirectReaderStatus()
        {
            if (_readers.Count == 0) return "no direct read";
            var sb = new StringBuilder();
            foreach (var kv in _readers)
            {
                HidDevice d;
                string name = Devices.TryGetValue(kv.Key, out d) ? d.ShortId : "?";
                sb.Append(string.Format("{0} [{4}]: {1} reports, {2} requests, {5} empty reads{3}; ", name, kv.Value.ReportCount, kv.Value.PollCount,
                    string.IsNullOrEmpty(kv.Value.LastError) ? "" : " (" + kv.Value.LastError + ")", kv.Value.OpenMode, kv.Value.ZeroReads));
            }
            return sb.ToString();
        }

        public static HidDevice Describe(IntPtr h)
        {
            var d = new HidDevice();
            d.Handle = h;

            uint size = 0;
            Native.GetRawInputDeviceInfoW(h, Native.RIDI_DEVICENAME, IntPtr.Zero, ref size);
            if (size > 0 && size < 4096)
            {
                IntPtr p = Marshal.AllocHGlobal((int)size * 2);
                try
                {
                    if (Native.GetRawInputDeviceInfoW(h, Native.RIDI_DEVICENAME, p, ref size) != unchecked((uint)-1))
                        d.Path = Marshal.PtrToStringUni(p) ?? "";
                }
                finally { Marshal.FreeHGlobal(p); }
            }

            var info = new Native.RID_DEVICE_INFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(Native.RID_DEVICE_INFO));
            uint isz = info.cbSize;
            IntPtr ip = Marshal.AllocHGlobal((int)isz);
            try
            {
                Marshal.StructureToPtr(info, ip, false);
                if (Native.GetRawInputDeviceInfoW(h, Native.RIDI_DEVICEINFO, ip, ref isz) == unchecked((uint)-1)) return null;
                info = (Native.RID_DEVICE_INFO)Marshal.PtrToStructure(ip, typeof(Native.RID_DEVICE_INFO));
            }
            finally { Marshal.FreeHGlobal(ip); }

            d.Vid = (ushort)info.hid.dwVendorId;
            d.Pid = (ushort)info.hid.dwProductId;
            d.UsagePage = info.hid.usUsagePage;
            d.Usage = info.hid.usUsage;

            uint psz = 0;
            Native.GetRawInputDeviceInfoW(h, Native.RIDI_PREPARSEDDATA, IntPtr.Zero, ref psz);
            if (psz > 0)
            {
                IntPtr pre = Marshal.AllocHGlobal((int)psz);
                if (Native.GetRawInputDeviceInfoW(h, Native.RIDI_PREPARSEDDATA, pre, ref psz) != unchecked((uint)-1))
                {
                    d.Preparsed = pre;
                    Native.HIDP_CAPS caps;
                    if (Native.HidP_GetCaps(pre, out caps) == Native.HIDP_STATUS_SUCCESS)
                    {
                        d.InputReportLength = caps.InputReportByteLength;
                        d.OutputReportLength = caps.OutputReportByteLength;
                    }
                    int max = Native.HidP_MaxUsageListLength(Native.HidP_Input, Native.HID_USAGE_PAGE_BUTTON, pre);
                    if (max > 0 && max < 1024) d.UsageBuf = new ushort[max];
                }
                else Marshal.FreeHGlobal(pre);
            }

            return d;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOW_SETTINGS && !_learn) { ShowSettings(); return; }
            if (m.Msg == Native.WM_INPUT) OnRawInput(m.LParam);
            else if (m.Msg == Native.WM_INPUT_DEVICE_CHANGE) { RefreshDevices(); RegisterRawInput(); StartDirectReaders(); }
            base.WndProc(ref m);
        }

        private void OnRawInput(IntPtr hRawInput)
        {
            uint size = 0;
            if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, IntPtr.Zero, ref size, (uint)_headerSize) == unchecked((uint)-1)) return;
            if (size == 0) return;

            if (_buf == IntPtr.Zero || _bufSize < (int)size)
            {
                if (_buf != IntPtr.Zero) Marshal.FreeHGlobal(_buf);
                _bufSize = (int)size + 64;
                _buf = Marshal.AllocHGlobal(_bufSize);
            }

            uint got = size;
            if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, _buf, ref got, (uint)_headerSize) == unchecked((uint)-1)) return;

            if ((uint)Marshal.ReadInt32(_buf, 0) != Native.RIM_TYPEHID) return;
            IntPtr hDev = Marshal.ReadIntPtr(_buf, 8);

            HidDevice dev;
            if (!Devices.TryGetValue(hDev, out dev))
            {
                RefreshDevices();
                if (!Devices.TryGetValue(hDev, out dev)) return;
            }
            if (dev.DirectRead) return; // our ReadFile loop already delivers this device

            // Bail out before allocating and copying anything for devices nobody watches.
            if (!_learn && _cap == null && !DeviceIsInteresting(dev)) return;

            int sizeHid = Marshal.ReadInt32(_buf, _headerSize);
            int cnt = Marshal.ReadInt32(_buf, _headerSize + 4);
            if (sizeHid <= 0 || cnt <= 0) return;

            int dataOff = _headerSize + 8;
            var report = new byte[sizeHid];
            for (int i = 0; i < cnt; i++)
            {
                Marshal.Copy(new IntPtr(_buf.ToInt64() + dataOff + i * sizeHid), report, 0, sizeHid);
                HandleReport(dev, report);
            }
        }

        #endregion

        #region Report handling

        private static HashSet<int> ParseButtons(HidDevice dev, byte[] report)
        {
            var set = new HashSet<int>();
            if (dev.Preparsed == IntPtr.Zero || dev.UsageBuf.Length == 0) return set;
            int len = dev.UsageBuf.Length;
            int st = Native.HidP_GetUsages(Native.HidP_Input, Native.HID_USAGE_PAGE_BUTTON, 0,
                                           dev.UsageBuf, ref len, dev.Preparsed, report, report.Length);
            if (st != Native.HIDP_STATUS_SUCCESS) return set;
            for (int i = 0; i < len; i++) set.Add(dev.UsageBuf[i]);
            return set;
        }

        /// <summary>
        /// True when some enabled rule actually looks at this device. The pad's gamepad
        /// collection streams ~1100 reports a second; parsing those when nothing subscribes
        /// to them burned several percent of a core for nothing.
        /// </summary>
        private bool DeviceIsInteresting(HidDevice dev)
        {
            foreach (var r in Rules)
                if (r.Enabled && r.Trig.IsSet && r.Trig.MatchesDevice(dev)) return true;
            return false;
        }

        private void HandleReport(HidDevice dev, byte[] report)
        {
            if (!_learn && _cap == null && !DeviceIsInteresting(dev)) return;

            if (dev.IsVendorPage)
            {
                _lastVendorReport = DateTime.UtcNow;
                if (_vendorWarned)
                {
                    _vendorWarned = false;
                    if (_tray != null) _tray.Text = "PadKey";
                }
            }

            var buttons = ParseButtons(dev, report);

            if (_learn) LearnLog(dev, report, buttons);
            if (_cap != null) FeedCapture(dev, report, buttons);

            if (!_learn)
            {
                foreach (var r in Rules)
                {
                    if (!r.Enabled || !r.Trig.IsSet) { r.Active = false; continue; }
                    if (!r.Trig.MatchesDevice(dev)) continue;
                    if (!r.Trig.Applies(report)) continue;   // wrong report type: not state, so say nothing

                    // Press is taken immediately (no added latency); release has to stay
                    // stable for DebounceMs. The pad drops occasional 0x00 frames mid-press,
                    // and without this each dropout looked like release+press = double fire.
                    if (r.Trig.Evaluate(report, buttons))
                    {
                        var now = DateTime.UtcNow;
                        if (r.Active) { r.LastActiveSeen = now; continue; }

                        if (r.PendingSince == DateTime.MinValue) { r.PendingSince = now; continue; }
                        if ((now - r.PendingSince).TotalMilliseconds < r.ArmMs) continue;

                        r.Active = true;
                        r.LastActiveSeen = now;
                        r.PressedAt = r.PendingSince;
                        if (r.Trig.ByteIndex >= 0 && r.Trig.ByteIndex < report.Length)
                            r.LastByte = report[r.Trig.ByteIndex];
                        if (_cap == null) Fire(r, true);
                    }
                    else if (!r.Active) r.PendingSince = DateTime.MinValue;   // blip, forget it
                }
            }

            dev.LastButtons = buttons;
            // Only the learn view and the capture compare against the previous report.
            if (_learn || _cap != null) dev.Last = (byte[])report.Clone();
        }

        #endregion

        #region Capture

        public void BeginCapture(Action<List<Candidate>> done, Action<string> status, Action<string> failed)
        {
            _cap = new CaptureSession();
            _cap.BaselineUntil = DateTime.UtcNow.AddSeconds(2.0);
            _cap.Done = done;
            _cap.Status = status;
            _cap.Failed = failed;
            RegisterRawInput();   // capture needs to hear every device again
            if (status != null) status("Hands off: do not touch the pad for 2 seconds...");
        }

        private static string StatusLine(CaptureSession c, string msg)
        {
            return string.Format("{0}\r\n({1} device(s), {2} reports)", msg, c.Counts.Count, c.TotalReports);
        }

        public void CancelCapture() { _cap = null; RegisterRawInput(); }
        public bool Capturing { get { return _cap != null; } }

        private void FeedCapture(HidDevice dev, byte[] report, HashSet<int> buttons)
        {
            var c = _cap;
            if (c == null) return;

            int n;
            c.Counts.TryGetValue(dev.Handle, out n);
            c.Counts[dev.Handle] = n + 1;

            if (!c.BaselineDone)
            {
                byte[] prev;
                if (c.Base.TryGetValue(dev.Handle, out prev) && prev.Length == report.Length)
                {
                    HashSet<int> noisy;
                    if (!c.Noisy.TryGetValue(dev.Handle, out noisy)) { noisy = new HashSet<int>(); c.Noisy[dev.Handle] = noisy; }
                    for (int i = 0; i < report.Length; i++) if (prev[i] != report[i]) noisy.Add(i);
                }
                c.Base[dev.Handle] = (byte[])report.Clone();
                c.BaseButtons[dev.Handle] = new HashSet<int>(buttons);
                return;
            }

            byte[] base_;
            if (!c.Base.TryGetValue(dev.Handle, out base_) || base_.Length != report.Length)
            {
                // Device never reported during the baseline: take this report as its resting state.
                c.Base[dev.Handle] = (byte[])report.Clone();
                c.BaseButtons[dev.Handle] = new HashSet<int>(buttons);
                return;
            }

            HashSet<int> noisyBytes;
            if (!c.Noisy.TryGetValue(dev.Handle, out noisyBytes)) noisyBytes = new HashSet<int>();

            HashSet<int> baseBtn;
            if (!c.BaseButtons.TryGetValue(dev.Handle, out baseBtn)) baseBtn = new HashSet<int>();

            // any bit that differs from the resting report - rising OR falling. Some pads
            // signal a paddle by clearing a bit (0x08 -> 0x00), not by setting one.
            for (int i = 0; i < report.Length; i++)
            {
                if (noisyBytes.Contains(i)) continue;
                int diff = (report[i] ^ base_[i]) & 0xFF;
                if (diff == 0) continue;
                var cand = new Candidate();
                cand.Dev = dev; cand.ByteIndex = i; cand.Mask = diff; cand.Value = report[i] & diff;
                if (report.Length > 1) cand.HeaderByte = report[1];
                if (!c.Found.ContainsKey(cand.Key)) c.Found[cand.Key] = cand;
            }

            foreach (var b in buttons)
            {
                if (baseBtn.Contains(b)) continue;
                var cand = new Candidate();
                cand.Dev = dev; cand.Button = b; cand.Mask = 1;
                if (!c.Found.ContainsKey(cand.Key)) c.Found[cand.Key] = cand;
            }

            // mark release: a real button returns to its resting value
            foreach (var cand in c.Found.Values)
            {
                if (cand.Dev.Handle != dev.Handle) continue;
                if (cand.Button > 0) { if (!buttons.Contains(cand.Button)) cand.Released = true; }
                else if (cand.ByteIndex < report.Length && (report[cand.ByteIndex] & cand.Mask) != cand.Value) cand.Released = true;
            }

            if (c.Found.Count > 0 && c.Deadline == null)
            {
                c.Deadline = DateTime.UtcNow.AddMilliseconds(1200);
                if (c.Status != null) c.Status("Detected, release the button...");
            }
        }

        private void TickCapture()
        {
            var c = _cap;
            if (c == null) return;

            if (!c.BaselineDone && DateTime.UtcNow >= c.BaselineUntil)
            {
                c.BaselineDone = true;
                c.GiveUpAt = DateTime.UtcNow.AddSeconds(12);
                if (c.Status != null) c.Status(StatusLine(c, "Now press and release the button you want to teach."));
                return;
            }

            if (c.BaselineDone && c.Deadline == null)
            {
                if (c.Status != null) c.Status(StatusLine(c, "Now press and release the button you want to teach."));
                if (DateTime.UtcNow >= c.GiveUpAt)
                {
                    _cap = null;
                    if (c.Failed != null)
                        c.Failed(c.TotalReports == 0
                            ? "No HID reports arrived from the pad.\r\n" + DirectReaderStatus()
                            : string.Format("{1} reports from {0} device(s), but this button changed no bit.\r\n{2}", c.Counts.Count, c.TotalReports, DirectReaderStatus()));
                }
                return;
            }

            if (c.Deadline != null && DateTime.UtcNow >= c.Deadline.Value)
            {
                var list = new List<Candidate>(c.Found.Values);
                list.Sort(delegate (Candidate a, Candidate b) { return a.Score().CompareTo(b.Score()); });
                _cap = null;
                RegisterRawInput();
                if (c.Done != null) c.Done(list);
            }
        }

        #endregion

        #region Key injection

        private void Fire(Rule r, bool down)
        {
            if (r.Hold)
            {
                SendKey(r, down);
                LastEvent = string.Format("{0}  {1}  {2}", DateTime.Now.ToString("HH:mm:ss"), r.Name, down ? "DOWN" : "UP");
                return;
            }

            if (!down) return;
            if ((DateTime.UtcNow - r.LastFire).TotalMilliseconds < r.CooldownMs) return;
            r.LastFire = DateTime.UtcNow;
            SendKey(r, true);
            _pendingUp.Add(new KeyValuePair<DateTime, Rule>(DateTime.UtcNow.AddMilliseconds(r.HoldMs), r));
            LastEvent = string.Format("{0}  {1}  ->  {2}", DateTime.Now.ToString("HH:mm:ss"), r.Name, r.KeyText);
            // Milliseconds and the raw byte, so a real double tap can be told apart from a
            // spurious repeat. Only written on transitions, so this costs nothing at rest.
            Log(string.Format("{0}  FIRED    {1} -> {2}   byte=0x{3:X2}",
                DateTime.Now.ToString("HH:mm:ss.fff"), r.Name, r.KeyText, r.LastByte));
        }

        private static bool IsExtended(ushort vk)
        {
            switch (vk)
            {
                case 0xA3: case 0xA5: case 0x2D: case 0x2E: case 0x24: case 0x23:
                case 0x21: case 0x22: case 0x25: case 0x26: case 0x27: case 0x28:
                case 0x90: case 0x6F: case 0x2C: case 0x5B: case 0x5C:
                    return true;
                default: return false;
            }
        }

        private static Native.INPUT MakeKey(ushort vk, bool up)
        {
            var inp = new Native.INPUT();
            inp.type = Native.INPUT_KEYBOARD;
            inp.u.ki.wVk = vk;
            inp.u.ki.wScan = (ushort)Native.MapVirtualKeyW(vk, 0);
            inp.u.ki.dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0);
            inp.u.ki.time = 0;
            inp.u.ki.dwExtraInfo = IntPtr.Zero;
            return inp;
        }

        public static void SendKey(Rule r, bool down)
        {
            if (r.Key == 0) return;
            var list = new List<Native.INPUT>();
            if (down)
            {
                foreach (var m in r.Mods) list.Add(MakeKey(m, false));
                list.Add(MakeKey(r.Key, false));
            }
            else
            {
                list.Add(MakeKey(r.Key, true));
                for (int i = r.Mods.Length - 1; i >= 0; i--) list.Add(MakeKey(r.Mods[i], true));
            }
            var arr = list.ToArray();
            Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        /// <summary>
        /// The pad's vendor collection only streams while the vendor's own tool is running.
        /// If it goes quiet the paddles stop working with no visible error, so say so.
        /// </summary>
        private void CheckVendorSilence()
        {
            if (_learn || _vendorWarned || _tray == null) return;

            bool needsVendor = false;
            foreach (var r in Rules)
                if (r.Enabled && r.Trig.IsSet && r.Trig.UsagePage >= 0xF0) { needsVendor = true; break; }
            if (!needsVendor) return;

            if ((DateTime.UtcNow - _lastVendorReport).TotalSeconds < 15) return;

            _vendorWarned = true;
            _tray.Text = "PadKey - vendor interface is silent";
            Notify("PadKey", "The pad's vendor interface sends no data, so the back buttons cannot be read. "
                           + "Unplug and replug the pad, or run the vendor tool once.");
        }

        /// <summary>Confirms releases once the trigger has stayed inactive long enough.</summary>
        private void TickRelease()
        {
            var now = DateTime.UtcNow;
            foreach (var r in Rules)
            {
                if (!r.Active) continue;
                if ((now - r.LastActiveSeen).TotalMilliseconds < r.DebounceMs) continue;
                r.Active = false;
                r.PendingSince = DateTime.MinValue;
                Log(string.Format("{0}  released {1}   held {2:N0} ms",
                    DateTime.Now.ToString("HH:mm:ss.fff"), r.Name, (r.LastActiveSeen - r.PressedAt).TotalMilliseconds));
                if (_cap == null) Fire(r, false);
            }
        }

        private DateTime _learnStatusAt = DateTime.MinValue;

        private DateTime _nextReaderScan = DateTime.MinValue;

        /// <summary>
        /// Safety net for the pad arriving late or a first open failing during boot. Costs
        /// nothing while a reader is alive: it only rescans when there is none.
        /// </summary>
        private void TickReaders()
        {
            if (_learn) return;
            if (DateTime.UtcNow < _nextReaderScan) return;
            _nextReaderScan = DateTime.UtcNow.AddSeconds(3);

            bool wantsVendor = false;
            foreach (var r in Rules)
                if (r.Enabled && r.Trig.IsSet && r.Trig.UsagePage >= 0xF0) { wantsVendor = true; break; }
            if (!wantsVendor) return;

            var dead = new List<IntPtr>();
            bool alive = false;
            foreach (var kv in _readers)
            {
                if (kv.Value.Failed) dead.Add(kv.Key);
                else alive = true;
            }
            if (alive && dead.Count == 0) return;

            foreach (var k in dead)
            {
                Log("direct read died (" + _readers[k].LastError + "), retrying");
                try { _readers[k].Stop(); } catch { }
                _readers.Remove(k);
            }

            RefreshDevices();
            StartDirectReaders();
            RegisterRawInput();
        }

        private void OnTimer(object sender, EventArgs e)
        {
            TickReaders();
            if (_learn)
            {
                if (_learnStatusAt == DateTime.MinValue) _learnStatusAt = DateTime.UtcNow.AddSeconds(3);
                else if (_learnStatusAt != DateTime.MaxValue && DateTime.UtcNow >= _learnStatusAt)
                {
                    _learnStatusAt = DateTime.MaxValue;
                    Log("direct read status: " + DirectReaderStatus());
                }
            }

            DrainInbox();
            TickRelease();
            CheckVendorSilence();
            TickCapture();

            if (_pendingUp.Count == 0) return;
            var now = DateTime.UtcNow;
            for (int i = _pendingUp.Count - 1; i >= 0; i--)
            {
                if (_pendingUp[i].Key <= now)
                {
                    SendKey(_pendingUp[i].Value, false);
                    _pendingUp.RemoveAt(i);
                }
            }
        }

        #endregion

        #region Learn console mode

        private void LearnLog(HidDevice dev, byte[] report, HashSet<int> buttons)
        {
            if (_learnVidFilter >= 0 && dev.Vid != _learnVidFilter) return;

            HashSet<int> noisy;
            if (!_learnNoisy.TryGetValue(dev.Handle, out noisy)) { noisy = new HashSet<int>(); _learnNoisy[dev.Handle] = noisy; }

            if (dev.Last == null || dev.Last.Length != report.Length)
            {
                Log(string.Format("[{0}] first report, {1} bytes: {2}", dev.Ident, report.Length, Hex(report)));
                Log("    path: " + dev.Path);
                return;
            }

            var changed = new List<int>();
            for (int i = 0; i < report.Length; i++) if (dev.Last[i] != report[i]) changed.Add(i);

            if (DateTime.UtcNow < _learnBaselineUntil)
            {
                foreach (var i in changed) noisy.Add(i);
                return;
            }

            var interesting = new List<int>();
            foreach (var i in changed) if (!noisy.Contains(i)) interesting.Add(i);

            bool btnChanged = !SetEquals(buttons, dev.LastButtons);
            if (interesting.Count == 0 && !btnChanged) return;

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("  ").Append(dev.Ident).Append("  bytes:");
            foreach (var i in interesting)
                sb.Append(string.Format(" [{0}]={1:X2}({2})", i, report[i], Bits(report[i])));
            if (btnChanged)
            {
                sb.Append("  buttons:");
                if (buttons.Count == 0) sb.Append(" (none)");
                var l = new List<int>(buttons); l.Sort();
                foreach (var b in l) sb.Append(" ").Append(b);
            }
            sb.Append("   raw=").Append(Hex(report));
            Log(sb.ToString());
        }

        private static bool SetEquals(HashSet<int> a, HashSet<int> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var x in a) if (!b.Contains(x)) return false;
            return true;
        }

        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 3);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }

        private static string Bits(byte v)
        {
            var sb = new StringBuilder(8);
            for (int i = 7; i >= 0; i--) sb.Append(((v >> i) & 1) != 0 ? '1' : '0');
            return sb.ToString();
        }

        #endregion

        #region Tray / settings

        private void SetupTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Settings...", null, delegate { ShowSettings(); });
            menu.Items.Add("Diagnostics", null, delegate
            {
                var sb = new StringBuilder();
                sb.AppendLine("HID devices: " + Devices.Count);
                sb.AppendLine("Registered usages: " + string.Join(" ", RegisteredPairs.ToArray()));
                sb.AppendLine();
                sb.AppendLine("Direct read: " + DirectReaderStatus());
                sb.AppendLine();
                sb.AppendLine("Son olay: " + LastEvent);
                MessageBox.Show(sb.ToString(), "PadKey");
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { _tray.Visible = false; Application.Exit(); });

            _tray = new NotifyIcon();
            _tray.Icon = Theme.AppIcon(SystemInformation.SmallIconSize.Width);
            _tray.Text = "PadKey";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowSettings(); };
        }

        /// <summary>Switches the live rule set to another profile without restarting.</summary>
        public void LoadProfile(string name)
        {
            Profiles.Active = name;
            Config.LoadFrom(Profiles.ActivePath, Rules);
            RegisterRawInput();   // a different rule set may need different devices
        }

        public void ShowSettings()
        {
            if (_settings != null && !_settings.IsDisposed) { _settings.Activate(); return; }
            _settings = new SettingsForm(this);
            // Rules stay live while the window is open, so edits take effect immediately.
            // Only an in-progress capture suppresses output.
            _settings.FormClosed += delegate { CancelCapture(); _settings = null; };
            _settings.Show();
        }

        public void Notify(string title, string text)
        {
            if (_tray == null) return;
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = text;
            _tray.ShowBalloonTip(2500);
        }

        #endregion

        #region Logging

        private static StreamWriter _logFile;

        public static string ExeDir { get { return System.IO.Path.GetDirectoryName(Application.ExecutablePath); } }

        /// <summary>
        /// Settings live in %APPDATA%\PadKey, not next to the exe, so a single padkey.exe can
        /// sit anywhere (Desktop included) without scattering files around it.
        /// </summary>
        public static string DataDir
        {
            get
            {
                string d = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PadKey");
                try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static void Log(string s)
        {
            try { Console.WriteLine(s); } catch { }
            try
            {
                if (_logFile == null)
                {
                    // Append rather than truncate: intermittent faults only show up when the
                    // history survives a restart. Capped so it cannot grow without bound.
                    string path = System.IO.Path.Combine(DataDir, "padkey-log.txt");
                    try
                    {
                        var fi = new FileInfo(path);
                        if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    }
                    catch { }
                    _logFile = new StreamWriter(path, true, new UTF8Encoding(false));
                    _logFile.AutoFlush = true;
                    _logFile.WriteLine("--- PadKey started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ---");
                }
                _logFile.WriteLine(s);
            }
            catch { }
        }

        #endregion
    }

    #region Config file / profiles

    internal static class Config
    {
        public static void LoadFrom(string path, List<Rule> rules)
        {
            rules.Clear();
            if (!File.Exists(path)) { Defaults(rules); return; }

            Rule cur = null;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    if (cur != null) rules.Add(cur);
                    cur = new Rule();
                    cur.Name = line.Trim('[', ']');
                    continue;
                }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                try
                {
                    switch (k)
                    {
                        case "vid": cur.Trig.Vid = Num(v); break;
                        case "pid": cur.Trig.Pid = Num(v); break;
                        case "usagepage": cur.Trig.UsagePage = Num(v); break;
                        case "usage": cur.Trig.Usage = Num(v); break;
                        case "button": cur.Trig.Button = Num(v); break;
                        case "byte": cur.Trig.ByteIndex = Num(v); break;
                        case "mask": cur.Trig.Mask = Num(v); break;
                        case "value": cur.Trig.Value = Num(v); break;
                        case "devlabel": cur.Trig.DevLabel = v; break;
                        case "gate_byte": cur.Trig.GateByte = Num(v); break;
                        case "gate_mask": cur.Trig.GateMask = Num(v); break;
                        case "gate_value": cur.Trig.GateValue = Num(v); break;
                        case "key": ParseKey(v, cur); break;
                        case "mode": cur.Hold = v.Equals("hold", StringComparison.OrdinalIgnoreCase); break;
                        case "hold_ms": cur.HoldMs = Num(v); break;
                        case "cooldown_ms": cur.CooldownMs = Num(v); break;
                        case "debounce_ms": cur.DebounceMs = Num(v); break;
                        case "arm_ms": cur.ArmMs = Num(v); break;
                        case "enabled": cur.Enabled = v != "0" && !v.Equals("false", StringComparison.OrdinalIgnoreCase); break;
                    }
                }
                catch (Exception ex) { PadKeyForm.Log(System.IO.Path.GetFileName(path) + ": " + ex.Message); }
            }
            if (cur != null) rules.Add(cur);
            if (rules.Count == 0) Defaults(rules);
        }

        public static void Defaults(List<Rule> rules)
        {
            var a = new Rule(); a.Name = "Back button 1"; a.Key = 0x7B; // F12
            var b = new Rule(); b.Name = "Back button 2"; b.Key = 0x74; // F5
            rules.Add(a); rules.Add(b);
        }

        public static void SaveTo(string path, List<Rule> rules)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; PadKey profile - edited from within the app");
            sb.AppendLine();
            foreach (var r in rules)
            {
                sb.AppendLine("[" + r.Name + "]");
                sb.AppendLine("enabled = " + (r.Enabled ? "1" : "0"));
                if (r.Trig.Vid >= 0) sb.AppendLine("vid = 0x" + r.Trig.Vid.ToString("X4"));
                if (r.Trig.Pid >= 0) sb.AppendLine("pid = 0x" + r.Trig.Pid.ToString("X4"));
                if (r.Trig.UsagePage >= 0) sb.AppendLine("usagepage = 0x" + r.Trig.UsagePage.ToString("X2"));
                if (r.Trig.Usage >= 0) sb.AppendLine("usage = 0x" + r.Trig.Usage.ToString("X2"));
                if (r.Trig.Button > 0) sb.AppendLine("button = " + r.Trig.Button);
                if (r.Trig.ByteIndex >= 0)
                {
                    sb.AppendLine("byte = " + r.Trig.ByteIndex);
                    sb.AppendLine("mask = 0x" + r.Trig.Mask.ToString("X2"));
                    sb.AppendLine("value = 0x" + r.Trig.ActiveValue.ToString("X2"));
                }
                if (r.Trig.GateByte >= 0)
                {
                    sb.AppendLine("gate_byte = " + r.Trig.GateByte);
                    sb.AppendLine("gate_mask = 0x" + r.Trig.GateMask.ToString("X2"));
                    sb.AppendLine("gate_value = 0x" + r.Trig.GateValue.ToString("X2"));
                }
                if (!string.IsNullOrEmpty(r.Trig.DevLabel)) sb.AppendLine("devlabel = " + r.Trig.DevLabel);
                sb.AppendLine("key = " + r.KeyText);
                sb.AppendLine("mode = " + (r.Hold ? "hold" : "tap"));
                sb.AppendLine("hold_ms = " + r.HoldMs);
                sb.AppendLine("cooldown_ms = " + r.CooldownMs);
                sb.AppendLine("debounce_ms = " + r.DebounceMs);
                sb.AppendLine("arm_ms = " + r.ArmMs);
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static int Num(string v)
        {
            v = v.Trim();
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(v.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return int.Parse(v, CultureInfo.InvariantCulture);
        }

        private static void ParseKey(string spec, Rule r)
        {
            var parts = spec.Split('+');
            var mods = new List<ushort>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (i < parts.Length - 1) mods.Add(Keys_.FromName(p));
                else r.Key = Keys_.FromName(p);
            }
            r.Mods = mods.ToArray();
        }
    }

    /// <summary>
    /// Named rule sets under profiles\*.ini. padkey.ini keeps only which one is active,
    /// so an older padkey.ini full of rules is migrated into the first profile on startup.
    /// </summary>
    internal static class Profiles
    {
        public const string DefaultName = "Default";

        public static string Dir { get { return System.IO.Path.Combine(PadKeyForm.DataDir, "profiles"); } }
        public static string StatePath { get { return System.IO.Path.Combine(PadKeyForm.DataDir, "padkey.ini"); } }
        public static string PathOf(string name) { return System.IO.Path.Combine(Dir, Sanitize(name) + ".ini"); }
        public static string ActivePath { get { return PathOf(Active); } }

        private static string _active;

        public static string Sanitize(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name.Trim())
                sb.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim();
            return s.Length == 0 ? DefaultName : s;
        }

        public static List<string> All()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(Dir))
                    foreach (var f in Directory.GetFiles(Dir, "*.ini"))
                        list.Add(System.IO.Path.GetFileNameWithoutExtension(f));
            }
            catch { }
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return list;
        }

        private static bool _stateLoaded;

        private static void LoadState()
        {
            if (_stateLoaded) return;
            _stateLoaded = true;
            try
            {
                if (!File.Exists(StatePath)) return;
                foreach (var raw in File.ReadAllLines(StatePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "profile") _active = v;
                }
            }
            catch { }
        }

        private static void SaveState()
        {
            try
            {
                File.WriteAllText(StatePath,
                    "; PadKey - active profile. Rules live under profiles\\\r\n" +
                    "[padkey]\r\nprofile = " + (_active ?? DefaultName) + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        public static string Active
        {
            get
            {
                LoadState();
                if (string.IsNullOrEmpty(_active)) _active = DefaultName;
                return _active;
            }
            set
            {
                LoadState();
                _active = value;
                SaveState();
            }
        }

        public static void Init()
        {
            try { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); } catch { }

            // Older builds kept everything beside the exe. Bring that across once.
            try
            {
                string oldDir = System.IO.Path.Combine(PadKeyForm.ExeDir, "profiles");
                if (Directory.Exists(oldDir) && Directory.GetFiles(Dir, "*.ini").Length == 0)
                {
                    foreach (var f in Directory.GetFiles(oldDir, "*.ini"))
                        File.Copy(f, System.IO.Path.Combine(Dir, System.IO.Path.GetFileName(f)), false);

                    string oldState = System.IO.Path.Combine(PadKeyForm.ExeDir, "padkey.ini");
                    if (File.Exists(oldState) && !File.Exists(StatePath)) File.Copy(oldState, StatePath, false);
                    PadKeyForm.Log("settings migrated to " + Dir);
                }
            }
            catch (Exception ex) { PadKeyForm.Log("settings migration error: " + ex.Message); }

            // Legacy padkey.ini held the rules themselves - move them into a profile.
            try
            {
                if (File.Exists(StatePath))
                {
                    string txt = File.ReadAllText(StatePath, Encoding.UTF8);
                    if (txt.IndexOf("[padkey]", StringComparison.OrdinalIgnoreCase) < 0 && txt.IndexOf('[') >= 0)
                    {
                        string dest = PathOf(DefaultName);
                        if (!File.Exists(dest)) File.Copy(StatePath, dest, false);
                        Active = DefaultName;
                        PadKeyForm.Log("old padkey.ini moved into profiles\\" + DefaultName + ".ini");
                    }
                }
            }
            catch (Exception ex) { PadKeyForm.Log("profile migration error: " + ex.Message); }

            if (All().Count == 0)
            {
                var rules = new List<Rule>();
                Config.Defaults(rules);
                Config.SaveTo(PathOf(DefaultName), rules);
                Active = DefaultName;
            }

            if (!File.Exists(ActivePath))
            {
                var list = All();
                if (list.Count > 0) Active = list[0];
            }
        }

        public static void Delete(string name)
        {
            try { File.Delete(PathOf(name)); } catch { }
        }

        public static void Rename(string oldName, string newName)
        {
            try
            {
                string src = PathOf(oldName), dst = PathOf(newName);
                if (File.Exists(src) && !File.Exists(dst)) File.Move(src, dst);
            }
            catch { }
        }
    }

    #endregion

    #region Dark theme

    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(0x14, 0x16, 0x1B);
        public static readonly Color Panel = Color.FromArgb(0x1B, 0x1E, 0x25);
        public static readonly Color Card = Color.FromArgb(0x20, 0x24, 0x2C);
        public static readonly Color Input = Color.FromArgb(0x28, 0x2D, 0x37);
        public static readonly Color Border = Color.FromArgb(0x33, 0x38, 0x45);
        public static readonly Color Text = Color.FromArgb(0xE8, 0xEB, 0xF1);
        public static readonly Color Dim = Color.FromArgb(0x8C, 0x94, 0xA4);
        public static readonly Color Accent = Color.FromArgb(0x5B, 0x9D, 0xF9);
        public static readonly Color AccentDim = Color.FromArgb(0x2C, 0x4A, 0x78);
        public static readonly Color Good = Color.FromArgb(0x46, 0xC4, 0x6B);
        public static readonly Color Bad = Color.FromArgb(0xF0, 0x68, 0x5F);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, 4);
        }

        /// <summary>
        /// Pulls the icon embedded by /win32icon at the exact size asked for, so the tray
        /// gets a crisp 16px version instead of a downscaled 32px one. LR_SHARED means the
        /// handle is cached by the system and must not be destroyed.
        /// </summary>
        public static Icon AppIcon(int size)
        {
            try
            {
                IntPtr h = Native.LoadImageW(Native.GetModuleHandleW(null),
                    new IntPtr(Native.IDI_APPLICATION_RES), Native.IMAGE_ICON, size, size, Native.LR_SHARED);
                if (h != IntPtr.Zero) return Icon.FromHandle(h);
            }
            catch { }
            return SystemIcons.Application;
        }

        public static Label Caption(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, 18);
            l.ForeColor = Dim;
            l.BackColor = Color.Transparent;
            l.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            return l;
        }

        public static TextBox Input_(int x, int y, int w)
        {
            var t = new TextBox();
            t.SetBounds(x, y, w, 26);
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Input;
            t.ForeColor = Text;
            return t;
        }

        public static Button Btn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.ForeColor = primary ? Color.White : Text;
            b.BackColor = primary ? Accent : Card;
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(0x74, 0xAF, 0xFF) : Color.FromArgb(0x2C, 0x31, 0x3C);
            b.FlatAppearance.MouseDownBackColor = primary
                ? Color.FromArgb(0x3F, 0x84, 0xE0) : Color.FromArgb(0x36, 0x3C, 0x49);
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            return b;
        }

        public static CheckBox Check(string text, int x, int y, int w)
        {
            var c = new DarkCheck();
            c.Text = text;
            c.SetBounds(x, y, w, 24);
            return c;
        }

        public static ComboBox Combo(int x, int y, int w)
        {
            var c = new DarkCombo();
            c.SetBounds(x, y, w, 26);
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Input;
            c.ForeColor = Text;
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.ItemHeight = 20;
            return c;
        }

        /// <summary>
        /// WinForms paints the checkbox glyph and the combo drop button with system colours
        /// that stay light no matter what BackColor is set, so both are drawn by hand.
        /// </summary>
        public class DarkCheck : CheckBox
        {
            public DarkCheck()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                ForeColor = Theme.Text;   // Control.Text would shadow the theme colour here
                Cursor = Cursors.Hand;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent != null ? Parent.BackColor : Card);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                var box = new Rectangle(0, (Height - 16) / 2, 16, 16);
                using (var b = new SolidBrush(Checked ? Accent : Input)) g.FillRectangle(b, box);
                using (var p = new Pen(Checked ? Accent : Border)) g.DrawRectangle(p, box);

                if (Checked)
                    using (var p = new Pen(Color.White, 2f))
                        g.DrawLines(p, new Point[] {
                            new Point(box.X + 4, box.Y + 8),
                            new Point(box.X + 7, box.Y + 11),
                            new Point(box.X + 12, box.Y + 5) });

                using (var b = new SolidBrush(Enabled ? ForeColor : Dim))
                    g.DrawString(Text, Font, b, box.Right + 8, (Height - Font.Height) / 2f);
            }
        }

        public class DarkCombo : ComboBox
        {
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0x000F) return;   // WM_PAINT
                using (var g = Graphics.FromHwnd(Handle))
                {
                    const int bw = 20;
                    var btn = new Rectangle(Width - bw - 1, 1, bw, Height - 2);
                    using (var b = new SolidBrush(Input)) g.FillRectangle(b, btn);

                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    int cx = btn.X + btn.Width / 2, cy = btn.Y + btn.Height / 2;
                    using (var b = new SolidBrush(Dim))
                        g.FillPolygon(b, new Point[] {
                            new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) });

                    using (var pen = new Pen(Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
            }
        }

        /// <summary>Flat dark panel with a 1px border, used as a card background.</summary>
        public static Panel CardPanel(int x, int y, int w, int h)
        {
            var p = new Panel();
            p.SetBounds(x, y, w, h);
            p.BackColor = Card;
            p.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }
    }

    #endregion

    #region Settings window

    internal class SettingsForm : Form
    {
        private readonly PadKeyForm _core;

        private readonly ComboBox _profile = Theme.Combo(0, 0, 10);
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _name = new TextBox();
        private readonly Label _trigger = new Label();
        private readonly Label _status = new Label();
        private readonly TextBox _key = new TextBox();
        private readonly ComboBox _mode = Theme.Combo(0, 0, 10);
        private readonly CheckBox _enabled = new Theme.DarkCheck();
        private readonly Label _lamp = new Label();
        private Button _btnAlt;
        private CheckBox _autostart;
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer();

        private bool _grabKey;
        private bool _loading;
        private bool _dirty;
        private DateTime _dirtySince;

        private Rule Current
        {
            get { return _list.SelectedIndex >= 0 && _list.SelectedIndex < _core.Rules.Count ? _core.Rules[_list.SelectedIndex] : null; }
        }

        public SettingsForm(PadKeyForm core)
        {
            _core = core;

            Text = "PadKey";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(772, 508);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            KeyPreview = true;
            Icon = Theme.AppIcon(SystemInformation.IconSize.Width);
            ShowIcon = true;

            BuildHeader();
            BuildLeft();
            BuildRight();
            BuildFooter();

            RefreshProfiles();
            RefreshList();
            if (_core.Rules.Count > 0) _list.SelectedIndex = 0; else LoadCurrent();

            _tick.Interval = 60;
            _tick.Tick += delegate { UpdateLamp(); AutoSave(); };
            _tick.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        #region layout

        private void BuildHeader()
        {
            var head = new Panel();
            head.SetBounds(0, 0, ClientSize.Width, 58);
            head.BackColor = Theme.Panel;
            Controls.Add(head);

            var title = new Label();
            title.Text = "PadKey";
            title.SetBounds(20, 16, 86, 26);
            title.ForeColor = Theme.Text;
            title.BackColor = Color.Transparent;
            title.Font = new Font("Segoe UI Semibold", 13f);
            head.Controls.Add(title);

            var sub = new Label();
            sub.Text = "gamepad \u2192 keyboard";
            sub.SetBounds(112, 23, 130, 20);   // stops short of the ? button at x=250
            sub.ForeColor = Theme.Dim;
            sub.BackColor = Color.Transparent;
            head.Controls.Add(sub);

            var help = Theme.Btn("?", 250, 17, 26, 26, false);
            help.Font = new Font("Segoe UI Semibold", 9f);
            help.Click += delegate { ShowHelp(); };
            head.Controls.Add(help);

            var pl = Theme.Caption("Profile", 404, 20, 44);
            head.Controls.Add(pl);

            _profile.SetBounds(448, 16, 156, 26);
            _profile.DrawItem += DrawComboItem;
            _profile.SelectedIndexChanged += delegate
            {
                if (_loading || _profile.SelectedItem == null) return;
                SaveNow();
                _core.LoadProfile(_profile.SelectedItem.ToString());
                RefreshList();
                if (_core.Rules.Count > 0) _list.SelectedIndex = 0; else LoadCurrent();
            };
            head.Controls.Add(_profile);

            var bNew = Theme.Btn("New", 612, 16, 68, 26, false);
            bNew.Click += delegate { NewProfile(); };
            head.Controls.Add(bNew);

            var bDelP = Theme.Btn("Delete", 688, 16, 64, 26, false);
            bDelP.Click += delegate { DeleteProfile(); };
            head.Controls.Add(bDelP);
        }

        private void BuildLeft()
        {
            var card = Theme.CardPanel(16, 74, 288, 336);
            Controls.Add(card);

            var cap = Theme.Caption("RULES", 12, 10, 200);
            card.Controls.Add(cap);

            _list.SetBounds(1, 32, 286, 303);
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Theme.Card;
            _list.ForeColor = Theme.Text;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 40;
            _list.DrawItem += DrawRuleItem;
            _list.SelectedIndexChanged += delegate { LoadCurrent(); };
            card.Controls.Add(_list);

            var bAdd = Theme.Btn("+ Add", 16, 418, 138, 30, false);
            bAdd.Click += delegate
            {
                var r = new Rule();
                r.Name = "Rule " + (_core.Rules.Count + 1);
                _core.Rules.Add(r);
                RefreshList();
                _list.SelectedIndex = _core.Rules.Count - 1;
                Touch();
            };
            Controls.Add(bAdd);

            var bDel = Theme.Btn("Delete", 166, 418, 138, 30, false);
            bDel.Click += delegate
            {
                int i = _list.SelectedIndex;
                if (i < 0) return;
                _core.Rules.RemoveAt(i);
                RefreshList();
                if (_core.Rules.Count > 0) _list.SelectedIndex = Math.Min(i, _core.Rules.Count - 1);
                else LoadCurrent();
                Touch();
            };
            Controls.Add(bDel);
        }

        private void BuildRight()
        {
            var card = Theme.CardPanel(318, 74, 438, 336);
            Controls.Add(card);
            int x = 16, w = 406;

            card.Controls.Add(Theme.Caption("NAME", x, 12, w));
            _name.SetBounds(x, 32, w, 26);
            _name.BorderStyle = BorderStyle.FixedSingle;
            _name.BackColor = Theme.Input;
            _name.ForeColor = Theme.Text;
            _name.TextChanged += delegate
            {
                if (_loading || Current == null) return;
                Current.Name = _name.Text;
                _list.Invalidate();
                Touch();
            };
            card.Controls.Add(_name);

            card.Controls.Add(Theme.Caption("GAMEPAD TRIGGER", x, 70, w));
            _trigger.SetBounds(x, 90, w, 20);
            _trigger.ForeColor = Theme.Accent;
            _trigger.BackColor = Color.Transparent;
            card.Controls.Add(_trigger);

            var bLearn = Theme.Btn("Learn gamepad button", x, 116, 200, 30, false);
            bLearn.Click += delegate { StartCapture(); };
            card.Controls.Add(bLearn);

            _btnAlt = Theme.Btn("Try another trigger", x + 210, 116, 160, 30, false);
            _btnAlt.Enabled = false;
            _btnAlt.Click += delegate { CycleAlternative(); };
            card.Controls.Add(_btnAlt);

            _status.SetBounds(x, 152, w, 36);
            _status.ForeColor = Theme.Dim;
            _status.BackColor = Color.Transparent;
            card.Controls.Add(_status);

            card.Controls.Add(Theme.Caption("KEYBOARD KEY   (click the box, then press a key)", x, 196, w));
            _key.SetBounds(x, 216, 200, 26);
            _key.BorderStyle = BorderStyle.FixedSingle;
            _key.BackColor = Theme.Input;
            _key.ForeColor = Theme.Text;
            _key.ReadOnly = true;
            _key.Cursor = Cursors.Hand;
            _key.Click += delegate { BeginGrabKey(); };
            _key.Enter += delegate { BeginGrabKey(); };
            card.Controls.Add(_key);

            var bTest = Theme.Btn("Test", x + 210, 216, 100, 26, false);
            bTest.Click += delegate
            {
                var r = Current;
                if (r == null) return;
                PadKeyForm.SendKey(r, true);
                System.Threading.Thread.Sleep(50);
                PadKeyForm.SendKey(r, false);
            };
            card.Controls.Add(bTest);

            card.Controls.Add(Theme.Caption("MODE", x, 254, w));
            _mode.SetBounds(x, 274, 260, 26);
            _mode.DrawItem += DrawComboItem;
            _mode.Items.Add("Tap (for screenshots)");
            _mode.Items.Add("Hold (key stays down)");
            _mode.SelectedIndexChanged += delegate
            {
                if (_loading || Current == null) return;
                Current.Hold = _mode.SelectedIndex == 1;
                Touch();
            };
            card.Controls.Add(_mode);

            _enabled.Text = "Rule enabled";
            _enabled.SetBounds(x, 306, 200, 24);
            _enabled.CheckedChanged += delegate
            {
                if (_loading || Current == null) return;
                Current.Enabled = _enabled.Checked;
                Touch();
            };
            card.Controls.Add(_enabled);

            _lamp.SetBounds(x + 240, 306, 160, 24);
            _lamp.TextAlign = ContentAlignment.MiddleRight;
            _lamp.BackColor = Color.Transparent;
            card.Controls.Add(_lamp);
        }

        private void BuildFooter()
        {
            var bSave = Theme.Btn("Save and close", 492, 418, 140, 32, true);
            bSave.Click += delegate { SaveNow(); Close(); };
            Controls.Add(bSave);

            var bClose = Theme.Btn("Close", 644, 418, 112, 32, false);
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);

            _autostart = Theme.Check("Start with Windows", 18, 452, 220);
            _autostart.Checked = Autostart.IsEnabled;
            _autostart.CheckedChanged += delegate { Autostart.Set(_autostart.Checked); };
            Controls.Add(_autostart);

            var hint = new Label();
            hint.Text = "Changes take effect immediately and are saved automatically.";
            hint.SetBounds(258, 454, 500, 20);
            hint.TextAlign = ContentAlignment.MiddleRight;
            hint.ForeColor = Theme.Dim;
            hint.BackColor = Color.Transparent;
            hint.Font = new Font("Segoe UI", 8.25f);
            Controls.Add(hint);

            const string credit = "Made by Snowman with Claude Code";
            var by = new LinkLabel();
            by.Text = credit;
            by.LinkArea = new LinkArea(credit.IndexOf("Snowman"), "Snowman".Length);
            by.SetBounds(18, 480, 300, 20);   // own row: the checkbox owns y=452
            by.ForeColor = Theme.Dim;
            by.LinkColor = Theme.Accent;
            by.ActiveLinkColor = Color.White;
            by.VisitedLinkColor = Theme.Accent;
            by.LinkBehavior = LinkBehavior.HoverUnderline;
            by.BackColor = Color.Transparent;
            by.Font = new Font("Segoe UI", 8.25f);
            by.LinkClicked += delegate
            {
                try { System.Diagnostics.Process.Start("https://steamcommunity.com/id/thesnowman42/"); }
                catch { }
            };
            Controls.Add(by);
        }

        #endregion

        #region help

        private const string HelpText =
"WHAT THIS IS\r\n" +
"PadKey turns a gamepad button into a real keyboard key. It was built for the back\r\n" +
"paddles of a Beitong/Betop pad, which games cannot see at all, but the trigger\r\n" +
"mechanism is generic and works with other HID gamepads too.\r\n" +
"\r\n" +
"WHY IT IS NEEDED\r\n" +
"The pad reports only two HID collections to Windows: a gamepad (10 buttons) and a\r\n" +
"vendor pipe. There is no keyboard collection, so the pad physically cannot type,\r\n" +
"no matter what its own software is told. The paddles do not appear on the gamepad\r\n" +
"collection either - they exist only on the vendor pipe. So something on the PC has\r\n" +
"to read that pipe and press the key. That is what PadKey does.\r\n" +
"\r\n" +
"HOW IT WORKS\r\n" +
"The vendor pipe stays silent until it is greeted. PadKey sends the same two packets\r\n" +
"the vendor tool sends on connect, then the pad streams its state on its own. Byte 10\r\n" +
"of that stream carries the paddles: 0x08 idle, 0x09 right, 0x0A left. When a rule's\r\n" +
"bit goes high, PadKey injects a real keyboard event with SendInput - indistinguishable\r\n" +
"from a physical key press, which is why Steam's screenshot hotkey accepts it.\r\n" +
"\r\n" +
"It never touches Steam Input and never sits in the input chain, so it cannot cause\r\n" +
"stick drift or lower the polling rate. Nothing is written to the pad's memory.\r\n" +
"\r\n" +
"HOW TO USE IT\r\n" +
"1. Pick a rule on the left. Two are set up already: left paddle to F12 (Steam\r\n" +
"   screenshot), right paddle to F5.\r\n" +
"2. To change the key, click the KEYBOARD KEY box and press the key you want.\r\n" +
"   Ctrl/Shift/Alt combinations work. Esc cancels.\r\n" +
"3. To bind a different gamepad button, click 'Learn gamepad button', take your hands\r\n" +
"   off the pad for two seconds, then press and release the button. If it picks the\r\n" +
"   wrong signal, use 'Try another trigger' and watch the lamp on the right.\r\n" +
"4. Mode: 'Tap' presses and releases once - use this for screenshots. 'Hold' keeps the\r\n" +
"   key down for as long as you hold the pad button.\r\n" +
"5. '+ Add' creates more rules. Profiles at the top keep separate sets of rules.\r\n" +
"\r\n" +
"Changes apply immediately and are saved by themselves. Settings live in\r\n" +
"%APPDATA%\\PadKey, so the exe can sit anywhere on its own.\r\n" +
"\r\n" +
"IMPORTANT: leave the back buttons UNASSIGNED in the pad's own software. If you also\r\n" +
"map them to a gamepad button there, that button reaches the game as well.\r\n" +
"\r\n" +
"GOOD TO KNOW\r\n" +
"- Steam only takes a screenshot with F12 inside a game with the overlay enabled.\r\n" +
"  Pressing F12 on the desktop does nothing, even on a real keyboard.\r\n" +
"- If a game runs as administrator, PadKey has to as well, otherwise Windows blocks\r\n" +
"  the injected key from reaching that window.\r\n" +
"- Closing this window does not quit PadKey; it keeps working from the tray icon.\r\n" +
"  Double-click that icon to get back here, right-click it to exit.\r\n" +
"- 'Start with Windows' launches it straight to the tray without this window.";

        private void ShowHelp()
        {
            using (var f = new Form())
            {
                f.Text = "PadKey - Help";
                f.Font = new Font("Segoe UI", 9f);
                f.ClientSize = new Size(700, 560);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bg;
                f.ForeColor = Theme.Text;
                f.HandleCreated += delegate { Theme.DarkTitleBar(f.Handle); };

                var box = new TextBox();
                box.SetBounds(16, 16, 668, 490);
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Vertical;
                box.BorderStyle = BorderStyle.FixedSingle;
                box.BackColor = Theme.Card;
                box.ForeColor = Theme.Text;
                box.Font = new Font("Segoe UI", 9f);
                box.Text = HelpText;
                box.Select(0, 0);
                f.Controls.Add(box);

                var ok = Theme.Btn("Close", 584, 516, 100, 30, true);
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);
                f.AcceptButton = ok;
                f.CancelButton = ok;
                f.ShowDialog(this);
            }
        }

        #endregion

        #region owner drawing

        private void DrawRuleItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _core.Rules.Count) return;
            var r = _core.Rules[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;

            var g = e.Graphics;
            using (var bg = new SolidBrush(sel ? Theme.AccentDim : Theme.Card))
                g.FillRectangle(bg, e.Bounds);

            if (sel)
                using (var bar = new SolidBrush(Theme.Accent))
                    g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);

            using (var fg = new SolidBrush(r.Enabled ? Theme.Text : Theme.Dim))
            using (var f = new Font("Segoe UI", 9.75f))
                g.DrawString(r.Name, f, fg, e.Bounds.X + 14, e.Bounds.Y + 4);

            using (var sub = new SolidBrush(Theme.Accent))
            using (var f = new Font("Consolas", 9f))
                g.DrawString(r.KeyText, f, sub, e.Bounds.X + 14, e.Bounds.Y + 21);

            if (!r.Trig.IsSet)
                using (var warn = new SolidBrush(Theme.Bad))
                using (var f = new Font("Segoe UI", 8.25f))
                    g.DrawString("unassigned", f, warn, e.Bounds.Right - 78, e.Bounds.Y + 22);
        }

        private void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var cb = (ComboBox)sender;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Theme.AccentDim : Theme.Input))
                e.Graphics.FillRectangle(bg, e.Bounds);
            if (e.Index < 0) return;
            using (var fg = new SolidBrush(Theme.Text))
                e.Graphics.DrawString(cb.Items[e.Index].ToString(), cb.Font, fg, e.Bounds.X + 3, e.Bounds.Y + 2);
        }

        #endregion

        #region profiles

        private void RefreshProfiles()
        {
            _loading = true;
            _profile.Items.Clear();
            foreach (var p in Profiles.All()) _profile.Items.Add(p);
            int i = _profile.Items.IndexOf(Profiles.Active);
            _profile.SelectedIndex = i >= 0 ? i : (_profile.Items.Count > 0 ? 0 : -1);
            _loading = false;
        }

        private void NewProfile()
        {
            string name = Prompt("New profile name:", "Profile " + (Profiles.All().Count + 1));
            if (string.IsNullOrEmpty(name)) return;
            name = Profiles.Sanitize(name);
            if (File.Exists(Profiles.PathOf(name)))
            {
                MessageBox.Show("A profile with that name already exists.", "PadKey");
                return;
            }
            SaveNow();
            var rules = new List<Rule>();
            Config.Defaults(rules);
            Config.SaveTo(Profiles.PathOf(name), rules);
            _core.LoadProfile(name);
            RefreshProfiles();
            RefreshList();
            if (_core.Rules.Count > 0) _list.SelectedIndex = 0;
        }

        private void DeleteProfile()
        {
            if (Profiles.All().Count <= 1)
            {
                MessageBox.Show("The last profile cannot be deleted.", "PadKey");
                return;
            }
            string name = Profiles.Active;
            if (MessageBox.Show("\"" + name + "\" - delete this profile?", "PadKey",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            Profiles.Delete(name);
            var rest = Profiles.All();
            _core.LoadProfile(rest[0]);
            RefreshProfiles();
            RefreshList();
            if (_core.Rules.Count > 0) _list.SelectedIndex = 0;
        }

        private static string Prompt(string caption, string initial)
        {
            using (var f = new Form())
            {
                f.Text = "PadKey";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(340, 122);
                f.MinimizeBox = f.MaximizeBox = false;
                f.BackColor = Theme.Bg;
                f.ForeColor = Theme.Text;
                f.Font = new Font("Segoe UI", 9f);
                f.HandleCreated += delegate { Theme.DarkTitleBar(f.Handle); };

                f.Controls.Add(Theme.Caption(caption, 16, 14, 300));
                var t = Theme.Input_(16, 36, 308);
                t.Text = initial;
                t.SelectAll();
                f.Controls.Add(t);

                var ok = Theme.Btn("OK", 168, 76, 74, 30, true);
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);

                var cancel = Theme.Btn("Cancel", 250, 76, 74, 30, false);
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(cancel);

                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? t.Text : null;
            }
        }

        #endregion

        #region state

        private void RefreshList()
        {
            int sel = _list.SelectedIndex;
            _list.Items.Clear();
            foreach (var r in _core.Rules) _list.Items.Add(r.Name);
            if (sel >= 0 && sel < _list.Items.Count) _list.SelectedIndex = sel;
        }

        private void LoadCurrent()
        {
            _loading = true;
            var r = Current;
            bool on = r != null;
            _name.Enabled = _key.Enabled = _mode.Enabled = _enabled.Enabled = on;
            if (r == null)
            {
                _name.Text = ""; _key.Text = ""; _trigger.Text = ""; _lamp.Text = "";
                _btnAlt.Enabled = false;
                _loading = false;
                return;
            }
            _name.Text = r.Name;
            _key.Text = r.KeyText;
            _trigger.Text = r.Trig.Describe();
            _mode.SelectedIndex = r.Hold ? 1 : 0;
            _enabled.Checked = r.Enabled;
            _btnAlt.Enabled = r.Alternatives.Count > 0;
            _loading = false;
        }

        private void UpdateLamp()
        {
            var r = Current;
            if (r == null) { _lamp.Text = ""; return; }
            string dot = ((char)0x25CF).ToString();
            if (r.Active) { _lamp.Text = dot + " PRESSED"; _lamp.ForeColor = Theme.Good; }
            else { _lamp.Text = dot + " idle"; _lamp.ForeColor = Theme.Border; }
        }

        /// <summary>Marks the config dirty; AutoSave writes it out shortly after.</summary>
        private void Touch()
        {
            _dirty = true;
            _dirtySince = DateTime.UtcNow;
            _list.Invalidate();
        }

        private void AutoSave()
        {
            if (!_dirty) return;
            if ((DateTime.UtcNow - _dirtySince).TotalMilliseconds < 600) return;
            SaveNow();
        }

        private void SaveNow()
        {
            _dirty = false;
            try { Config.SaveTo(Profiles.ActivePath, _core.Rules); }
            catch (Exception ex) { PadKeyForm.Log("save error: " + ex.Message); }
        }

        #endregion

        #region key grab

        private void BeginGrabKey()
        {
            if (_grabKey) return;
            _grabKey = true;
            _key.BackColor = Theme.AccentDim;
            _key.Text = "<press a key...>";
        }

        private void EndGrabKey()
        {
            _grabKey = false;
            _key.BackColor = Theme.Input;
            var r = Current;
            _key.Text = r != null ? r.KeyText : "";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_grabKey)
            {
                const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
                if (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN)
                {
                    ushort vk = (ushort)(keyData & Keys.KeyCode);
                    if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C)
                        return true; // wait for a real key, not a bare modifier

                    var r = Current;
                    if (r != null)
                    {
                        var mods = new List<ushort>();
                        if ((keyData & Keys.Control) != 0) mods.Add(0x11);
                        if ((keyData & Keys.Shift) != 0) mods.Add(0x10);
                        if ((keyData & Keys.Alt) != 0) mods.Add(0x12);
                        if (vk == 0x1B && mods.Count == 0) { EndGrabKey(); return true; } // ESC = cancel
                        r.Mods = mods.ToArray();
                        r.Key = vk;
                        Touch();
                    }
                    EndGrabKey();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        #endregion

        #region gamepad capture

        private void StartCapture()
        {
            var r = Current;
            if (r == null) return;
            _core.RefreshDevices();
            _status.ForeColor = Theme.Dim;
            _core.BeginCapture(delegate (List<Candidate> cands)
            {
                if (IsDisposed) return;
                if (cands.Count == 0)
                {
                    _status.ForeColor = Theme.Bad;
                    _status.Text = "No change detected.";
                    return;
                }
                r.Alternatives = new List<Trigger>();
                foreach (var c in cands) r.Alternatives.Add(c.ToTrigger());
                r.Trig = r.Alternatives[0].Clone();
                _btnAlt.Enabled = cands.Count > 1;
                _trigger.Text = r.Trig.Describe();
                _status.ForeColor = Theme.Dim;
                _status.Text = string.Format("Done. {0} candidate(s) found; if wrong, use \"Try another trigger\".", cands.Count);
                Touch();
            },
            delegate (string s) { if (!IsDisposed) _status.Text = s; },
            delegate (string s) { if (!IsDisposed) { _status.Text = s; _status.ForeColor = Theme.Bad; } });
        }

        private void CycleAlternative()
        {
            var r = Current;
            if (r == null || r.Alternatives.Count == 0) return;
            int idx = -1;
            for (int i = 0; i < r.Alternatives.Count; i++)
            {
                var t = r.Alternatives[i];
                if (t.Button == r.Trig.Button && t.ByteIndex == r.Trig.ByteIndex && t.Mask == r.Trig.Mask &&
                    t.UsagePage == r.Trig.UsagePage) { idx = i; break; }
            }
            idx = (idx + 1) % r.Alternatives.Count;
            r.Trig = r.Alternatives[idx].Clone();
            _trigger.Text = r.Trig.Describe();
            _status.Text = string.Format("Candidate {0}/{1}", idx + 1, r.Alternatives.Count);
            Touch();
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _tick.Stop();
            SaveNow();
            _core.CancelCapture();
            base.OnFormClosing(e);
        }
    }

    #endregion


    #region Autostart

    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "PadKey";

        /// <summary>
        /// Started by Windows we stay in the tray; started by hand the user wants the window.
        /// The autostart entry carries this flag so the two cases can be told apart.
        /// </summary>
        public const string TrayFlag = "--tray";

        private static string Command { get { return "\"" + Application.ExecutablePath + "\" " + TrayFlag; } }

        /// <summary>Upgrades an autostart entry written before the flag existed.</summary>
        public static void EnsureFlag()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    var v = k.GetValue(Name) as string;
                    if (v != null && v.IndexOf(TrayFlag, StringComparison.OrdinalIgnoreCase) < 0)
                        k.SetValue(Name, Command);
                }
            }
            catch { }
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue(Name) != null;
                }
                catch { return false; }
            }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(Name, Command);
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name);
                }
            }
            catch { }
        }
    }

    #endregion

    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // NOTE: deliberately NOT calling Native.EnableDpiAwareness(). WinForms on .NET
            // Framework does not rescale a hand-coded pixel layout, so on a 150/200% display
            // a DPI-aware window comes out half size. Letting Windows stretch the bitmap
            // keeps the dialog the right physical size.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "run";

            if (mode == "list" || mode == "learn" || mode == "probe" || mode == "hold" || mode == "keytest" || mode == "poke" || mode == "session")
            {
                OpenConsole();
                if (mode == "hold") { HoldTest(); Console.WriteLine("Press Enter to close."); Console.ReadLine(); return 0; }
                if (mode == "keytest") { KeyTest(); Console.WriteLine("Press Enter to close."); Console.ReadLine(); return 0; }
                if (mode == "session")
                {
                    int s2 = 6;
                    if (args.Length > 1) int.TryParse(args[1], out s2);
                    Session(s2);
                    return 0;
                }
                if (mode == "poke")
                {
                    byte cb = 0x25;
                    if (args.Length > 1) cb = (byte)int.Parse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    int secs = 5;
                    if (args.Length > 2) int.TryParse(args[2], out secs);
                    Poke(cb, secs);
                    return 0;
                }
                if (mode == "list") { ListDevices(); Console.WriteLine("Press Enter to close."); Console.ReadLine(); return 0; }
                if (mode == "probe")
                {
                    int secs = 20;
                    if (args.Length > 1) int.TryParse(args[1], out secs);
                    Probe(secs);
                    return 0;
                }

                int vid = -1;
                if (args.Length > 1)
                {
                    try { vid = int.Parse(args[1].Replace("0x", "").Replace("0X", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
                    catch { }
                }
                Application.Run(new PadKeyForm(true, vid));
                return 0;
            }

            bool tray = false;
            foreach (var a in args)
                if (a.Equals(Autostart.TrayFlag, StringComparison.OrdinalIgnoreCase)) tray = true;

            using (var mutex = new System.Threading.Mutex(true, "PadKey.SingleInstance"))
            {
                if (!mutex.WaitOne(0, false))
                {
                    // Already running: ask that instance to surface its window instead of
                    // telling the user off with a message box.
                    Native.PostMessageW(new IntPtr(Native.HWND_BROADCAST),
                        PadKeyForm.WM_SHOW_SETTINGS, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                Application.Run(new PadKeyForm(false, -1, tray));
            }
            return 0;
        }

        private static void OpenConsole()
        {
            // Attaching to the parent is only useful if that console is actually on screen.
            // Launched from a hidden/headless shell it is not, and the user sees nothing -
            // so fall back to a console window of our own.
            if (Native.GetConsoleWindow() == IntPtr.Zero)
            {
                if (!Native.AttachConsole(-1) || !Native.IsWindowVisible(Native.GetConsoleWindow()))
                {
                    Native.FreeConsole();
                    Native.AllocConsole();
                }
            }

            var stdout = new StreamWriter(Console.OpenStandardOutput());
            stdout.AutoFlush = true;
            Console.SetOut(stdout);
            try { Console.SetIn(new StreamReader(Console.OpenStandardInput())); } catch { }

            Native.SetConsoleTitleW("PadKey");
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            IntPtr hw = Native.GetConsoleWindow();
            if (hw != IntPtr.Zero) { Native.ShowWindow(hw, 5 /*SW_SHOW*/); Native.SetForegroundWindow(hw); }
        }

        /// <summary>
        /// Decisive paddle finder. Records which values every report byte takes while the pad
        /// is idle, then again while the user HOLDS a paddle down. A byte that takes a value
        /// during the hold that it never took at idle is the paddle - immune to press-timing
        /// guesswork, and it catches falling bits as well as rising ones.
        /// </summary>
        private static void HoldTest()
        {
            uint count = 0;
            uint sz = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            Native.GetRawInputDeviceList(null, ref count, sz);
            var arr = new Native.RAWINPUTDEVICELIST[count];
            Native.GetRawInputDeviceList(arr, ref count, sz);

            var pads = new HashSet<int>();
            var all = new List<HidDevice>();
            for (int i = 0; i < count; i++)
            {
                if (arr[i].dwType != Native.RIM_TYPEHID) continue;
                var d = PadKeyForm.Describe(arr[i].hDevice);
                if (d == null) continue;
                all.Add(d);
                if (d.UsagePage == 1 && (d.Usage == 4 || d.Usage == 5)) pads.Add((d.Vid << 16) | d.Pid);
            }

            var targets = new List<HidDevice>();
            foreach (var d in all) if (pads.Contains((d.Vid << 16) | d.Pid)) targets.Add(d);
            if (targets.Count == 0) { PadKeyForm.Log("No gamepad found."); return; }

            // byte index -> set of values seen, per device, per phase
            var seen = new Dictionary<string, Dictionary<int, HashSet<byte>>>();
            string phase = "idle";
            var lockObj = new object();
            var readers = new List<DirectReader>();

            foreach (var d in targets)
            {
                var dev = d;
                var rdr = new DirectReader(d.Path, d.InputReportLength > 0 ? d.InputReportLength : 65,
                    delegate (byte[] report)
                    {
                        lock (lockObj)
                        {
                            string key = phase + "|" + dev.ShortId + "|" + dev.UsagePage.ToString("X2") + ":" + dev.Usage.ToString("X2");
                            Dictionary<int, HashSet<byte>> map;
                            if (!seen.TryGetValue(key, out map)) { map = new Dictionary<int, HashSet<byte>>(); seen[key] = map; }
                            for (int i = 0; i < report.Length; i++)
                            {
                                HashSet<byte> vals;
                                if (!map.TryGetValue(i, out vals)) { vals = new HashSet<byte>(); map[i] = vals; }
                                vals.Add(report[i]);
                            }
                        }
                    });
                if (rdr.Start()) { readers.Add(rdr); PadKeyForm.Log("opened: " + d.Ident); }
                else PadKeyForm.Log("could not open: " + d.Ident + " - " + rdr.LastError);
            }

            if (readers.Count == 0) { PadKeyForm.Log("No interface could be opened."); return; }

            PadKeyForm.Log("");
            Countdown("IDLE baseline - DO NOT TOUCH the pad", 5);

            RunPhase(ref phase, "sol", lockObj, "PRESS and HOLD the LEFT back button");
            RunPhase(ref phase, "sag", lockObj, "PRESS and HOLD the RIGHT back button");

            foreach (var r in readers) r.Stop();

            PadKeyForm.Log("");
            PadKeyForm.Log("================ RESULT ================");
            foreach (var side in new string[] { "sol", "sag" })
            {
                PadKeyForm.Log("");
                PadKeyForm.Log("--- " + side.ToUpperInvariant() + " BACK BUTTON ---");
                bool any = false;
                foreach (var kv in seen)
                {
                    if (!kv.Key.StartsWith(side + "|")) continue;
                    string devKey = kv.Key.Substring(side.Length + 1);
                    Dictionary<int, HashSet<byte>> idle;
                    if (!seen.TryGetValue("idle|" + devKey, out idle)) continue;

                    foreach (var b in kv.Value)
                    {
                        HashSet<byte> idleVals;
                        if (!idle.TryGetValue(b.Key, out idleVals)) continue;
                        var uniq = new List<byte>();
                        foreach (var v in b.Value) if (!idleVals.Contains(v)) uniq.Add(v);
                        if (uniq.Count == 0) continue;
                        if (idleVals.Count > 4) continue;   // constantly churning byte, not a button

                        any = true;
                        PadKeyForm.Log(string.Format("  {0}  byte {1,2}: idle [{2}] -> pressed [{3}]",
                            devKey, b.Key, HexList(idleVals), HexList(uniq)));
                    }
                }
                if (!any) PadKeyForm.Log("  (no byte changed consistently)");
            }
            PadKeyForm.Log("");
        }

        private static void RunPhase(ref string phase, string name, object lockObj, string prompt)
        {
            PadKeyForm.Log("");
            Countdown(">>> " + prompt + " <<<", 3);
            lock (lockObj) phase = name;
            Countdown("    HOLD IT...", 5);
            lock (lockObj) phase = "bitti_" + name;
            PadKeyForm.Log("    RELEASE.");
            System.Threading.Thread.Sleep(1200);
        }

        private static void Countdown(string msg, int secs)
        {
            PadKeyForm.Log(msg);
            for (int i = secs; i > 0; i--)
            {
                Console.Write("  " + i + " ");
                System.Threading.Thread.Sleep(1000);
            }
            Console.WriteLine();
        }

        private static string HexList(IEnumerable<byte> vals)
        {
            var l = new List<byte>(vals); l.Sort();
            var sb = new StringBuilder();
            foreach (var v in l) { if (sb.Length > 0) sb.Append(' '); sb.Append(v.ToString("X2")); }
            return sb.ToString();
        }

        /// <summary>
        /// Read-only interrogation of the pad's vendor collections. Tries every feature and
        /// input report id, then polls whatever answered so we can see which bytes move when
        /// a paddle is pressed. Nothing is ever written to the device.
        /// </summary>
        private static void Probe(int seconds)
        {
            uint count = 0;
            uint sz = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            Native.GetRawInputDeviceList(null, ref count, sz);
            var arr = new Native.RAWINPUTDEVICELIST[count];
            Native.GetRawInputDeviceList(arr, ref count, sz);

            var pads = new HashSet<int>();
            var all = new List<HidDevice>();
            for (int i = 0; i < count; i++)
            {
                if (arr[i].dwType != Native.RIM_TYPEHID) continue;
                var d = PadKeyForm.Describe(arr[i].hDevice);
                if (d == null) continue;
                all.Add(d);
                if (d.UsagePage == 1 && (d.Usage == 4 || d.Usage == 5)) pads.Add((d.Vid << 16) | d.Pid);
            }

            var targets = new List<HidDevice>();
            foreach (var d in all)
                if (d.IsVendorPage && pads.Contains((d.Vid << 16) | d.Pid)) targets.Add(d);

            if (targets.Count == 0) { PadKeyForm.Log("No vendor interface found for the gamepad."); return; }

            foreach (var d in targets)
            {
                PadKeyForm.Log("");
                PadKeyForm.Log("=== " + d.Ident + " ===");
                PadKeyForm.Log("  " + d.Path);

                int inLen = d.InputReportLength, featLen = 0, outLen = 0;
                if (d.Preparsed != IntPtr.Zero)
                {
                    Native.HIDP_CAPS caps;
                    if (Native.HidP_GetCaps(d.Preparsed, out caps) == Native.HIDP_STATUS_SUCCESS)
                    {
                        inLen = caps.InputReportByteLength;
                        outLen = caps.OutputReportByteLength;
                        featLen = caps.FeatureReportByteLength;
                    }
                }
                PadKeyForm.Log(string.Format("  report sizes: input={0} output={1} feature={2}", inLen, outLen, featLen));

                IntPtr h = Native.CreateFileW(d.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID_HANDLE)
                    h = Native.CreateFileW(d.Path, Native.GENERIC_READ,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID_HANDLE)
                {
                    PadKeyForm.Log("  ACILAMADI err=" + Marshal.GetLastWin32Error());
                    continue;
                }

                var liveFeature = new List<int>();
                var liveInput = new List<int>();

                if (featLen > 0)
                    for (int id = 0; id < 32; id++)
                    {
                        var buf = new byte[featLen];
                        buf[0] = (byte)id;
                        if (Native.HidD_GetFeature(h, buf, buf.Length))
                        {
                            PadKeyForm.Log(string.Format("  FEATURE id {0,2}: {1}", id, PadKeyForm.Hex(buf)));
                            liveFeature.Add(id);
                        }
                    }
                else PadKeyForm.Log("  (no feature report)");

                if (inLen > 0)
                    for (int id = 0; id < 32; id++)
                    {
                        var buf = new byte[inLen];
                        buf[0] = (byte)id;
                        if (Native.HidD_GetInputReport(h, buf, buf.Length))
                        {
                            PadKeyForm.Log(string.Format("  INPUT   id {0,2}: {1}", id, PadKeyForm.Hex(buf)));
                            liveInput.Add(id);
                        }
                    }

                if (liveFeature.Count == 0 && liveInput.Count == 0)
                {
                    PadKeyForm.Log("  No report id answered.");
                    Native.CloseHandle(h);
                    continue;
                }

                PadKeyForm.Log("");
                PadKeyForm.Log(string.Format("  >>> polling for {0} s. PRESS THE BACK BUTTONS NOW. <<<", seconds));

                var prevF = new Dictionary<int, byte[]>();
                var prevI = new Dictionary<int, byte[]>();
                var end = DateTime.UtcNow.AddSeconds(seconds);
                while (DateTime.UtcNow < end)
                {
                    foreach (var id in liveFeature)
                    {
                        var buf = new byte[featLen];
                        buf[0] = (byte)id;
                        if (!Native.HidD_GetFeature(h, buf, buf.Length)) continue;
                        ReportDiff("FEATURE", id, prevF, buf);
                    }
                    foreach (var id in liveInput)
                    {
                        var buf = new byte[inLen];
                        buf[0] = (byte)id;
                        if (!Native.HidD_GetInputReport(h, buf, buf.Length)) continue;
                        ReportDiff("INPUT", id, prevI, buf);
                    }
                    System.Threading.Thread.Sleep(8);
                }

                Native.CloseHandle(h);
            }
            PadKeyForm.Log("");
            PadKeyForm.Log("probe bitti.");
        }

        private static void ReportDiff(string kind, int id, Dictionary<int, byte[]> prev, byte[] buf)
        {
            byte[] old;
            if (!prev.TryGetValue(id, out old) || old.Length != buf.Length)
            {
                prev[id] = (byte[])buf.Clone();
                return;
            }
            var changed = new List<int>();
            for (int i = 0; i < buf.Length; i++) if (old[i] != buf[i]) changed.Add(i);
            if (changed.Count == 0) return;

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("  ").Append(kind).Append(" id ").Append(id).Append(" :");
            foreach (var i in changed) sb.Append(string.Format(" [{0}] {1:X2}->{2:X2}", i, old[i], buf[i]));
            PadKeyForm.Log(sb.ToString());
            prev[id] = (byte[])buf.Clone();
        }

        private static Native.HookProc _keyTestProc;   // must outlive the hook
        private static readonly List<string> _keyTestSeen = new List<string>();

        /// <summary>
        /// Sends the keys from padkey.ini through SendInput while watching a WH_KEYBOARD_LL
        /// hook - the same mechanism Steam uses to catch its screenshot hotkey. If the hook
        /// sees the key, injection works and any remaining problem is on Steam's side.
        /// </summary>
        private static void KeyTest()
        {
            var rules = new List<Rule>();
            Profiles.Init();
            Config.LoadFrom(Profiles.ActivePath, rules);

            _keyTestProc = delegate (int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0)
                {
                    var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int msg = wParam.ToInt32();
                    string kind = (msg == 0x0100 || msg == 0x0104) ? "DOWN" : "UP  ";
                    _keyTestSeen.Add(string.Format("    hook saw: {0} vk=0x{1:X2} ({2}) scan=0x{3:X2}{4}",
                        kind, k.vkCode, Keys_.Name((ushort)k.vkCode), k.scanCode,
                        (k.flags & Native.LLKHF_INJECTED) != 0 ? " [injected]" : ""));
                }
                return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            };

            IntPtr hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _keyTestProc,
                Native.GetModuleHandleW(null), 0);
            if (hook == IntPtr.Zero)
            {
                PadKeyForm.Log("Could not install the keyboard hook, err=" + Marshal.GetLastWin32Error());
                return;
            }

            PadKeyForm.Log("Keyboard hook installed. Testing the keys from the active profile.");
            PadKeyForm.Log("");

            foreach (var r in rules)
            {
                _keyTestSeen.Clear();
                PadKeyForm.Log(string.Format("  [{0}]  key = {1}", r.Name, r.KeyText));

                PadKeyForm.SendKey(r, true);
                Pump(120);
                PadKeyForm.SendKey(r, false);
                Pump(250);

                if (_keyTestSeen.Count == 0) PadKeyForm.Log("    NOT SEEN AT ALL - SendInput is being blocked.");
                else foreach (var s in _keyTestSeen) PadKeyForm.Log(s);
                PadKeyForm.Log("");
            }

            Native.UnhookWindowsHookEx(hook);

            PadKeyForm.Log("Steam:");
            bool steam = false;
            foreach (var p in System.Diagnostics.Process.GetProcesses())
                if (p.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase)) steam = true;
            PadKeyForm.Log(steam ? "  steam.exe is RUNNING" : "  steam.exe is not running");
            PadKeyForm.Log("");
            PadKeyForm.Log("NOTE: Steam only catches F12 INSIDE A GAME with the overlay enabled.");
            PadKeyForm.Log("     Pressing F12 on the desktop does not take a screenshot.");
        }

        private static void Pump(int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
        }

        /// <summary>
        /// Sends one "status" request (cmd nibble 0x5 - the read-only family: 0x15 base info,
        /// 0x25 key event, 0x55 firmware info) and prints whatever the pad answers. Command
        /// bytes outside that family are rejected so this can never write configuration.
        /// </summary>
        private static void Poke(byte cmdByte, int seconds)
        {
            if ((cmdByte & 0x0F) != 0x5)
            {
                PadKeyForm.Log("Safety: only cmd nibble 0x5 (status query) is allowed. Given: 0x" + cmdByte.ToString("X2"));
                return;
            }

            uint count = 0;
            uint sz = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            Native.GetRawInputDeviceList(null, ref count, sz);
            var arr = new Native.RAWINPUTDEVICELIST[count];
            Native.GetRawInputDeviceList(arr, ref count, sz);

            HidDevice target = null;
            for (int i = 0; i < count; i++)
            {
                if (arr[i].dwType != Native.RIM_TYPEHID) continue;
                var d = PadKeyForm.Describe(arr[i].hDevice);
                if (d == null || !d.IsVendorPage) continue;
                if (d.Vid != 0x20BC && d.Vid != 0x20DD) continue;
                target = d; break;
            }
            if (target == null) { PadKeyForm.Log("Vendor interface not found."); return; }

            PadKeyForm.Log("target: " + target.Ident + "  input=" + target.InputReportLength + " output=" + target.OutputReportLength);

            int got = 0;
            var seen = new List<string>();
            var rdr = new DirectReader(target.Path, target.InputReportLength, delegate (byte[] rep)
            {
                got++;
                if (seen.Count < 6) seen.Add(PadKeyForm.Hex(rep));
            });

            int olen = target.OutputReportLength > 0 ? target.OutputReportLength : 65;
            var req = new byte[olen];
            req[0] = 0x02;
            req[1] = cmdByte;
            rdr.SetPoll(req, 8);

            if (!rdr.Start()) { PadKeyForm.Log("could not open: " + rdr.LastError); return; }

            PadKeyForm.Log(string.Format("sending 0x{0:X2} for {1} s...", cmdByte, seconds));
            System.Threading.Thread.Sleep(seconds * 1000);
            rdr.Stop();

            PadKeyForm.Log(string.Format("result: {0} requests, {1} replies{2}", rdr.PollCount, got,
                string.IsNullOrEmpty(rdr.LastError) ? "" : "  error: " + rdr.LastError));
            foreach (var s in seen) PadKeyForm.Log("  " + s);
        }

        /// <summary>
        /// Reproduces the vendor tool's session: a periodic Ping keepalive
        /// (report 02, header 0x21 = cmd 0x1 sync | subcmd 0x2, channel 0x1) alongside the
        /// key-event request 0x25. Ping is the tool's own keepalive and carries mode id 0,
        /// so it reports state rather than setting it.
        /// </summary>
        private static void Session(int seconds)
        {
            uint count = 0;
            uint sz = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            Native.GetRawInputDeviceList(null, ref count, sz);
            var arr = new Native.RAWINPUTDEVICELIST[count];
            Native.GetRawInputDeviceList(arr, ref count, sz);

            HidDevice target = null;
            for (int i = 0; i < count; i++)
            {
                if (arr[i].dwType != Native.RIM_TYPEHID) continue;
                var d = PadKeyForm.Describe(arr[i].hDevice);
                if (d == null || !d.IsVendorPage) continue;
                if (d.Vid != 0x20BC && d.Vid != 0x20DD) continue;
                target = d; break;
            }
            if (target == null) { PadKeyForm.Log("Vendor interface not found."); return; }

            int olen = target.OutputReportLength > 0 ? target.OutputReportLength : 65;
            var keyReq = new byte[olen]; keyReq[0] = 0x02; keyReq[1] = 0x25;
            var ping = new byte[olen]; ping[0] = 0x02; ping[1] = 0x21; ping[2] = 0x00; ping[3] = 0x10;

            int got = 0;
            var seen = new List<string>();
            var rdr = new DirectReader(target.Path, target.InputReportLength, delegate (byte[] rep)
            {
                got++;
                if (seen.Count < 8) seen.Add(PadKeyForm.Hex(rep));
            });
            rdr.SetPoll(keyReq, 8);
            if (!rdr.Start()) { PadKeyForm.Log("could not open: " + rdr.LastError); return; }

            PadKeyForm.Log("sending Ping + key request for " + seconds + " s...");
            var end = DateTime.UtcNow.AddSeconds(seconds);
            int pings = 0;
            while (DateTime.UtcNow < end)
            {
                if (rdr.SendOnce(ping)) pings++;
                System.Threading.Thread.Sleep(200);
            }
            rdr.Stop();

            PadKeyForm.Log(string.Format("result: {0} pings, {1} key requests, {2} replies", pings, rdr.PollCount, got));
            foreach (var s in seen) PadKeyForm.Log("  " + s);
        }

        private static void ListDevices()
        {
            uint count = 0;
            uint sz = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            Native.GetRawInputDeviceList(null, ref count, sz);
            var arr = new Native.RAWINPUTDEVICELIST[count];
            Native.GetRawInputDeviceList(arr, ref count, sz);

            PadKeyForm.Log("=== HID devices ===");
            for (int i = 0; i < count; i++)
            {
                if (arr[i].dwType != Native.RIM_TYPEHID) continue;
                var d = PadKeyForm.Describe(arr[i].hDevice);
                if (d == null) continue;
                PadKeyForm.Log("");
                PadKeyForm.Log(string.Format("VID_{0:X4} PID_{1:X4}   usagePage=0x{2:X2} usage=0x{3:X2}   inputReport={4} bayt",
                    d.Vid, d.Pid, d.UsagePage, d.Usage, d.InputReportLength));
                PadKeyForm.Log("  path: " + d.Path);
                if (d.Preparsed != IntPtr.Zero)
                {
                    Native.HIDP_CAPS caps;
                    if (Native.HidP_GetCaps(d.Preparsed, out caps) == Native.HIDP_STATUS_SUCCESS && caps.NumberInputButtonCaps > 0)
                    {
                        ushort n = caps.NumberInputButtonCaps;
                        var bc = new Native.HIDP_BUTTON_CAPS[n];
                        if (Native.HidP_GetButtonCaps(Native.HidP_Input, bc, ref n, d.Preparsed) == Native.HIDP_STATUS_SUCCESS)
                            for (int j = 0; j < n; j++)
                                PadKeyForm.Log(bc[j].IsRange != 0
                                    ? string.Format("  buttons: page 0x{0:X2}  usage {1}..{2}  (reportId {3})", bc[j].UsagePage, bc[j].UsageMin, bc[j].UsageMax, bc[j].ReportID)
                                    : string.Format("  button : page 0x{0:X2}  usage {1}  (reportId {2})", bc[j].UsagePage, bc[j].UsageMin, bc[j].ReportID));
                    }
                }
                else PadKeyForm.Log("  (preparsed data yok)");
            }
        }
    }
}
