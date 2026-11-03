using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("Redgear A20 Control Center")]
[assembly: AssemblyDescription("Open-Source Hardware Control Utility for Redgear A20")]
[assembly: AssemblyProduct("Redgear A20 Control Center")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Redgear A20 Open Driver Project Contributors")]
[assembly: AssemblyVersion("0.4.0.0")]
[assembly: AssemblyFileVersion("0.4.0.0")]

namespace RedgearA20ControlCenter
{
    // ═════════════════════════════════════════════════════════════════════════
    //  Win32 HID Low-Level Communication Layer (Safe P/Invoke)
    // ═════════════════════════════════════════════════════════════════════════
    public static class HidTransport
    {
        public const ushort VENDOR_ID = 0x18F8;
        public const ushort PRODUCT_ID = 0x0FC0;
        public const ushort USAGE_PAGE_PRIMARY = 0xFF01; // Col04
        public const ushort USAGE_PAGE_FALLBACK = 0xFF00; // Col03
        public const byte REPORT_ID = 0x07;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public uint flags;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
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
        public struct HIDD_ATTRIBUTES
        {
            public uint Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [DllImport("hid.dll", SetLastError = true)]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(SafeFileHandle hDevice, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle hDevice, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS caps);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(SafeFileHandle hDevice, byte[] reportBuffer, uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(SafeFileHandle hDevice, byte[] reportBuffer, uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FlushQueue(SafeFileHandle hDevice);

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle hFile, byte[] buffer, int toRead, out int read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr overlapped);

        // Read-only, fully shared handle for the vendor input channel (shortcut press reports).
        public static SafeFileHandle OpenForRead(string path)
        {
            return CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        public static string FindTargetDevicePath(out ushort detectedUsagePage)
        {
            string inputPath;
            int inputLen;
            return FindDevicePaths(out detectedUsagePage, out inputPath, out inputLen);
        }

        // inputPath: vendor collection 0xFF00 / Usage 1, which carries the button press reports.
        public static string FindDevicePaths(out ushort detectedUsagePage, out string inputPath, out int inputLen)
        {
            detectedUsagePage = 0;
            inputPath = null;
            inputLen = 0;
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (hDevInfo == IntPtr.Zero || hDevInfo.ToInt64() == -1) return null;

            string primaryPath = null;
            string fallbackPath = null;

            try
            {
                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

                uint index = 0;
                while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, index++, ref ifData))
                {
                    uint requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
                    if (requiredSize == 0) continue;

                    IntPtr pDetail = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        // Set cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA (4 bytes on 32-bit, 8 bytes on 64-bit)
                        Marshal.WriteInt32(pDetail, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);

                        if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, pDetail, requiredSize, out requiredSize, IntPtr.Zero))
                        {
                            IntPtr pPath = new IntPtr(pDetail.ToInt64() + 4);
                            string path = Marshal.PtrToStringAuto(pPath);

                            // A malformed/empty detail record can yield null; skip it.
                            if (string.IsNullOrEmpty(path)) continue;

                            // Quick filter on VID & PID in string
                            string pathLower = path.ToLowerInvariant();
                            if (pathLower.Contains("vid_18f8") && pathLower.Contains("pid_0fc0"))
                            {
                                SafeFileHandle hFile = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (!hFile.IsInvalid)
                                {
                                    using (hFile)
                                    {
                                        IntPtr pData;
                                        if (HidD_GetPreparsedData(hFile, out pData))
                                        {
                                            try
                                            {
                                                HIDP_CAPS caps = new HIDP_CAPS();
                                                HidP_GetCaps(pData, ref caps);

                                                if (caps.UsagePage == USAGE_PAGE_PRIMARY && primaryPath == null)
                                                {
                                                    primaryPath = path;
                                                }
                                                else if (caps.UsagePage == USAGE_PAGE_FALLBACK)
                                                {
                                                    if (fallbackPath == null) fallbackPath = path;
                                                    if (caps.Usage == 1 && caps.InputReportByteLength >= 3 && inputPath == null)
                                                    {
                                                        inputPath = path;
                                                        inputLen = caps.InputReportByteLength;
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                HidD_FreePreparsedData(pData);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pDetail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }

            if (primaryPath != null)
            {
                detectedUsagePage = USAGE_PAGE_PRIMARY;
                return primaryPath;
            }
            if (fallbackPath != null)
            {
                detectedUsagePage = USAGE_PAGE_FALLBACK;
                return fallbackPath;
            }
            return null;
        }

        public static bool SendSinglePacket(string path, byte[] packet, out int win32Err, out string error)
        {
            error = null;
            win32Err = 0;
            if (packet == null || packet.Length != 8)
            {
                error = "Packet must be exactly 8 bytes.";
                return false;
            }

            SafeFileHandle hFile = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (hFile.IsInvalid)
            {
                win32Err = Marshal.GetLastWin32Error();
                error = string.Format("Failed to open device handle (Win32 Error: {0})", win32Err);
                return false;
            }

            try
            {
                bool success = HidD_SetFeature(hFile, packet, 8);
                if (!success)
                {
                    win32Err = Marshal.GetLastWin32Error();
                    error = string.Format("HidD_SetFeature failed (Win32 Error: {0})", win32Err);
                    return false;
                }
                HidD_FlushQueue(hFile);
                return true;
            }
            finally
            {
                hFile.Close();
                hFile.Dispose();
            }
        }

        public static bool SendSinglePacket(string path, byte[] packet, out string error)
        {
            int win32Err;
            return SendSinglePacket(path, packet, out win32Err, out error);
        }

        public static bool VerifyDeviceTransportHealth(string path, out int win32Err, out string error)
        {
            error = null;
            win32Err = 0;
            SafeFileHandle hFile = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (hFile.IsInvalid)
            {
                win32Err = Marshal.GetLastWin32Error();
                error = string.Format("Device handle unavailable after flash write (Win32 Error: {0})", win32Err);
                return false;
            }

            using (hFile)
            {
                byte[] buf = new byte[32];
                buf[0] = REPORT_ID; // Report ID 0x07 must be byte 0
                bool ok = HidD_GetFeature(hFile, buf, (uint)buf.Length);
                if (!ok)
                {
                    win32Err = Marshal.GetLastWin32Error();
                    error = string.Format("Device non-responsive after flash write (Win32 Error: {0})", win32Err);
                    return false;
                }
                HidD_FlushQueue(hFile);
                return true;
            }
        }
    }

    public struct SaveParameters
    {
        public Color Color;
        public int Brightness;
        public byte ActiveDpiIndex;
        public byte DpiMask;
        public bool CommitToFlash;
        public bool SendDpi;
        public bool SendButtonBindings;
        public ButtonBinding[] ButtonBindings;
    }

    // Verified hardware action codes for Command 0x10 (see AI-CONTEXT.md section 2.1).
    // Derived from the vendor UI-index -> firmware-code translation at VA 0x004087A7-0x0040880F.
    // Codes outside this set collapse to 0x00 in firmware (silent no-op), so they are never sent.
    public enum ButtonAction : byte
    {
        // Report-to-host: the button does nothing locally and instead emits vendor input report
        // [06, 0xF0+ButtonID, 01/80]; ShortcutEngine answers it with Cmd 0x15 key injection.
        Shortcut = 0x00,
        LeftButton = 0x01,
        MiddleClick = 0x02,
        RightButton = 0x03,
        Back = 0x04,
        Forward = 0x05,
        DpiLoop = 0x06,
        ShowDesktop = 0x07,
        DoubleClick = 0x08,
        Fire = 0x09,
        Disabled = 0x0A,
        DpiUp = 0x0B,
        DpiDown = 0x0C
    }

    public struct ButtonBinding
    {
        public byte ButtonId;
        public ButtonAction Action;

        public ButtonBinding(byte buttonId, ButtonAction action)
        {
            ButtonId = buttonId;
            Action = action;
        }
    }

    public struct KeyShortcut
    {
        public byte Mods; // bit0 Ctrl, bit1 Shift, bit2 Alt, bit3 Win (HID modifier usage = 0xE0 + bit)
        public byte Vk;   // Windows virtual-key code of the single main key

        public KeyShortcut(byte mods, byte vk)
        {
            Mods = mods;
            Vk = vk;
        }
    }

    // Shortcut rules live here so the picker, the profile loader and the engine share one gate.
    public static class KeyShortcuts
    {
        public const byte MOD_CTRL = 1, MOD_SHIFT = 2, MOD_ALT = 4, MOD_WIN = 8;
        private static readonly string[] ModNames = { "Ctrl", "Shift", "Alt", "Win" };

        // Non-contiguous VK -> HID usage pairs, mirroring the vendor converter at VA 0x00415410.
        // Caps / Num / Scroll Lock are deliberately absent: toggles would leave lasting state.
        private static readonly byte[] VkPairs = {
            0x08, 0x2A, 0x09, 0x2B, 0x0D, 0x28, 0x13, 0x48, 0x1B, 0x29, 0x20, 0x2C, 0x21, 0x4B, 0x22, 0x4E,
            0x23, 0x4D, 0x24, 0x4A, 0x25, 0x50, 0x26, 0x52, 0x27, 0x4F, 0x28, 0x51, 0x2C, 0x46, 0x2D, 0x49,
            0x2E, 0x4C, 0x30, 0x27, 0x5D, 0x65, 0x60, 0x62, 0x6A, 0x55, 0x6B, 0x57, 0x6D, 0x56, 0x6E, 0x63,
            0x6F, 0x54, 0xBA, 0x33, 0xBB, 0x2E, 0xBC, 0x36, 0xBD, 0x2D, 0xBE, 0x37, 0xBF, 0x38, 0xC0, 0x35,
            0xDB, 0x2F, 0xDC, 0x31, 0xDD, 0x30, 0xDE, 0x34 };

        public static byte VkToUsage(byte vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return (byte)(vk - 0x3D); // A-Z
            if (vk >= 0x31 && vk <= 0x39) return (byte)(vk - 0x13); // 1-9
            if (vk >= 0x61 && vk <= 0x69) return (byte)(vk - 0x08); // Num 1-9
            if (vk >= 0x70 && vk <= 0x7B) return (byte)(vk - 0x36); // F1-F12
            if (vk >= 0x7C && vk <= 0x87) return (byte)(vk - 0x14); // F13-F24
            for (int i = 0; i < VkPairs.Length; i += 2)
            {
                if (VkPairs[i] == vk) return VkPairs[i + 1];
            }
            return 0;
        }

        public static string KeyName(byte vk)
        {
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);
            switch (vk)
            {
                case 0x08: return "Backspace";
                case 0x0D: return "Enter";
                case 0x14: return "Caps Lock";
                case 0x90: return "Num Lock";
                case 0x91: return "Scroll Lock";
                case 0x1B: return "Esc";
                case 0x21: return "Page Up";
                case 0x22: return "Page Down";
                case 0x2C: return "Print Screen";
                case 0x5D: return "Menu";
                case 0x6A: return "Num *";
                case 0x6B: return "Num +";
                case 0x6D: return "Num -";
                case 0x6E: return "Num .";
                case 0x6F: return "Num /";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
            }
            return ((Keys)vk).ToString();
        }

        public static string Describe(KeyShortcut ks)
        {
            StringBuilder sb = new StringBuilder();
            for (int bit = 0; bit < 4; bit++)
            {
                if ((ks.Mods & (1 << bit)) != 0) sb.Append(ModNames[bit]).Append(" + ");
            }
            return sb.Append(KeyName(ks.Vk)).ToString();
        }

        // Ctrl+Alt+Del is the Secure Attention Sequence; a real USB keyboard (which is what the
        // mouse becomes) can trigger it, so it is refused outright.
        public static bool IsBlocked(KeyShortcut ks)
        {
            bool ctrlAlt = (ks.Mods & (MOD_CTRL | MOD_ALT)) == (MOD_CTRL | MOD_ALT);
            return ctrlAlt && (ks.Vk == 0x2E || ks.Vk == 0x6E);
        }

        public static bool IsAllowed(KeyShortcut ks)
        {
            return (ks.Mods & 0xF0) == 0 && VkToUsage(ks.Vk) != 0 && !IsBlocked(ks);
        }

        // Allowed, but confirmed with the user before saving.
        public static string Warning(KeyShortcut ks)
        {
            bool ctrl = (ks.Mods & MOD_CTRL) != 0;
            if (((ks.Mods & MOD_ALT) != 0 || ctrl) && ks.Vk == 0x73 || ctrl && ks.Vk == 0x57)
                return "closes the active window, which may be this app or a document with unsaved work.";
            if ((ks.Mods & MOD_WIN) != 0 && ks.Vk == 0x4C)
                return "locks your PC.";
            return null;
        }

        public static string ToToken(KeyShortcut ks)
        {
            return ks.Mods.ToString("X2") + ":" + ks.Vk.ToString("X2");
        }

        public static bool TryParse(string token, out KeyShortcut ks)
        {
            ks = new KeyShortcut();
            Match m = Regex.Match(token ?? "", "^([0-9A-Fa-f]{2}):([0-9A-Fa-f]{2})$");
            if (!m.Success) return false;
            ks = new KeyShortcut(Convert.ToByte(m.Groups[1].Value, 16), Convert.ToByte(m.Groups[2].Value, 16));
            return IsAllowed(ks);
        }

        // [07 15 k0..k5]: modifiers first (as HID usages 0xE0-0xE3), then the main key.
        public static byte[] DownPacket(KeyShortcut ks)
        {
            byte[] pkt = new byte[] { HidTransport.REPORT_ID, 0x15, 0, 0, 0, 0, 0, 0 };
            int slot = 2;
            for (int bit = 0; bit < 4; bit++)
            {
                if ((ks.Mods & (1 << bit)) != 0) pkt[slot++] = (byte)(0xE0 + bit);
            }
            pkt[slot] = VkToUsage(ks.Vk);
            return pkt;
        }

        public static byte[] ReleasePacket()
        {
            return new byte[] { HidTransport.REPORT_ID, 0x15, 0, 0, 0, 0, 0, 0 };
        }
    }

    // Single source of truth for the physical button matrix. Button IDs come from the vendor's
    // table at VA 0x005BB018 (01 02 03 05 04 08 06 09); factory actions from VA 0x005BB038.
    public static class ButtonMap
    {
        // LAYER 2 of the Left/Right interlock: hardware IDs that may never be rebound.
        // NOTE: 0x01 = Left, 0x03 = Right. 0x02 is the MIDDLE button and IS remappable.
        public const byte BUTTON_ID_LEFT = 0x01;
        public const byte BUTTON_ID_RIGHT = 0x03;

        public static bool IsLocked(byte buttonId)
        {
            return buttonId == BUTTON_ID_LEFT || buttonId == BUTTON_ID_RIGHT;
        }

        public static bool IsKnownAction(ButtonAction action)
        {
            return Enum.IsDefined(typeof(ButtonAction), action);
        }

        public class ButtonInfo
        {
            public byte Id;
            public string DisplayName;
            public string ProfileKey;
            public ButtonAction FactoryAction;
            public bool Locked;
        }

        // Ordered exactly as presented on the Buttons page.
        public static readonly ButtonInfo[] All = new ButtonInfo[]
        {
            new ButtonInfo { Id = 0x01, DisplayName = "Left Click",    ProfileKey = "BindLeft",    FactoryAction = ButtonAction.LeftButton,  Locked = true  },
            new ButtonInfo { Id = 0x03, DisplayName = "Right Click",   ProfileKey = "BindRight",   FactoryAction = ButtonAction.RightButton, Locked = true  },
            new ButtonInfo { Id = 0x02, DisplayName = "Middle / Wheel",ProfileKey = "BindMiddle",  FactoryAction = ButtonAction.MiddleClick, Locked = false },
            new ButtonInfo { Id = 0x05, DisplayName = "Forward",       ProfileKey = "BindForward", FactoryAction = ButtonAction.Forward,     Locked = false },
            new ButtonInfo { Id = 0x04, DisplayName = "Back",          ProfileKey = "BindBack",    FactoryAction = ButtonAction.Back,        Locked = false },
            new ButtonInfo { Id = 0x08, DisplayName = "DPI + (Upper)", ProfileKey = "BindDpiUp",   FactoryAction = ButtonAction.DpiUp,       Locked = false },
            new ButtonInfo { Id = 0x06, DisplayName = "DPI − (Lower)", ProfileKey = "BindDpiDown", FactoryAction = ButtonAction.DpiDown,     Locked = false }
        };

        // Actions offered in the UI. Media keys (Cmd 0x17) are intentionally absent: that opcode
        // carries no Button ID, so binding it cannot be verified from static analysis (v0.5.0).
        public static readonly ButtonAction[] Selectable = new ButtonAction[]
        {
            ButtonAction.MiddleClick,
            ButtonAction.Back,
            ButtonAction.Forward,
            ButtonAction.DpiLoop,
            ButtonAction.DpiUp,
            ButtonAction.DpiDown,
            ButtonAction.DoubleClick,
            ButtonAction.Fire,
            ButtonAction.ShowDesktop,
            ButtonAction.Shortcut,
            ButtonAction.Disabled
        };

        public static string Describe(ButtonAction action)
        {
            switch (action)
            {
                case ButtonAction.Shortcut: return "Keyboard Shortcut…";
                case ButtonAction.LeftButton: return "Left Button";
                case ButtonAction.MiddleClick: return "Middle Click";
                case ButtonAction.RightButton: return "Right Button";
                case ButtonAction.Back: return "Browser Back";
                case ButtonAction.Forward: return "Browser Forward";
                case ButtonAction.DpiLoop: return "DPI Cycle (Loop)";
                case ButtonAction.ShowDesktop: return "Show Desktop";
                case ButtonAction.DoubleClick: return "Double Click";
                case ButtonAction.Fire: return "Rapid Fire (Triple Click)";
                case ButtonAction.Disabled: return "Disabled (No Action)";
                case ButtonAction.DpiUp: return "DPI Increase";
                case ButtonAction.DpiDown: return "DPI Decrease";
                default: return "Unknown";
            }
        }

        public static ButtonInfo Find(byte buttonId)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == buttonId) return All[i];
            }
            return null;
        }

        // Stable profile token, decoupled from the raw firmware code so a future code
        // correction cannot silently repoint a saved binding (AI-CONTEXT.md decision 8.11).
        public static string ToToken(ButtonAction action)
        {
            return action.ToString();
        }

        public static bool TryParseToken(string token, out ButtonAction action)
        {
            action = ButtonAction.Disabled;
            if (string.IsNullOrWhiteSpace(token) || !char.IsLetter(token.Trim()[0])) return false;
            try
            {
                object parsed = Enum.Parse(typeof(ButtonAction), token.Trim(), true);
                ButtonAction candidate = (ButtonAction)parsed;
                if (!IsKnownAction(candidate)) return false;
                action = candidate;
                return true;
            }
            catch { return false; }
        }
    }

    // Host-assisted keyboard shortcuts (hardware-verified by probe, Nov 2026).
    // A button bound to code 0x00 makes the mouse emit vendor input report [06, 0xF0+ID, 01=down /
    // 80=up] on collection 0xFF00. On "down" we reply with Cmd 0x15 key-down then all-zero key-up,
    // and the mouse types the combo as its own USB keyboard. No global hooks and no SendInput:
    // we only read our own device's vendor channel.
    public static class ShortcutEngine
    {
        private const int HOLD_MS = 50; // also the mandatory inter-packet pacing

        private static readonly object sync = new object();
        private static volatile Dictionary<byte, KeyShortcut> active = new Dictionary<byte, KeyShortcut>();
        private static volatile string writePath;
        private static volatile Thread reader;
        private static SafeFileHandle readHandle;
        private static int busy;
        private static volatile bool keysMayBeHeld;
        public static Action<string, Color> Log;

        private static void Report(string msg, Color col)
        {
            Action<string, Color> l = Log;
            if (l != null) l(msg, col);
        }

        // map = buttons the mouse RAM currently has bound to code 0x00. Empty map stops listening.
        public static void Configure(Dictionary<byte, KeyShortcut> map, string devicePath)
        {
            string msg = StartOrStop(map, devicePath);
            // Logged outside the lock: Log marshals to the UI thread, which may itself be waiting on it.
            if (msg != null) Report(msg, msg.StartsWith("Shortcut listener active") ? Color.FromArgb(140, 200, 255) : Color.FromArgb(255, 120, 120));
        }

        private static string StartOrStop(Dictionary<byte, KeyShortcut> map, string devicePath)
        {
            lock (sync)
            {
                active = map != null ? new Dictionary<byte, KeyShortcut>(map) : new Dictionary<byte, KeyShortcut>();
                writePath = devicePath;
                if (active.Count == 0 || string.IsNullOrEmpty(devicePath))
                {
                    StopReader();
                    return null;
                }
                if (reader != null && reader.IsAlive) return null;

                ushort page;
                string inputPath;
                int inputLen;
                HidTransport.FindDevicePaths(out page, out inputPath, out inputLen);
                if (inputPath == null) return "Shortcut listener: vendor input channel (UsagePage 0xFF00) not found.";
                SafeFileHandle h = HidTransport.OpenForRead(inputPath);
                if (h.IsInvalid) return "Shortcut listener: could not open input channel (Win32 " + Marshal.GetLastWin32Error() + ").";
                readHandle = h;
                reader = new Thread(delegate() { ReadLoop(h, inputLen); });
                reader.IsBackground = true;
                reader.Name = "A20 shortcut listener";
                reader.Start();
                return string.Format("Shortcut listener active for {0} button(s).", active.Count);
            }
        }

        private static void StopReader()
        {
            Thread t = reader;
            SafeFileHandle h = readHandle;
            reader = null;
            readHandle = null;
            if (t == null || t == Thread.CurrentThread) return;
            // CancelIoEx only hits a read that is already pending, so retry until the thread exits.
            for (int i = 0; i < 5 && t.IsAlive; i++)
            {
                try { HidTransport.CancelIoEx(h, IntPtr.Zero); } catch { }
                t.Join(100);
            }
        }

        // Rebinds the listener to a (re)enumerated device without changing the shortcut map.
        public static void Rebind(string devicePath)
        {
            Configure(active, devicePath);
        }

        public static byte[] ActiveButtonIds()
        {
            return new List<byte>(active.Keys).ToArray();
        }

        // Ends on cancel or device removal; the next hotplug refresh calls Configure again.
        private static void ReadLoop(SafeFileHandle h, int len)
        {
            byte[] buf = new byte[len];
            try
            {
                int n;
                while (HidTransport.ReadFile(h, buf, len, out n, IntPtr.Zero))
                {
                    if (reader != Thread.CurrentThread) break; // superseded by a newer listener
                    if (n >= 3 && buf[0] == 0x06 && buf[1] > 0xF0 && buf[2] == 0x01) OnPress((byte)(buf[1] - 0xF0));
                }
            }
            catch { }
            finally
            {
                h.Dispose();
            }
        }

        private static void OnPress(byte buttonId)
        {
            KeyShortcut ks;
            if (ButtonMap.IsLocked(buttonId) || !active.TryGetValue(buttonId, out ks) || !KeyShortcuts.IsAllowed(ks)) return;
            // One shortcut in flight at a time; presses arriving meanwhile are dropped, not queued.
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Fire(ks); }
                catch { }
                finally { Interlocked.Exchange(ref busy, 0); }
            });
        }

        private static void Fire(KeyShortcut ks)
        {
            string path = writePath;
            if (string.IsNullOrEmpty(path)) return;
            using (Mutex m = new Mutex(false, TaskSchedulerHelper.HARDWARE_MUTEX_NAME))
            {
                bool got = false;
                try { got = m.WaitOne(250, false); }
                catch (AbandonedMutexException) { got = true; }
                if (!got) return; // a settings write is in progress; drop rather than interleave
                try
                {
                    int w;
                    string err;
                    keysMayBeHeld = true;
                    bool ok = HidTransport.SendSinglePacket(path, KeyShortcuts.DownPacket(ks), out w, out err);
                    Thread.Sleep(HOLD_MS);
                    // Release is attempted unconditionally so a key can never stay held down.
                    if (HidTransport.SendSinglePacket(path, KeyShortcuts.ReleasePacket(), out w, out err)) keysMayBeHeld = false;
                    Thread.Sleep(HOLD_MS);
                    if (!ok) Report("Shortcut " + KeyShortcuts.Describe(ks) + " failed: " + err, Color.FromArgb(255, 80, 80));
                }
                finally
                {
                    m.ReleaseMutex();
                }
            }
        }

        // Graceful exit: stop listening, release keys, and hand shortcut buttons back to their
        // factory action so they do not stay dead while no listener is running.
        public static void Shutdown()
        {
            Dictionary<byte, KeyShortcut> map;
            string path;
            lock (sync)
            {
                Log = null;
                map = active;
                path = writePath;
                active = new Dictionary<byte, KeyShortcut>();
                StopReader();
            }
            if (string.IsNullOrEmpty(path) || (map.Count == 0 && !keysMayBeHeld)) return;

            using (Mutex m = new Mutex(false, TaskSchedulerHelper.HARDWARE_MUTEX_NAME))
            {
                bool got = false;
                try { got = m.WaitOne(1000, false); }
                catch (AbandonedMutexException) { got = true; }
                if (!got) return;
                try
                {
                    string err;
                    HidTransport.SendSinglePacket(path, KeyShortcuts.ReleasePacket(), out err);
                    Thread.Sleep(HOLD_MS);
                    foreach (byte id in map.Keys)
                    {
                        ButtonMap.ButtonInfo info = ButtonMap.Find(id);
                        if (info == null || info.Locked) continue;
                        HidTransport.SendSinglePacket(path, new byte[] { HidTransport.REPORT_ID, 0x10, id, (byte)info.FactoryAction, 0, 0, 0, 0 }, out err);
                        Thread.Sleep(HOLD_MS);
                    }
                }
                finally
                {
                    m.ReleaseMutex();
                }
            }
        }
    }

    public class PipelineResult
    {
        public bool Success;
        public string ErrorMessage;
        public int FailingPacketIndex;
        public int Win32ErrorCode;
    }

    public enum PersistenceMode
    {
        StealthWatcher = 0, // Invisible background watcher (No window, No tray icon, ~1.8 MB RAM, 0% CPU, instant hotplug on all ports - Recommended)
        TrayAgent = 1,      // System tray process (~2.7 MB RAM, quick access tray icon)
        Disabled = 2        // RAM only (resets on disconnect or reboot)
    }

    public class AppProfile
    {
        public string HexColor = "#BE59FF";
        public int Brightness = 100;
        public int ActiveDpiStage = 1; // Default 1000 DPI (Stage 1)
        public bool EnableDpi = false;
        public PersistenceMode Mode = PersistenceMode.StealthWatcher;

        // Library identity. profile.json carries the id/name of the profile it was applied from.
        public string Id = null;
        public string Name = "Profile 1";
        public const int MAX_NAME_LENGTH = 32;

        // Global UI preference stored in profile.json only: 0 = System, 1 = Dark, 2 = Light.
        public int ThemePref = 0;

        // Button remapping (Cmd 0x10). Absent/unparsable entries fall back to the factory
        // action, so an older or hand-edited profile can never put an unknown byte on the wire.
        public Dictionary<byte, ButtonAction> ButtonBindings = new Dictionary<byte, ButtonAction>();
        public Dictionary<byte, KeyShortcut> Shortcuts = new Dictionary<byte, KeyShortcut>();

        public ButtonAction GetBinding(byte buttonId)
        {
            ButtonMap.ButtonInfo info = ButtonMap.Find(buttonId);
            ButtonAction fallback = info != null ? info.FactoryAction : ButtonAction.Disabled;
            if (ButtonBindings == null) return fallback;
            ButtonAction stored;
            if (!ButtonBindings.TryGetValue(buttonId, out stored)) return fallback;
            // A Shortcut binding without a valid key combo would leave a dead button.
            KeyShortcut ks;
            if (stored == ButtonAction.Shortcut && !TryGetShortcut(buttonId, out ks)) return fallback;
            return stored;
        }

        public bool TryGetShortcut(byte buttonId, out KeyShortcut ks)
        {
            ks = new KeyShortcut();
            return Shortcuts != null && Shortcuts.TryGetValue(buttonId, out ks) && KeyShortcuts.IsAllowed(ks);
        }

        public void SetShortcut(byte buttonId, KeyShortcut ks)
        {
            if (ButtonMap.IsLocked(buttonId) || ButtonMap.Find(buttonId) == null || !KeyShortcuts.IsAllowed(ks)) return;
            if (Shortcuts == null) Shortcuts = new Dictionary<byte, KeyShortcut>();
            Shortcuts[buttonId] = ks;
        }

        // Buttons whose effective binding is Shortcut, with their key combos (engine input).
        public Dictionary<byte, KeyShortcut> GetActiveShortcuts()
        {
            Dictionary<byte, KeyShortcut> map = new Dictionary<byte, KeyShortcut>();
            ButtonMap.ButtonInfo[] all = ButtonMap.All;
            for (int i = 0; i < all.Length; i++)
            {
                KeyShortcut ks;
                if (!all[i].Locked && GetBinding(all[i].Id) == ButtonAction.Shortcut && TryGetShortcut(all[i].Id, out ks)) map[all[i].Id] = ks;
            }
            return map;
        }

        public void SetBinding(byte buttonId, ButtonAction action)
        {
            // LAYER 2 of the interlock: the model itself refuses to store a primary-click rebind.
            if (ButtonMap.IsLocked(buttonId)) return;
            if (!ButtonMap.IsKnownAction(action)) return;
            if (ButtonBindings == null) ButtonBindings = new Dictionary<byte, ButtonAction>();
            ButtonBindings[buttonId] = action;
        }

        public bool HasCustomBindings()
        {
            ButtonMap.ButtonInfo[] all = ButtonMap.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Locked) continue;
                if (GetBinding(all[i].Id) != all[i].FactoryAction) return true;
            }
            return false;
        }

        // A freshly powered mouse starts at factory, so only non-factory bindings are transmitted
        // there, keeping the hotplug replay budget minimal. full = true also resends factory
        // bindings, for a mouse that may still carry another profile's remaps.
        // A process that exits right after applying (headless) must skip Shortcut bindings:
        // with nobody listening for the press report, the button would simply go dead.
        public ButtonBinding[] GetBindingsToTransmit(bool includeShortcuts, bool full = false)
        {
            List<ButtonBinding> list = new List<ButtonBinding>();
            ButtonMap.ButtonInfo[] all = ButtonMap.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Locked) continue;
                ButtonAction action = GetBinding(all[i].Id);
                if (action == all[i].FactoryAction && !full) continue;
                if (action == ButtonAction.Shortcut && !includeShortcuts) continue;
                list.Add(new ButtonBinding(all[i].Id, action));
            }
            return list.ToArray();
        }

        public bool AutoStart
        {
            get { return Mode == PersistenceMode.StealthWatcher || Mode == PersistenceMode.TrayAgent; }
            set { Mode = value ? PersistenceMode.StealthWatcher : PersistenceMode.Disabled; }
        }

        public static string GetProfilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RedgearA20");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "profile.json");
        }

        public static AppProfile Load()
        {
            return LoadFrom(GetProfilePath());
        }

        public static AppProfile LoadFrom(string file)
        {
            AppProfile p = new AppProfile();
            try
            {
                if (File.Exists(file))
                {
                    Parse(File.ReadAllText(file), p);
                }
            }
            catch { }
            return p;
        }

        public static string SanitizeName(string name)
        {
            if (name == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                if (c == '"' || c == '\\' || char.IsControl(c)) continue;
                sb.Append(c);
            }
            string s = Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
            if (s.Length > MAX_NAME_LENGTH) s = s.Substring(0, MAX_NAME_LENGTH).Trim();
            return s;
        }

        private static void Parse(string json, AppProfile p)
        {
            {
                {
                    Match mName = Regex.Match(json, "\"ProfileName\"\\s*:\\s*\"([^\"]*)\"");
                    if (mName.Success)
                    {
                        string n = SanitizeName(mName.Groups[1].Value);
                        if (n.Length > 0) p.Name = n;
                    }

                    Match mId = Regex.Match(json, "\"ProfileId\"\\s*:\\s*\"([0-9A-Fa-f]{8,32})\"");
                    if (mId.Success) p.Id = mId.Groups[1].Value.ToLowerInvariant();

                    Match mTheme = Regex.Match(json, "\"Theme\"\\s*:\\s*(\\d+)");
                    if (mTheme.Success) p.ThemePref = ParseClampedInt(mTheme.Groups[1].Value, 0, 2, 0);

                    Match mHex = Regex.Match(json, "\"HexColor\"\\s*:\\s*\"([^\"]+)\"");
                    if (mHex.Success)
                    {
                        string hex = mHex.Groups[1].Value.Trim();
                        if (Regex.IsMatch(hex, "^#?[0-9A-Fa-f]{6}$"))
                        {
                            p.HexColor = "#" + hex.TrimStart('#').ToUpperInvariant();
                        }
                    }

                    Match mBri = Regex.Match(json, "\"Brightness\"\\s*:\\s*(\\d+)");
                    if (mBri.Success) p.Brightness = ParseClampedInt(mBri.Groups[1].Value, 5, 100, p.Brightness);

                    Match mDpiStage = Regex.Match(json, "\"ActiveDpiStage\"\\s*:\\s*(\\d+)");
                    if (mDpiStage.Success) p.ActiveDpiStage = ParseClampedInt(mDpiStage.Groups[1].Value, 0, 5, p.ActiveDpiStage);

                    Match mEnDpi = Regex.Match(json, "\"EnableDpi\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
                    if (mEnDpi.Success) p.EnableDpi = bool.Parse(mEnDpi.Groups[1].Value);

                    // Flat per-button fields keep the regex parser dependency-free. Unknown action
                    // tokens are skipped (factory fallback) rather than trusted.
                    ButtonMap.ButtonInfo[] buttons = ButtonMap.All;
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (buttons[i].Locked) continue;
                        Match mShort = Regex.Match(json, "\"" + ShortcutKey(buttons[i]) + "\"\\s*:\\s*\"([^\"]*)\"");
                        KeyShortcut ks;
                        if (mShort.Success && KeyShortcuts.TryParse(mShort.Groups[1].Value, out ks)) p.SetShortcut(buttons[i].Id, ks);

                        Match mBind = Regex.Match(json, "\"" + buttons[i].ProfileKey + "\"\\s*:\\s*\"([^\"]*)\"");
                        if (!mBind.Success) continue;
                        ButtonAction action;
                        if (ButtonMap.TryParseToken(mBind.Groups[1].Value, out action))
                        {
                            p.SetBinding(buttons[i].Id, action);
                        }
                    }

                    Match mMode = Regex.Match(json, "\"(?:PersistenceMode|Mode)\"\\s*:\\s*(\\d+)");
                    if (mMode.Success)
                    {
                        int modeVal;
                        if (int.TryParse(mMode.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out modeVal) &&
                            Enum.IsDefined(typeof(PersistenceMode), modeVal))
                        {
                            p.Mode = (PersistenceMode)modeVal;
                        }
                    }
                    else
                    {
                        Match mModeStr = Regex.Match(json, "\"(?:PersistenceMode|Mode)\"\\s*:\\s*\"([^\"]+)\"");
                        if (mModeStr.Success)
                        {
                            string val = mModeStr.Groups[1].Value;
                            if (string.Equals(val, "TrayAgent", StringComparison.OrdinalIgnoreCase) || string.Equals(val, "Tray", StringComparison.OrdinalIgnoreCase))
                                p.Mode = PersistenceMode.TrayAgent;
                            else if (string.Equals(val, "Disabled", StringComparison.OrdinalIgnoreCase))
                                p.Mode = PersistenceMode.Disabled;
                            else
                                p.Mode = PersistenceMode.StealthWatcher;
                        }
                        else
                        {
                            // Backwards compatibility with v0.1.0/v0.1.1 legacy profile format: upgrade auto-start to StealthWatcher
                            Match mAuto = Regex.Match(json, "\"AutoStart\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
                            if (mAuto.Success)
                            {
                                bool auto = bool.Parse(mAuto.Groups[1].Value);
                                p.Mode = auto ? PersistenceMode.StealthWatcher : PersistenceMode.Disabled;
                            }
                        }
                    }
                }
            }
        }

        public AppProfile Clone()
        {
            AppProfile c = new AppProfile();
            c.Id = Id;
            c.Name = Name;
            c.Mode = Mode;
            c.ThemePref = ThemePref;
            c.CopySettingsFrom(this);
            return c;
        }

        // Device settings only (lighting, DPI, buttons). Identity, mode and theme are untouched.
        public void CopySettingsFrom(AppProfile o)
        {
            HexColor = o.HexColor;
            Brightness = o.Brightness;
            ActiveDpiStage = o.ActiveDpiStage;
            EnableDpi = o.EnableDpi;
            ButtonBindings = o.ButtonBindings != null ? new Dictionary<byte, ButtonAction>(o.ButtonBindings) : new Dictionary<byte, ButtonAction>();
            Shortcuts = o.Shortcuts != null ? new Dictionary<byte, KeyShortcut>(o.Shortcuts) : new Dictionary<byte, KeyShortcut>();
        }

        public bool SameSettings(AppProfile o)
        {
            if (o == null) return false;
            if (!string.Equals(HexColor, o.HexColor, StringComparison.OrdinalIgnoreCase)) return false;
            if (Brightness != o.Brightness || ActiveDpiStage != o.ActiveDpiStage || EnableDpi != o.EnableDpi) return false;
            ButtonMap.ButtonInfo[] all = ButtonMap.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Locked) continue;
                ButtonAction a = GetBinding(all[i].Id);
                if (a != o.GetBinding(all[i].Id)) return false;
                if (a == ButtonAction.Shortcut)
                {
                    KeyShortcut k1, k2;
                    TryGetShortcut(all[i].Id, out k1);
                    o.TryGetShortcut(all[i].Id, out k2);
                    if (k1.Mods != k2.Mods || k1.Vk != k2.Vk) return false;
                }
            }
            return true;
        }

        // "BindForward" -> "ShortForward" ("mods:vk" hex token, e.g. "04:73" = Alt + F4).
        private static string ShortcutKey(ButtonMap.ButtonInfo info)
        {
            return "Short" + info.ProfileKey.Substring(4);
        }

        // Tolerates corrupted/oversized numeric fields without aborting the whole profile load.
        private static int ParseClampedInt(string text, int min, int max, int fallback)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public bool Save(out string error)
        {
            return SaveTo(GetProfilePath(), out error);
        }

        public bool SaveTo(string file, out string error)
        {
            error = null;
            string tmpFile = null;
            try
            {
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                int bri = Math.Max(5, Math.Min(100, Brightness));
                int dpi = Math.Max(0, Math.Min(5, ActiveDpiStage));

                StringBuilder sbBinds = new StringBuilder();
                ButtonMap.ButtonInfo[] buttons = ButtonMap.All;
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i].Locked) continue;
                    sbBinds.AppendFormat(",\n  \"{0}\": \"{1}\"", buttons[i].ProfileKey, ButtonMap.ToToken(GetBinding(buttons[i].Id)));
                    KeyShortcut ks;
                    if (TryGetShortcut(buttons[i].Id, out ks)) sbBinds.AppendFormat(",\n  \"{0}\": \"{1}\"", ShortcutKey(buttons[i]), KeyShortcuts.ToToken(ks));
                }

                string name = SanitizeName(Name);
                if (name.Length == 0) name = "Profile";
                string idPart = (!string.IsNullOrEmpty(Id) && Regex.IsMatch(Id, "^[0-9a-fA-F]{8,32}$"))
                    ? string.Format("  \"ProfileId\": \"{0}\",\n", Id) : "";

                string json = string.Format("{{\n  \"ProfileName\": \"{6}\",\n{7}  \"HexColor\": \"{0}\",\n  \"Brightness\": {1},\n  \"ActiveDpiStage\": {2},\n  \"EnableDpi\": {3},\n  \"PersistenceMode\": {4},\n  \"Theme\": {8}{5}\n}}\n",
                    HexColor, bri, dpi, EnableDpi.ToString().ToLower(), (int)Mode, sbBinds.ToString(), name, idPart, Math.Max(0, Math.Min(2, ThemePref)));

                tmpFile = file + ".tmp";
                File.WriteAllText(tmpFile, json, Encoding.UTF8);

                if (File.Exists(file))
                {
                    try
                    {
                        File.Replace(tmpFile, file, null);
                    }
                    catch
                    {
                        // Non-NTFS fallback: overwrite in place, then remove the temp file.
                        // (Avoids a window where the only copy of the profile has been deleted.)
                        File.Copy(tmpFile, file, true);
                        File.Delete(tmpFile);
                    }
                }
                else
                {
                    File.Move(tmpFile, file);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (!string.IsNullOrEmpty(tmpFile))
                {
                    try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
                }
                return false;
            }
        }

        public void Save()
        {
            string dummy;
            Save(out dummy);
        }
    }

    // Named profiles live as individual files: %LocalAppData%\RedgearA20\profiles\<id>.json.
    // profile.json stays the single source the watcher/headless paths read on hotplug.
    public class ProfileLibrary
    {
        public readonly List<AppProfile> Items = new List<AppProfile>();

        public static string GetDir()
        {
            string dir = Path.Combine(Path.GetDirectoryName(AppProfile.GetProfilePath()), "profiles");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private static string PathFor(string id)
        {
            return Path.Combine(GetDir(), id + ".json");
        }

        private static long lastId = 0;
        public static string NewId()
        {
            long t = DateTime.UtcNow.Ticks;
            if (t <= lastId) t = lastId + 1;
            lastId = t;
            return t.ToString("x16");
        }

        public static ProfileLibrary Load(AppProfile active)
        {
            ProfileLibrary lib = new ProfileLibrary();
            try
            {
                foreach (string f in Directory.GetFiles(GetDir(), "*.json"))
                {
                    string id = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (!Regex.IsMatch(id, "^[0-9a-f]{8,32}$")) continue;
                    AppProfile p = AppProfile.LoadFrom(f);
                    p.Id = id;
                    lib.Items.Add(p);
                }
            }
            catch { }
            lib.Items.Sort(delegate (AppProfile a, AppProfile b) { return string.CompareOrdinal(a.Id, b.Id); });

            // First run (or the active profile's file is gone): seed from profile.json.
            if (active != null && (active.Id == null || lib.Find(active.Id) == null))
            {
                AppProfile seed = active.Clone();
                if (seed.Id == null || lib.Find(seed.Id) != null) seed.Id = NewId();
                if (lib.Items.Count > 0 && active.Id == null) seed.Name = lib.UniqueName("Profile");
                string err;
                lib.Items.Add(seed);
                lib.Write(seed, out err);
                active.Id = seed.Id;
                active.Name = seed.Name;
                active.Save(out err);
                lib.Items.Sort(delegate (AppProfile a, AppProfile b) { return string.CompareOrdinal(a.Id, b.Id); });
            }
            return lib;
        }

        public AppProfile Find(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) return Items[i];
            return null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) return i;
            return -1;
        }

        public string UniqueName(string baseName)
        {
            for (int n = 1; n < 1000; n++)
            {
                string candidate = AppProfile.SanitizeName(baseName + " " + n);
                bool taken = false;
                for (int i = 0; i < Items.Count; i++)
                    if (string.Equals(Items[i].Name, candidate, StringComparison.OrdinalIgnoreCase)) { taken = true; break; }
                if (!taken) return candidate;
            }
            return baseName;
        }

        public bool Write(AppProfile p, out string error)
        {
            if (p.Id == null) p.Id = NewId();
            return p.SaveTo(PathFor(p.Id), out error);
        }

        public void Add(AppProfile p, out string error)
        {
            Items.Add(p);
            Write(p, out error);
        }

        public void Delete(string id)
        {
            int i = IndexOf(id);
            if (i >= 0) Items.RemoveAt(i);
            try { string f = PathFor(id); if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    public static class TaskSchedulerHelper
    {
        public const string TASK_NAME = "RedgearA20_AutoApply";
        public const string HARDWARE_MUTEX_NAME = @"Local\RedgearA20_HardwareLock";
        public const string SHOW_EVENT_NAME = @"Local\RedgearA20_ShowControlCenter_Event";

        public static string GetInstalledExePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RedgearA20");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "RedgearA20.exe");
        }

        public static bool DeployDriverBinary(out string targetPath, out string error)
        {
            error = null;
            targetPath = GetInstalledExePath();
            try
            {
                string currentExe = null;
                try
                {
                    currentExe = typeof(TaskSchedulerHelper).Assembly.Location;
                }
                catch { }
                if (string.IsNullOrEmpty(currentExe))
                {
                    currentExe = Application.ExecutablePath;
                }

                if (string.Equals(currentExe, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string dir = Path.GetDirectoryName(targetPath);
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*.old"))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
                catch { }

                string oldPath = targetPath + ".old";
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
                if (File.Exists(oldPath))
                {
                    oldPath = targetPath + "." + DateTime.UtcNow.Ticks + ".old";
                }

                if (File.Exists(targetPath))
                {
                    try
                    {
                        FileInfo fiSrc = new FileInfo(currentExe);
                        FileInfo fiDst = new FileInfo(targetPath);
                        if (fiSrc.Length == fiDst.Length && fiSrc.LastWriteTimeUtc == fiDst.LastWriteTimeUtc)
                        {
                            return true;
                        }
                    }
                    catch { }
                }

                try
                {
                    File.Copy(currentExe, targetPath, true);
                    return true;
                }
                catch (IOException)
                {
                    try
                    {
                        File.Move(targetPath, oldPath);
                        try
                        {
                            File.Copy(currentExe, targetPath, true);
                            return true;
                        }
                        catch
                        {
                            try { File.Move(oldPath, targetPath); } catch { }
                            throw;
                        }
                    }
                    catch (Exception exMove)
                    {
                        if (File.Exists(targetPath)) return true;
                        throw exMove;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        [Obsolete("Task Scheduler mode is deprecated in favor of lightweight Stealth Watcher persistence (--stealth)")]
        public static bool InstallTask(out string error)
        {
            error = "Task Scheduler XML installer is deprecated. Use --stealth to run the lightweight background watcher.";
            return false;
        }

        public static bool RemoveTask(out string error)
        {
            error = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
                    Arguments = string.Format("/delete /tn \"{0}\" /f", TASK_NAME),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        error = "Failed to start schtasks.exe.";
                        return false;
                    }
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(4000);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string PageUrl;
        public string DownloadUrl;  // null when the release has no exe asset (manual download only)
        public string Sha256;       // GitHub-computed asset digest, if published
        public string ChecksumUrl;  // fallback: the .sha256 asset produced by build.bat
    }

    public class UpdateResult
    {
        public string RelaunchPath;
        public string Warning;
    }

    // Stable-channel updater backed by GitHub Releases. /releases/latest never returns drafts or
    // pre-releases, and tags with a suffix (e.g. v0.5.0-beta) are ignored, so only stable builds
    // are ever offered.
    public static class UpdateManager
    {
        public const string REPO = "shivamvcx/A20-Driver";
        public const string ASSET_EXE = "RedgearA20-win-x64.exe";
        public const string ASSET_SHA = "RedgearA20-win-x64.sha256";
        public const string RELEASES_PAGE = "https://github.com/" + REPO + "/releases/latest";
        private const string API_LATEST = "https://api.github.com/repos/" + REPO + "/releases/latest";
        private const string DOWNLOAD_PREFIX = "https://github.com/" + REPO + "/releases/download/";
        private const long MAX_DOWNLOAD_BYTES = 20L * 1024 * 1024;

        public static Version CurrentVersion
        {
            get { return Normalize(typeof(UpdateManager).Assembly.GetName().Version); }
        }

        public static string Display(Version v)
        {
            return string.Format(CultureInfo.InvariantCulture, "v{0}.{1}.{2}", v.Major, v.Minor, v.Build);
        }

        private static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        private static void EnableTls12()
        {
            // .NET 4.x defaults can exclude TLS 1.2, which GitHub requires.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        private static HttpWebRequest CreateRequest(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "RedgearA20-Updater/" + Display(CurrentVersion);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = true;
            return req;
        }

        private static string GetString(string url, string accept)
        {
            HttpWebRequest req = CreateRequest(url, 15000);
            if (accept != null) req.Accept = accept;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        // Returns null when the running build is already the newest stable release.
        public static UpdateInfo CheckLatest()
        {
            EnableTls12();
            string json = GetString(API_LATEST, "application/vnd.github+json");

            if (Regex.IsMatch(json, "\"(?:draft|prerelease)\"\\s*:\\s*true")) return null;
            Match mTag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(\\d+)\\.(\\d+)\\.(\\d+)\"");
            if (!mTag.Success) return null;

            Version latest = new Version(
                int.Parse(mTag.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(mTag.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(mTag.Groups[3].Value, CultureInfo.InvariantCulture), 0);
            if (latest <= CurrentVersion) return null;

            string tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            UpdateInfo info = new UpdateInfo { Version = latest, Tag = tag, PageUrl = RELEASES_PAGE };

            Match mPage = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/" + Regex.Escape(REPO) + "/releases/tag/[^\"]+)\"");
            if (mPage.Success) info.PageUrl = mPage.Groups[1].Value;

            string sha;
            info.DownloadUrl = FindAssetUrl(json, ASSET_EXE, tag, out sha);
            info.Sha256 = sha;
            string ignored;
            info.ChecksumUrl = FindAssetUrl(json, ASSET_SHA, tag, out ignored);
            return info;
        }

        // Asset objects end with browser_download_url, so the digest between the asset's name and
        // its URL belongs to that asset. URLs outside this repo's release downloads are rejected.
        private static string FindAssetUrl(string json, string assetName, string tag, out string sha256)
        {
            sha256 = null;
            Match mName = Regex.Match(json, "\"name\"\\s*:\\s*\"" + Regex.Escape(assetName) + "\"");
            if (!mName.Success) return null;
            Match mUrl = new Regex("\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"").Match(json, mName.Index);
            if (!mUrl.Success) return null;

            string url = mUrl.Groups[1].Value;
            if (!url.StartsWith(DOWNLOAD_PREFIX + tag + "/", StringComparison.Ordinal)) return null;

            string segment = json.Substring(mName.Index, mUrl.Index - mName.Index);
            Match mDigest = Regex.Match(segment, "\"digest\"\\s*:\\s*\"sha256:([0-9A-Fa-f]{64})\"");
            if (mDigest.Success) sha256 = mDigest.Groups[1].Value;
            return url;
        }

        // Downloads, verifies and swaps in the new build. Nothing on disk is touched until the
        // download has passed both the checksum and the embedded version check.
        public static UpdateResult Install(UpdateInfo info, Action<string> progress)
        {
            if (info == null || info.DownloadUrl == null) throw new InvalidOperationException("This release has no downloadable executable.");
            EnableTls12();

            string expected = info.Sha256;
            if (expected == null && info.ChecksumUrl != null)
            {
                Match m = Regex.Match(GetString(info.ChecksumUrl, null), "\\b[0-9A-Fa-f]{64}\\b");
                if (m.Success) expected = m.Value;
            }
            if (expected == null) throw new InvalidOperationException("The release has no published SHA-256 checksum, so the download cannot be verified.");

            string tmp = Path.Combine(Path.GetTempPath(), "RedgearA20-" + info.Tag + "-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                progress("Downloading " + Display(info.Version) + "...");
                Download(info.DownloadUrl, tmp);

                progress("Verifying download...");
                string actual = Sha256Of(tmp);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Checksum mismatch: the download is corrupted or was tampered with.");

                Version fileVersion = Normalize(AssemblyName.GetAssemblyName(tmp).Version);
                if (fileVersion != info.Version)
                    throw new InvalidOperationException(string.Format("The downloaded build reports version {0}, expected {1}.", Display(fileVersion), Display(info.Version)));

                progress("Installing " + Display(info.Version) + "...");
                UpdateResult result = new UpdateResult();
                string running = Application.ExecutablePath;
                ReplaceFile(running, tmp);
                result.RelaunchPath = running;

                // The auto-start copy is what Windows launches at sign-in; keep it in step too.
                string installed = TaskSchedulerHelper.GetInstalledExePath();
                if (!string.Equals(Path.GetFullPath(installed), Path.GetFullPath(running), StringComparison.OrdinalIgnoreCase) && File.Exists(installed))
                {
                    try { ReplaceFile(installed, tmp); }
                    catch (Exception ex) { result.Warning = "Could not update the auto-start copy at " + installed + ": " + ex.Message; }
                }
                RefreshInstallerVersion(info.Version);
                return result;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        // Setup.exe installs register under this key; keep "Apps & features" showing the right version.
        public const string INSTALLER_UNINSTALL_KEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{0D6C3141-13D7-410F-A775-B2301C4B7061}_is1";

        private static void RefreshInstallerVersion(Version v)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(INSTALLER_UNINSTALL_KEY, true))
                {
                    if (key == null) return;
                    string text = string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", v.Major, v.Minor, v.Build);
                    key.SetValue("DisplayVersion", text);
                    key.SetValue("DisplayName", "Redgear A20 Control Center " + text);
                }
            }
            catch { }
        }

        private static void Download(string url, string dest)
        {
            HttpWebRequest req = CreateRequest(url, 30000);
            using (WebResponse resp = req.GetResponse())
            {
                if (!string.Equals(resp.ResponseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing a non-HTTPS download.");
                using (Stream s = resp.GetResponseStream())
                using (FileStream fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write))
                {
                    byte[] buf = new byte[81920];
                    long total = 0;
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += n;
                        if (total > MAX_DOWNLOAD_BYTES) throw new InvalidOperationException("The download is larger than expected.");
                        fs.Write(buf, 0, n);
                    }
                }
            }
        }

        private static string Sha256Of(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // A running image cannot be overwritten, but it can be renamed out of the way.
        private static void ReplaceFile(string target, string source)
        {
            if (!File.Exists(target))
            {
                File.Copy(source, target, false);
                return;
            }
            string old = target + "." + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) + ".old";
            File.Move(target, old);
            try
            {
                File.Copy(source, target, false);
            }
            catch
            {
                try { File.Move(old, target); } catch { }
                throw;
            }
            try { File.Delete(old); } catch { } // fails while the old image is still running; cleaned up on next start
        }

        // Removes "<exe>.<ticks>.old" leftovers from a previous update.
        public static void CleanupOldBinaries()
        {
            try
            {
                string exe = Application.ExecutablePath;
                string dir = Path.GetDirectoryName(exe);
                foreach (string f in Directory.GetFiles(dir, Path.GetFileName(exe) + ".*.old"))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        public static void OpenInBrowser(string url)
        {
            try { Process.Start(url); } catch { }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI Design System Tokens (Dark / Light, swapped in place by Theme.Apply)
    // ═════════════════════════════════════════════════════════════════════════
    public static class Theme
    {
        public static bool IsDark = true;

        // ─── Colors: Base Neutrals ───
        public static Color Canvas, Surface, SurfaceElevated, SurfaceHover, Border, BorderSubtle;
        // ─── Colors: Text Neutrals ───
        public static Color TextPrimary, TextSecondary, TextMuted, TextDisabled;
        // ─── Colors: Single Primary Accent (Emerald / Hardware Mint) ───
        public static Color Accent, AccentHover, AccentDim;
        public static readonly Color AccentText = Color.White;
        // ─── Colors: Functional Semantic States ───
        public static Color Warning, WarningSurface, WarningBorder, Danger, DangerSurface, DangerBorder;
        // The diagnostic terminal stays dark in both themes.
        public static readonly Color TerminalBg = Color.FromArgb(14, 15, 18);
        public static readonly Color TerminalText = Color.FromArgb(16, 185, 129);

        static Theme()
        {
            Apply(true);
        }

        public static void Apply(bool dark)
        {
            IsDark = dark;
            if (dark)
            {
                Canvas = Color.FromArgb(18, 19, 22);
                Surface = Color.FromArgb(26, 28, 34);
                SurfaceElevated = Color.FromArgb(36, 39, 49);
                SurfaceHover = Color.FromArgb(45, 49, 62);
                Border = Color.FromArgb(43, 47, 58);
                BorderSubtle = Color.FromArgb(32, 35, 42);
                TextPrimary = Color.FromArgb(241, 243, 247);
                TextSecondary = Color.FromArgb(141, 147, 165);
                TextMuted = Color.FromArgb(100, 106, 124);
                TextDisabled = Color.FromArgb(82, 88, 102);
                Accent = Color.FromArgb(16, 185, 129);
                AccentHover = Color.FromArgb(5, 150, 105);
                AccentDim = Color.FromArgb(14, 52, 42);
                Warning = Color.FromArgb(245, 158, 11);
                WarningSurface = Color.FromArgb(40, 30, 16);
                WarningBorder = Color.FromArgb(92, 61, 16);
                Danger = Color.FromArgb(239, 68, 68);
                DangerSurface = Color.FromArgb(40, 20, 22);
                DangerBorder = Color.FromArgb(77, 29, 34);
            }
            else
            {
                Canvas = Color.FromArgb(243, 244, 246);
                Surface = Color.White;
                SurfaceElevated = Color.FromArgb(236, 238, 242);
                SurfaceHover = Color.FromArgb(225, 228, 234);
                Border = Color.FromArgb(220, 223, 230);
                BorderSubtle = Color.FromArgb(232, 234, 238);
                TextPrimary = Color.FromArgb(22, 24, 29);
                TextSecondary = Color.FromArgb(84, 90, 104);
                TextMuted = Color.FromArgb(120, 126, 140);
                TextDisabled = Color.FromArgb(165, 170, 180);
                Accent = Color.FromArgb(5, 150, 105);
                AccentHover = Color.FromArgb(4, 120, 87);
                AccentDim = Color.FromArgb(220, 245, 235);
                Warning = Color.FromArgb(176, 100, 0);
                WarningSurface = Color.FromArgb(254, 245, 228);
                WarningBorder = Color.FromArgb(240, 200, 130);
                Danger = Color.FromArgb(205, 38, 38);
                DangerSurface = Color.FromArgb(253, 236, 236);
                DangerBorder = Color.FromArgb(245, 185, 185);
            }
        }

        // pref: 0 = follow Windows app theme, 1 = dark, 2 = light.
        public static bool ResolveDark(int pref)
        {
            if (pref == 1) return true;
            if (pref == 2) return false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key != null ? key.GetValue("AppsUseLightTheme") : null;
                    if (v is int) return (int)v == 0;
                }
            }
            catch { }
            return false;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Pixel scaling + pixel-unit fonts for the main window (AutoScaleMode.None)
    // ═════════════════════════════════════════════════════════════════════════
    public static class Ui
    {
        public static float Scale = 1f;
        public static Font Title, Subtitle, Body, BodyBold, Small, Caption, Mono, Icon, IconSmall, IconLarge, HeroName, Brand, Big;
        public static string IconFamily;
        private static bool iconProbed = false;

        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static float Sf(float v) { return v * Scale; }
        public static Rectangle R(int x, int y, int w, int h) { return new Rectangle(S(x), S(y), S(w), S(h)); }

        public static void Init(float scale)
        {
            Scale = Math.Max(1f, Math.Min(4f, scale));
            if (!iconProbed)
            {
                iconProbed = true;
                string[] names = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };
                for (int i = 0; i < names.Length && IconFamily == null; i++)
                {
                    try { using (FontFamily f = new FontFamily(names[i])) IconFamily = names[i]; } catch { }
                }
            }
            Font[] old = { Title, Subtitle, Body, BodyBold, Small, Caption, Mono, Icon, IconSmall, IconLarge, HeroName, Brand, Big };
            Title = Px("Segoe UI Semibold", 20, FontStyle.Regular);
            Subtitle = Px("Segoe UI", 12.5f, FontStyle.Regular);
            Body = Px("Segoe UI", 13, FontStyle.Regular);
            BodyBold = Px("Segoe UI Semibold", 13, FontStyle.Regular);
            Small = Px("Segoe UI", 12, FontStyle.Regular);
            Caption = Px("Segoe UI", 11, FontStyle.Bold);
            Mono = Px("Consolas", 11.5f, FontStyle.Regular);
            HeroName = Px("Segoe UI Semibold", 19, FontStyle.Regular);
            Brand = Px("Segoe UI", 11, FontStyle.Bold);
            Big = Px("Segoe UI Semibold", 17, FontStyle.Regular);
            string iconName = IconFamily ?? "Segoe UI Symbol";
            Icon = Px(iconName, 16, FontStyle.Regular);
            IconSmall = Px(iconName, 13, FontStyle.Regular);
            IconLarge = Px(iconName, 20, FontStyle.Regular);
            for (int i = 0; i < old.Length; i++) if (old[i] != null) old[i].Dispose();
        }

        private static Font Px(string family, float px, FontStyle style)
        {
            return new Font(family, px * Scale, style, GraphicsUnit.Pixel);
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (Pen pen = new Pen(c, width))
                g.DrawPath(pen, p);
        }

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        public static void Glyph(Graphics g, string glyph, Font f, Rectangle r, Color c)
        {
            if (string.IsNullOrEmpty(glyph) || IconFamily == null) return;
            TextRenderer.DrawText(g, glyph, f, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        public static void PaintParentBackground(Control c, Graphics g)
        {
            IBackgroundPainter painter = c.Parent as IBackgroundPainter;
            if (painter != null) painter.PaintBackgroundFor(c, g);
            else g.Clear(c.Parent != null ? c.Parent.BackColor : Theme.Canvas);
        }

        // Classic 16x16 octicon "mark-github" path (M/C/c/L/Z + one circular A segment).
        private const string GITHUB_PATH = "M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.89-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0 0 16 8c0-4.42-3.58-8-8-8z";

        public static GraphicsPath GitHubMark()
        {
            GraphicsPath path = new GraphicsPath(FillMode.Alternate);
            MatchCollection tokens = Regex.Matches(GITHUB_PATH, "[MmCcLlZzA]|-?(?:\\d+\\.?\\d*|\\.\\d+)");
            char cmd = 'M';
            float cx = 0, cy = 0, sx = 0, sy = 0;
            List<float> nums = new List<float>();
            int i = 0;
            while (i < tokens.Count)
            {
                string t = tokens[i].Value;
                if (char.IsLetter(t[0]))
                {
                    cmd = t[0];
                    i++;
                    if (cmd == 'Z' || cmd == 'z') { path.CloseFigure(); cx = sx; cy = sy; }
                    continue;
                }
                int need = (cmd == 'C' || cmd == 'c') ? 6 : (cmd == 'A' ? 7 : 2);
                nums.Clear();
                while (nums.Count < need && i < tokens.Count && !char.IsLetter(tokens[i].Value[0]))
                {
                    nums.Add(float.Parse(tokens[i].Value, CultureInfo.InvariantCulture));
                    i++;
                }
                if (nums.Count < need) break;
                switch (cmd)
                {
                    case 'M':
                        path.StartFigure(); cx = sx = nums[0]; cy = sy = nums[1]; cmd = 'L'; break;
                    case 'L':
                        path.AddLine(cx, cy, nums[0], nums[1]); cx = nums[0]; cy = nums[1]; break;
                    case 'l':
                        path.AddLine(cx, cy, cx + nums[0], cy + nums[1]); cx += nums[0]; cy += nums[1]; break;
                    case 'C':
                        path.AddBezier(cx, cy, nums[0], nums[1], nums[2], nums[3], nums[4], nums[5]); cx = nums[4]; cy = nums[5]; break;
                    case 'c':
                        path.AddBezier(cx, cy, cx + nums[0], cy + nums[1], cx + nums[2], cy + nums[3], cx + nums[4], cy + nums[5]);
                        cx += nums[4]; cy += nums[5]; break;
                    case 'A':
                        AddSvgArc(path, cx, cy, nums[0], nums[3] != 0, nums[4] != 0, nums[5], nums[6]);
                        cx = nums[5]; cy = nums[6]; break;
                }
            }
            return path;
        }

        // Circular SVG arc (rx == ry, no rotation) via endpoint-to-center conversion.
        private static void AddSvgArc(GraphicsPath path, float x0, float y0, float r, bool large, bool sweep, float x, float y)
        {
            double x1 = (x0 - x) / 2.0, y1 = (y0 - y) / 2.0;
            double d2 = x1 * x1 + y1 * y1;
            if (d2 < 1e-9) return;
            double rr = Math.Max(r, Math.Sqrt(d2));
            double coef = Math.Sqrt(Math.Max(0, (rr * rr - d2) / d2));
            if (large == sweep) coef = -coef;
            double ccx = coef * y1, ccy = -coef * x1;
            double centerX = ccx + (x0 + x) / 2.0, centerY = ccy + (y0 + y) / 2.0;
            double t1 = Math.Atan2((y1 - ccy) / rr, (x1 - ccx) / rr);
            double t2 = Math.Atan2((-y1 - ccy) / rr, (-x1 - ccx) / rr);
            double dt = t2 - t1;
            if (!sweep && dt > 0) dt -= 2 * Math.PI;
            if (sweep && dt < 0) dt += 2 * Math.PI;
            path.AddArc((float)(centerX - rr), (float)(centerY - rr), (float)(2 * rr), (float)(2 * rr),
                (float)(t1 * 180 / Math.PI), (float)(dt * 180 / Math.PI));
        }
    }

    public static class Glyphs
    {
        public const string Lighting = "\uE706", Dpi = "\uEC4A", Buttons = "\uE962", Settings = "\uE713", Add = "\uE710",
            More = "\uE712", Refresh = "\uE72C", Lock = "\uE72E", Check = "\uE73E", Download = "\uE896", Warning = "\uE7BA",
            Play = "\uE768", Save = "\uE74E", Color = "\uE790", Copy = "\uE8C8", Delete = "\uE74D", Rename = "\uE8AC",
            Power = "\uE7E8", Undo = "\uE7A7", ChevronDown = "\uE70D", Info = "\uE946";
    }

    public interface IBackgroundPainter
    {
        void PaintBackgroundFor(Control child, Graphics g);
    }

    // ─── Context menus (profile actions, update pill) ───
    public class UiColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    public class UiMenuRenderer : ToolStripProfessionalRenderer
    {
        public UiMenuRenderer() : base(new UiColorTable()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Theme.TextDisabled : (e.Item.Tag as string == "danger" ? Theme.Danger : Theme.TextPrimary);
            base.OnRenderItemText(e);
        }

        // The stock check mark is nearly invisible on the dark surface.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            r.Inflate(Ui.S(2), Ui.S(2));
            Ui.Glyph(e.Graphics, Glyphs.Check, Ui.IconSmall, r, Theme.Accent);
        }

        public static ContextMenuStrip Create()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new UiMenuRenderer();
            m.Font = Ui.Body;
            m.ShowImageMargin = false;
            m.Padding = new Padding(0, Ui.S(4), 0, Ui.S(4));
            return m;
        }

        public static ToolStripMenuItem Item(ContextMenuStrip m, string text, EventHandler click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Padding = new Padding(Ui.S(6), Ui.S(4), Ui.S(6), Ui.S(4));
            it.Click += click;
            m.Items.Add(it);
            return it;
        }
    }

    // ─── Buttons ───
    public enum UiButtonKind { Primary, Secondary, Ghost, Danger, Warning, HeroIcon, HeroChip }

    public class UiButton : Button
    {
        public UiButtonKind Kind;
        public string Glyph;
        public GraphicsPath IconPath; // 16x16 design units, owned by the button
        public bool Active;
        private bool hover, down;

        public UiButton(UiButtonKind kind, string text, string glyph)
        {
            Kind = kind;
            Text = text ?? "";
            Glyph = glyph;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Font = kind == UiButtonKind.Primary ? Ui.BodyBold : Ui.Body;
            UseVisualStyleBackColor = false;
            if (string.IsNullOrEmpty(text) && glyph != null) AccessibleName = glyph;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && IconPath != null) { IconPath.Dispose(); IconPath = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = Color.Empty, border = Color.Empty, fore;
            bool hi = Enabled && (hover || Focused && ShowFocusCues);
            switch (Kind)
            {
                case UiButtonKind.Primary:
                    fill = !Enabled ? Theme.SurfaceElevated : (down ? Theme.AccentHover : (hover ? Theme.Blend(Theme.Accent, Theme.AccentHover, 0.5f) : Theme.Accent));
                    fore = Enabled ? Theme.AccentText : Theme.TextDisabled;
                    break;
                case UiButtonKind.Secondary:
                    fill = down ? Theme.Border : (hover && Enabled ? Theme.SurfaceHover : Theme.SurfaceElevated);
                    border = Theme.Border;
                    fore = Enabled ? Theme.TextPrimary : Theme.TextDisabled;
                    break;
                case UiButtonKind.Ghost:
                    if (Enabled && (hover || Active)) fill = down ? Theme.SurfaceHover : Theme.SurfaceElevated;
                    fore = !Enabled ? Theme.TextDisabled : (hover || Active ? Theme.TextPrimary : Theme.TextSecondary);
                    break;
                case UiButtonKind.Danger:
                    fill = hover && Enabled ? Theme.Blend(Theme.DangerSurface, Theme.DangerBorder, 0.4f) : Theme.DangerSurface;
                    border = Theme.DangerBorder;
                    fore = Enabled ? Theme.Danger : Theme.TextDisabled;
                    break;
                case UiButtonKind.Warning:
                    fill = hover && Enabled ? Theme.Blend(Theme.WarningSurface, Theme.WarningBorder, 0.35f) : Theme.WarningSurface;
                    border = Theme.WarningBorder;
                    fore = Enabled ? Theme.Warning : Theme.TextDisabled;
                    break;
                case UiButtonKind.HeroIcon:
                    int a = Active ? 46 : (down ? 56 : (hover ? 34 : 0));
                    if (a > 0) fill = Color.FromArgb(a, 255, 255, 255);
                    fore = Color.FromArgb(Enabled ? (hover || Active ? 255 : 210) : 90, 255, 255, 255);
                    break;
                default: // HeroChip
                    fill = Color.FromArgb(down ? 70 : (hover ? 54 : 36), 255, 255, 255);
                    border = Color.FromArgb(60, 255, 255, 255);
                    fore = Color.White;
                    break;
            }

            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = (Kind == UiButtonKind.HeroChip || Kind == UiButtonKind.HeroIcon) ? Height / 2f : Ui.Sf(6);
            if (!fill.IsEmpty) Ui.FillRound(g, fill, rect, radius);
            if (!border.IsEmpty) Ui.DrawRound(g, border, rect, radius, 1f);
            if (Focused && ShowFocusCues && Enabled)
                Ui.DrawRound(g, Kind == UiButtonKind.Primary ? Theme.TextPrimary : Theme.Accent, new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f), radius, Math.Max(1.5f, Ui.Sf(1.5f)));

            bool hasIcon = (Glyph != null && Ui.IconFamily != null) || IconPath != null;
            int iconSize = Kind == UiButtonKind.HeroIcon ? Ui.S(20) : Ui.S(15);
            int gap = Text.Length > 0 && hasIcon ? Ui.S(8) : 0;
            Size ts = Text.Length > 0 ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix) : Size.Empty;
            int total = (hasIcon ? iconSize : 0) + gap + ts.Width;
            int x = (Width - total) / 2;
            if (hasIcon)
            {
                Rectangle ir = new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize);
                if (IconPath != null)
                {
                    GraphicsState st = g.Save();
                    g.TranslateTransform(ir.X, ir.Y);
                    g.ScaleTransform(iconSize / 16f, iconSize / 16f);
                    using (SolidBrush b = new SolidBrush(fore)) g.FillPath(b, IconPath);
                    g.Restore(st);
                }
                else
                {
                    Ui.Glyph(g, Glyph, Kind == UiButtonKind.HeroIcon ? Ui.IconLarge : Ui.IconSmall, ir, fore);
                }
                x += iconSize + gap;
            }
            if (ts.Width > 0)
            {
                Ui.Text(g, Text, Font, new Rectangle(x, 0, ts.Width + 2, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    // ─── Toggle switch ───
    public class UiToggle : CheckBox
    {
        private bool hover;
        public UiToggle(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Ui.BodyBold;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int tw = Ui.S(40), th = Ui.S(22);
            RectangleF track = new RectangleF(0.5f, (Height - th) / 2f, tw, th);
            Color fill = Checked ? (hover ? Theme.AccentHover : Theme.Accent) : (hover ? Theme.SurfaceHover : Theme.SurfaceElevated);
            Ui.FillRound(g, fill, track, th / 2f);
            if (!Checked) Ui.DrawRound(g, Theme.Border, track, th / 2f, 1f);
            float k = th - Ui.Sf(8);
            float kx = Checked ? track.Right - Ui.Sf(4) - k : track.X + Ui.Sf(4);
            using (SolidBrush b = new SolidBrush(Checked ? Color.White : Theme.TextSecondary))
                g.FillEllipse(b, kx, track.Y + Ui.Sf(4), k, k);
            if (Focused && ShowFocusCues) Ui.DrawRound(g, Theme.Accent, RectangleF.Inflate(track, 2, 2), th / 2f + 2, 1.5f);
            Ui.Text(g, Text, Font, new Rectangle(tw + Ui.S(12), 0, Width - tw - Ui.S(12), Height), Enabled ? Theme.TextPrimary : Theme.TextDisabled,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // ─── Slider ───
    public class UiSlider : Control
    {
        private int min = 0, max = 100, value = 50;
        private bool dragging, hover;
        public event EventHandler ValueChanged;
        public event EventHandler ValueCommitted;

        public UiSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return min; } set { min = value; Value = this.value; } }
        public int Maximum { get { return max; } set { max = value; Value = this.value; } }
        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        private float Pad { get { return Ui.Sf(10); } }

        private void SetFromX(int x)
        {
            float w = Width - 2 * Pad;
            if (w <= 0) return;
            float t = Math.Max(0, Math.Min(1, (x - Pad) / w));
            Value = min + (int)Math.Round(t * (max - min));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = 0;
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: step = -1; break;
                case Keys.Right: case Keys.Up: step = 1; break;
                case Keys.PageDown: step = -10; break;
                case Keys.PageUp: step = 10; break;
                case Keys.Home: Value = min; Commit(); e.Handled = true; return;
                case Keys.End: Value = max; Commit(); e.Handled = true; return;
            }
            if (step != 0) { Value += step; Commit(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void Commit() { if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            dragging = true;
            Capture = true;
            SetFromX(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (dragging) { dragging = false; Capture = false; Commit(); } }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float th = Ui.Sf(4), cy = Height / 2f;
            float w = Width - 2 * Pad;
            float t = max > min ? (value - min) / (float)(max - min) : 0;
            Ui.FillRound(g, Theme.SurfaceHover, new RectangleF(Pad, cy - th / 2, w, th), th / 2);
            Ui.FillRound(g, Enabled ? Theme.Accent : Theme.TextDisabled, new RectangleF(Pad, cy - th / 2, Math.Max(th, w * t), th), th / 2);
            float kr = Ui.Sf(dragging || hover ? 9 : 8);
            float kx = Pad + w * t;
            if (Focused && ShowFocusCues)
                using (SolidBrush ring = new SolidBrush(Color.FromArgb(60, Theme.Accent)))
                    g.FillEllipse(ring, kx - kr - Ui.Sf(4), cy - kr - Ui.Sf(4), 2 * kr + Ui.Sf(8), 2 * kr + Ui.Sf(8));
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - kr, cy - kr, 2 * kr, 2 * kr);
            using (Pen p = new Pen(Enabled ? Theme.Accent : Theme.TextDisabled, Ui.Sf(2.5f))) g.DrawEllipse(p, kx - kr, cy - kr, 2 * kr, 2 * kr);
        }
    }

    // ─── Segmented control (theme picker) ───
    public class UiSegmented : Control
    {
        public string[] Items = new string[0];
        private int selected = 0, hoverIndex = -1;
        public event EventHandler SelectedIndexChanged;

        public UiSegmented()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Ui.Body;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected || value < 0 || value >= Items.Length) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        private int IndexAt(int x) { return Items.Length == 0 ? -1 : Math.Max(0, Math.Min(Items.Length - 1, x * Items.Length / Math.Max(1, Width))); }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && selected > 0) SelectedIndex = selected - 1;
            else if (e.KeyCode == Keys.Right && selected < Items.Length - 1) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e) { int h = IndexAt(e.X); if (h != hoverIndex) { hoverIndex = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); SelectedIndex = IndexAt(e.X); } }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float r = Ui.Sf(8);
            Ui.FillRound(g, Theme.SurfaceElevated, outer, r);
            if (Items.Length == 0) return;
            float segW = (Width - Ui.Sf(8)) / (float)Items.Length;
            for (int i = 0; i < Items.Length; i++)
            {
                RectangleF seg = new RectangleF(Ui.Sf(4) + i * segW, Ui.Sf(4), segW, Height - Ui.Sf(8));
                if (i == selected)
                {
                    Ui.FillRound(g, Theme.IsDark ? Theme.SurfaceHover : Color.White, seg, r - Ui.Sf(3));
                    if (!Theme.IsDark) Ui.DrawRound(g, Theme.Border, seg, r - Ui.Sf(3), 1f);
                    if (Focused && ShowFocusCues) Ui.DrawRound(g, Theme.Accent, seg, r - Ui.Sf(3), 1.5f);
                }
                Color fc = i == selected ? Theme.TextPrimary : (i == hoverIndex ? Theme.TextPrimary : Theme.TextSecondary);
                Ui.Text(g, Items[i], i == selected ? Ui.BodyBold : Font, Rectangle.Round(seg), fc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    // ─── Bottom icon tab bar ───
    public class UiTabBar : Control
    {
        private readonly string[] labels, glyphs;
        private int selected = 0, hoverIndex = -1;
        public event EventHandler SelectedIndexChanged;

        public UiTabBar(string[] labels, string[] glyphs)
        {
            this.labels = labels;
            this.glyphs = glyphs;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PageTabList;
        }

        // -1 = no tab highlighted (Settings is open).
        public int SelectedIndex
        {
            get { return selected; }
            set { if (value != selected) { selected = value; Invalidate(); } }
        }

        private void Pick(int i)
        {
            selected = i;
            Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        private int IndexAt(int x) { return Math.Max(0, Math.Min(labels.Length - 1, x * labels.Length / Math.Max(1, Width))); }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int cur = selected < 0 ? 0 : selected;
            if (e.KeyCode == Keys.Left) Pick(Math.Max(0, cur - 1));
            else if (e.KeyCode == Keys.Right) Pick(Math.Min(labels.Length - 1, cur + 1));
            else if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && selected < 0) Pick(0);
            base.OnKeyDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e) { int h = IndexAt(e.X); if (h != hoverIndex) { hoverIndex = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) Pick(IndexAt(e.X)); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, 0, Width, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Width / (float)labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                Rectangle cell = new Rectangle((int)(i * w), 0, (int)w, Height);
                bool sel = i == selected;
                Color c = sel ? Theme.Accent : (i == hoverIndex ? Theme.TextPrimary : Theme.TextSecondary);
                if (sel) Ui.FillRound(g, Theme.Accent, new RectangleF(cell.X + cell.Width / 2f - Ui.Sf(18), 0, Ui.Sf(36), Ui.Sf(3)), Ui.Sf(1.5f));
                Ui.Glyph(g, glyphs[i], Ui.Icon, new Rectangle(cell.X, Ui.S(12), cell.Width, Ui.S(22)), c);
                Ui.Text(g, labels[i], sel ? Ui.BodyBold : Ui.Small, new Rectangle(cell.X, Ui.S(36), cell.Width, Ui.S(18)), c,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (Focused && ShowFocusCues && (sel || (selected < 0 && i == 0)))
                    Ui.DrawRound(g, Theme.Accent, new RectangleF(cell.X + Ui.Sf(8), Ui.Sf(6), cell.Width - Ui.Sf(16), Height - Ui.Sf(12)), Ui.Sf(6), 1.5f);
            }
        }
    }

    // ─── Rounded card container ───
    public class RoundPanel : Panel
    {
        public Color Fill = Color.Empty, Stroke = Color.Empty;
        public int Radius = 8;

        public RoundPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Canvas);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, Fill.IsEmpty ? BackColor : Fill, r, Ui.Sf(Radius));
            if (!Stroke.IsEmpty) Ui.DrawRound(g, Stroke, r, Ui.Sf(Radius), 1f);
            base.OnPaint(e);
        }
    }

    // ─── Colour swatches + custom "+" swatch ───
    public class SwatchRow : Control
    {
        public Color[] Colors = new Color[0];
        public string[] Names = new string[0];
        private Color selectedColor = Color.Empty;
        private int hoverIndex = -1, focusIndex = 0;
        public event EventHandler ColorPicked;     // preset chosen (SelectedColor holds it)
        public event EventHandler CustomRequested; // "+" chosen
        private readonly ToolTip tip = new ToolTip();
        private int tipIndex = -2;

        public SwatchRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public Color SelectedColor
        {
            get { return selectedColor; }
            set { selectedColor = value; Invalidate(); }
        }

        private int Count { get { return Colors.Length + 1; } }
        private float Cell { get { return Width / (float)Count; } }

        private int IndexAt(int x) { return Math.Max(0, Math.Min(Count - 1, (int)(x / Cell))); }

        private int PresetIndexOf(Color c)
        {
            for (int i = 0; i < Colors.Length; i++) if (Colors[i].ToArgb() == c.ToArgb()) return i;
            return -1;
        }

        private void Choose(int i)
        {
            if (i < Colors.Length)
            {
                selectedColor = Colors[i];
                Invalidate();
                if (ColorPicked != null) ColorPicked(this, EventArgs.Empty);
            }
            else if (CustomRequested != null) CustomRequested(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { focusIndex = Math.Max(0, focusIndex - 1); Invalidate(); }
            else if (e.KeyCode == Keys.Right) { focusIndex = Math.Min(Count - 1, focusIndex + 1); Invalidate(); }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Choose(focusIndex); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e)
        {
            int p = PresetIndexOf(selectedColor);
            focusIndex = p >= 0 ? p : Count - 1;
            Invalidate();
            base.OnGotFocus(e);
        }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = IndexAt(e.X);
            if (h != hoverIndex) { hoverIndex = h; Invalidate(); }
            if (h != tipIndex)
            {
                tipIndex = h;
                tip.SetToolTip(this, h < Names.Length ? Names[h] : "Custom colour…");
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; tipIndex = -2; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { focusIndex = IndexAt(e.X); Choose(focusIndex); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Math.Min(Cell - Ui.Sf(8), Height - Ui.Sf(8));
            int sel = PresetIndexOf(selectedColor);
            for (int i = 0; i < Count; i++)
            {
                float cx = i * Cell + Cell / 2f, cy = Height / 2f;
                float dd = d - (i == hoverIndex ? 0 : Ui.Sf(2));
                RectangleF r = new RectangleF(cx - dd / 2, cy - dd / 2, dd, dd);
                bool isSel = i < Colors.Length ? i == sel : (sel < 0 && !selectedColor.IsEmpty);
                if (i < Colors.Length)
                {
                    using (SolidBrush b = new SolidBrush(Colors[i])) g.FillEllipse(b, r);
                }
                else if (isSel)
                {
                    using (SolidBrush b = new SolidBrush(selectedColor)) g.FillEllipse(b, r);
                    Ui.Glyph(g, Glyphs.Color, Ui.IconSmall, Rectangle.Round(r), selectedColor.GetBrightness() > 0.6f ? Color.Black : Color.White);
                }
                else
                {
                    using (Pen p = new Pen(Theme.TextMuted, Ui.Sf(1.5f)) { DashStyle = DashStyle.Dash }) g.DrawEllipse(p, r);
                    Ui.Glyph(g, Glyphs.Add, Ui.IconSmall, Rectangle.Round(r), Theme.TextSecondary);
                }
                if (i < Colors.Length && Colors[i].GetBrightness() > 0.85f)
                    using (Pen p = new Pen(Theme.Border)) g.DrawEllipse(p, r);
                if (isSel)
                {
                    float o = Ui.Sf(4);
                    using (Pen p = new Pen(Theme.TextPrimary, Ui.Sf(2))) g.DrawEllipse(p, r.X - o, r.Y - o, r.Width + 2 * o, r.Height + 2 * o);
                }
                else if (Focused && ShowFocusCues && i == focusIndex)
                {
                    float o = Ui.Sf(4);
                    using (Pen p = new Pen(Theme.Accent, Ui.Sf(1.5f))) g.DrawEllipse(p, r.X - o, r.Y - o, r.Width + 2 * o, r.Height + 2 * o);
                }
            }
        }
    }

    // ─── Card-style radio button (background mode) ───
    // RadioButton "clicks" itself whenever it gains focus without a mouse or Tab (e.g. when the window
    // is re-activated), which would silently change a setting. These only check on explicit input.
    public class ExplicitRadio : RadioButton
    {
        public ExplicitRadio() { AutoCheck = false; }

        public void Choose()
        {
            if (Checked) return;
            if (Parent != null)
                foreach (Control c in Parent.Controls)
                {
                    ExplicitRadio o = c as ExplicitRadio;
                    if (o != null && o != this && o.GetType() == GetType()) o.Checked = false;
                }
            Checked = true;
        }
        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); if (e.Button == MouseButtons.Left) Choose(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.Handled = true; Choose(); } }
    }

    public class OptionCard : ExplicitRadio
    {
        public string Description;
        public string Badge;
        private bool hover;

        public OptionCard(string title, string description, string badge)
        {
            Text = title;
            Description = description;
            Badge = badge;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Ui.BodyBold;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, Checked ? Theme.AccentDim : (hover ? Theme.SurfaceElevated : Theme.Surface), r, Ui.Sf(8));
            Ui.DrawRound(g, Checked ? Theme.Accent : (Focused && ShowFocusCues ? Theme.Accent : Theme.Border), r, Ui.Sf(8), Checked ? 1.5f : 1f);
            float d = Ui.Sf(18), cx = Ui.Sf(16), cy = Ui.Sf(16);
            using (Pen p = new Pen(Checked ? Theme.Accent : Theme.TextMuted, Ui.Sf(1.5f))) g.DrawEllipse(p, cx, cy, d, d);
            if (Checked) using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, cx + Ui.Sf(4.5f), cy + Ui.Sf(4.5f), d - Ui.Sf(9), d - Ui.Sf(9));
            int tx = Ui.S(46);
            Size ts = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            Ui.Text(g, Text, Font, new Rectangle(tx, Ui.S(14), Width - tx - Ui.S(12), Ui.S(20)), Theme.TextPrimary, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            if (!string.IsNullOrEmpty(Badge))
            {
                Size bs = TextRenderer.MeasureText(g, Badge, Ui.Caption, Size.Empty, TextFormatFlags.NoPadding);
                RectangleF br = new RectangleF(tx + ts.Width + Ui.S(8), Ui.S(15), bs.Width + Ui.S(12), Ui.S(18));
                Ui.FillRound(g, Checked ? Theme.Accent : Theme.SurfaceHover, br, br.Height / 2);
                Ui.Text(g, Badge, Ui.Caption, Rectangle.Round(br), Checked ? Color.White : Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            Ui.Text(g, Description, Ui.Small, new Rectangle(tx, Ui.S(36), Width - tx - Ui.S(14), Height - Ui.S(40)), Theme.TextSecondary, TextFormatFlags.WordBreak);
        }
    }

    // ─── DPI stage tile ───
    public class StageTile : ExplicitRadio
    {
        public int Dpi;
        public Color StageColor;
        private bool hover;

        public StageTile(int dpi, Color stageColor)
        {
            Dpi = dpi;
            StageColor = stageColor;
            Text = dpi + " DPI";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool on = Checked && Enabled;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, on ? Theme.AccentDim : (hover && Enabled ? Theme.SurfaceElevated : Theme.Surface), r, Ui.Sf(8));
            Ui.DrawRound(g, on ? Theme.Accent : (Focused && ShowFocusCues ? Theme.Accent : Theme.Border), r, Ui.Sf(8), on ? 1.5f : 1f);
            Color fc = Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            Ui.Text(g, Dpi.ToString(CultureInfo.InvariantCulture), Ui.Big, new Rectangle(Ui.S(14), Ui.S(12), Width - Ui.S(28), Ui.S(26)), fc, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            Ui.Text(g, "DPI", Ui.Caption, new Rectangle(Ui.S(14), Ui.S(40), Width - Ui.S(28), Ui.S(16)), Enabled ? Theme.TextSecondary : Theme.TextDisabled, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            using (SolidBrush b = new SolidBrush(Enabled ? StageColor : Theme.TextDisabled))
                g.FillEllipse(b, Width - Ui.Sf(24), Ui.Sf(14), Ui.Sf(10), Ui.Sf(10));
            if (on) Ui.Glyph(g, Glyphs.Check, Ui.IconSmall, new Rectangle(Width - Ui.S(30), Height - Ui.S(28), Ui.S(20), Ui.S(20)), Theme.Accent);
        }
    }

    // ─── Read-only button binding summary ───
    // Button bindings; clicking an unlocked row asks the owner to show its action menu.
    public class BindingListView : Control
    {
        private AppProfile profile;
        private int hoverIndex = -1, focusIndex = -1;
        public int RowHeight { get { return Ui.S(38); } }

        // Button id and the row's bounds in screen coordinates (to place the menu).
        public event Action<byte, Rectangle> RowActivated;

        public BindingListView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
        }

        public AppProfile Profile { get { return profile; } set { profile = value; Invalidate(); } }

        private int IndexAt(Point p)
        {
            int i = p.Y / RowHeight;
            return p.X >= 0 && p.X < Width && i >= 0 && i < ButtonMap.All.Length ? i : -1;
        }

        private static bool Editable(int i) { return i >= 0 && i < ButtonMap.All.Length && !ButtonMap.All[i].Locked; }

        private void Activate(int i)
        {
            if (!Editable(i) || !Enabled) return;
            focusIndex = i;
            Invalidate();
            if (RowActivated != null)
                RowActivated(ButtonMap.All[i].Id, RectangleToScreen(new Rectangle(0, i * RowHeight, Width, RowHeight)));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = IndexAt(e.Location);
            if (!Editable(h)) h = -1;
            if (h != hoverIndex) { hoverIndex = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            Activate(IndexAt(e.Location));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }

        private void MoveFocus(int dir)
        {
            int i = focusIndex < 0 ? (dir > 0 ? -1 : ButtonMap.All.Length) : focusIndex;
            for (int n = i + dir; n >= 0 && n < ButtonMap.All.Length; n += dir)
            {
                if (Editable(n)) { focusIndex = n; Invalidate(); return; }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up: MoveFocus(-1); e.Handled = true; break;
                case Keys.Down: MoveFocus(1); e.Handled = true; break;
                case Keys.Enter: case Keys.Space: case Keys.Apps: case Keys.F4: Activate(focusIndex); e.Handled = true; break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { if (!Editable(focusIndex)) MoveFocus(1); Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Ui.FillRound(g, Theme.Surface, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.Sf(8));
            Ui.DrawRound(g, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.Sf(8), 1f);
            ButtonMap.ButtonInfo[] all = ButtonMap.All;
            int rh = RowHeight;
            using (Pen sep = new Pen(Theme.BorderSubtle))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    ButtonMap.ButtonInfo info = all[i];
                    int y = i * rh;
                    if (i > 0) g.DrawLine(sep, Ui.S(14), y, Width - Ui.S(14), y);
                    if (!info.Locked && (i == hoverIndex || (Focused && ShowFocusCues && i == focusIndex)))
                    {
                        RectangleF hr = new RectangleF(Ui.Sf(4), y + Ui.Sf(3), Width - Ui.Sf(8), rh - Ui.Sf(6));
                        if (i == hoverIndex && Enabled) Ui.FillRound(g, Theme.SurfaceHover, hr, Ui.Sf(6));
                        if (Focused && ShowFocusCues && i == focusIndex) Ui.DrawRound(g, Theme.Accent, hr, Ui.Sf(6), 1f);
                    }
                    string action;
                    bool custom = false;
                    if (info.Locked) action = ButtonMap.Describe(info.FactoryAction);
                    else
                    {
                        ButtonAction a = profile != null ? profile.GetBinding(info.Id) : info.FactoryAction;
                        KeyShortcut ks;
                        if (a == ButtonAction.Shortcut && profile.TryGetShortcut(info.Id, out ks)) action = KeyShortcuts.Describe(ks);
                        else action = ButtonMap.Describe(a);
                        custom = a != info.FactoryAction;
                    }
                    Rectangle row = new Rectangle(Ui.S(16), y, Width - Ui.S(32), rh);
                    Ui.Text(g, info.DisplayName, info.Locked ? Ui.Body : Ui.BodyBold, row, info.Locked ? Theme.TextSecondary : Theme.TextPrimary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    Rectangle right = new Rectangle(row.X + row.Width / 2 - Ui.S(20), y, row.Width / 2 + Ui.S(20), rh);
                    if (info.Locked)
                    {
                        Ui.Glyph(g, Glyphs.Lock, Ui.IconSmall, new Rectangle(right.Right - Ui.S(16), y, Ui.S(16), rh), Theme.TextMuted);
                        right.Width -= Ui.S(24);
                    }
                    else
                    {
                        Ui.Glyph(g, Glyphs.ChevronDown, Ui.IconSmall, new Rectangle(right.Right - Ui.S(14), y, Ui.S(14), rh), Enabled ? Theme.TextSecondary : Theme.TextDisabled);
                        right.Width -= Ui.S(22);
                        if (custom)
                        {
                            using (SolidBrush b = new SolidBrush(Enabled ? Theme.Accent : Theme.TextDisabled)) g.FillEllipse(b, right.Right - Ui.Sf(8), y + rh / 2f - Ui.Sf(4), Ui.Sf(8), Ui.Sf(8));
                            right.Width -= Ui.S(18);
                        }
                    }
                    Ui.Text(g, action, Ui.Body, right, !Enabled && !info.Locked ? Theme.TextDisabled : (custom ? Theme.Accent : Theme.TextSecondary),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }
            }
        }
    }

    // ─── Saved profiles list ───
    public class ProfileListView : Control
    {
        public List<AppProfile> Items = new List<AppProfile>();
        public string ActiveId;
        public bool ActiveDirty;
        public AppProfile ActiveDraft; // live values shown for the active row
        private int hoverIndex = -1, focusIndex = 0, scrollY = 0;
        private bool hoverMore;
        private TextBox editor;
        private string editingId;

        public event Action<string> ProfileClicked;
        public event Action<string, Point> MenuRequested;
        public event Action<string, string> RenameCommitted;
        public event Action<string> DeleteRequested;

        public ProfileListView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Saved profiles";
        }

        private int RowH { get { return Ui.S(60); } }
        private int Gap { get { return Ui.S(6); } }
        private int ContentHeight { get { return Items.Count * (RowH + Gap); } }

        private Rectangle RowRect(int i) { return new Rectangle(Ui.S(12), i * (RowH + Gap) - scrollY, Width - Ui.S(24), RowH); }
        private Rectangle MoreRect(Rectangle row) { return new Rectangle(row.Right - Ui.S(36), row.Y + (row.Height - Ui.S(28)) / 2, Ui.S(28), Ui.S(28)); }

        private int IndexAt(Point p)
        {
            for (int i = 0; i < Items.Count; i++) if (RowRect(i).Contains(p)) return i;
            return -1;
        }

        public void RefreshItems()
        {
            if (focusIndex >= Items.Count) focusIndex = Math.Max(0, Items.Count - 1);
            ClampScroll();
            Invalidate();
        }

        public void EnsureVisible(string id)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id != id) continue;
                focusIndex = i;
                Rectangle r = RowRect(i);
                if (r.Top < 0) scrollY += r.Top;
                else if (r.Bottom > Height) scrollY += r.Bottom - Height + Gap;
                ClampScroll();
                Invalidate();
                return;
            }
        }

        private void ClampScroll()
        {
            scrollY = Math.Max(0, Math.Min(scrollY, Math.Max(0, ContentHeight - Height)));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            CommitEdit();
            scrollY -= e.Delta / 120 * (RowH + Gap);
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = IndexAt(e.Location);
            bool m = h >= 0 && MoreRect(RowRect(h)).Contains(e.Location);
            if (h != hoverIndex || m != hoverMore) { hoverIndex = h; hoverMore = m; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; hoverMore = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int i = IndexAt(e.Location);
            if (i < 0) return;
            focusIndex = i;
            Invalidate();
            Rectangle row = RowRect(i);
            if (e.Button == MouseButtons.Right || MoreRect(row).Contains(e.Location))
            {
                Rectangle mr = MoreRect(row);
                if (MenuRequested != null) MenuRequested(Items[i].Id, PointToScreen(e.Button == MouseButtons.Right ? e.Location : new Point(mr.Left, mr.Bottom)));
                return;
            }
            if (e.Button == MouseButtons.Left && ProfileClicked != null) ProfileClicked(Items[i].Id);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            Point p = PointToClient(Cursor.Position);
            int i = IndexAt(p);
            if (i >= 0 && !MoreRect(RowRect(i)).Contains(p)) BeginRename(Items[i].Id);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Items.Count == 0) { base.OnKeyDown(e); return; }
            string id = Items[Math.Min(focusIndex, Items.Count - 1)].Id;
            switch (e.KeyCode)
            {
                case Keys.Up: focusIndex = Math.Max(0, focusIndex - 1); EnsureVisible(Items[focusIndex].Id); e.Handled = true; break;
                case Keys.Down: focusIndex = Math.Min(Items.Count - 1, focusIndex + 1); EnsureVisible(Items[focusIndex].Id); e.Handled = true; break;
                case Keys.Enter: case Keys.Space: if (ProfileClicked != null) ProfileClicked(id); e.Handled = true; break;
                case Keys.F2: BeginRename(id); e.Handled = true; break;
                case Keys.Delete: if (DeleteRequested != null) DeleteRequested(id); e.Handled = true; break;
                case Keys.Apps:
                    {
                        Rectangle r = RowRect(focusIndex);
                        if (MenuRequested != null) MenuRequested(id, PointToScreen(new Point(MoreRect(r).Left, MoreRect(r).Bottom)));
                        e.Handled = true;
                        break;
                    }
            }
            if (e.KeyCode == Keys.F10 && e.Shift)
            {
                Rectangle r = RowRect(focusIndex);
                if (MenuRequested != null) MenuRequested(id, PointToScreen(new Point(MoreRect(r).Left, MoreRect(r).Bottom)));
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnResize(EventArgs e) { ClampScroll(); base.OnResize(e); }

        public void BeginRename(string id)
        {
            CommitEdit();
            int i = -1;
            for (int k = 0; k < Items.Count; k++) if (Items[k].Id == id) i = k;
            if (i < 0) return;
            EnsureVisible(id);
            Rectangle row = RowRect(i);
            editingId = id;
            editor = new TextBox
            {
                Text = Items[i].Name,
                MaxLength = AppProfile.MAX_NAME_LENGTH,
                BorderStyle = BorderStyle.None,
                Font = Ui.BodyBold,
                BackColor = Theme.IsDark ? Theme.Canvas : Color.White,
                ForeColor = Theme.TextPrimary,
                Location = new Point(row.X + Ui.S(40), row.Y + Ui.S(12)),
                Width = row.Width - Ui.S(84),
                AccessibleName = "Profile name"
            };
            editor.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter) { ke.SuppressKeyPress = true; CommitEdit(); Focus(); }
                else if (ke.KeyCode == Keys.Escape) { ke.SuppressKeyPress = true; CancelEdit(); Focus(); }
            };
            editor.LostFocus += delegate { CommitEdit(); };
            Controls.Add(editor);
            editor.Focus();
            editor.SelectAll();
            Invalidate();
        }

        public void CommitEdit()
        {
            if (editor == null) return;
            TextBox ed = editor;
            string id = editingId, text = ed.Text;
            editor = null;
            editingId = null;
            Controls.Remove(ed);
            ed.Dispose();
            if (RenameCommitted != null) RenameCommitted(id, text);
            Invalidate();
        }

        private void CancelEdit()
        {
            if (editor == null) return;
            TextBox ed = editor;
            editor = null;
            editingId = null;
            Controls.Remove(ed);
            ed.Dispose();
            Invalidate();
        }

        private static readonly int[] StageDpi = { 600, 1000, 1200, 1600, 2400, 3200 };

        public static string Describe(AppProfile p)
        {
            string s = p.Brightness + "%";
            if (p.EnableDpi) s += " · " + StageDpi[Math.Max(0, Math.Min(5, p.ActiveDpiStage))] + " DPI";
            int remaps = 0;
            ButtonMap.ButtonInfo[] all = ButtonMap.All;
            for (int i = 0; i < all.Length; i++) if (!all[i].Locked && p.GetBinding(all[i].Id) != all[i].FactoryAction) remaps++;
            if (remaps > 0) s += " · " + remaps + (remaps == 1 ? " remap" : " remaps");
            return s;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle row = RowRect(i);
                if (row.Bottom < 0 || row.Top > Height) continue;
                AppProfile p = Items[i];
                bool active = p.Id == ActiveId;
                AppProfile shown = active && ActiveDraft != null ? ActiveDraft : p;
                bool hov = i == hoverIndex;
                Color fill = active ? Theme.SurfaceElevated : (hov ? (Theme.IsDark ? Color.FromArgb(31, 34, 42) : Color.FromArgb(244, 245, 248)) : BackColor);
                RectangleF rf = new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1, row.Height - 1);
                Ui.FillRound(g, fill, rf, Ui.Sf(8));
                if (active) Ui.DrawRound(g, Theme.Accent, rf, Ui.Sf(8), 1.5f);
                else if (Focused && ShowFocusCues && i == focusIndex) Ui.DrawRound(g, Theme.Accent, rf, Ui.Sf(8), 1f);
                if (active && Focused && ShowFocusCues && i == focusIndex)
                    Ui.DrawRound(g, Theme.TextPrimary, RectangleF.Inflate(rf, -Ui.Sf(3), -Ui.Sf(3)), Ui.Sf(6), 1f);

                Color led = ParseHex(shown.HexColor);
                float d = Ui.Sf(14);
                using (SolidBrush b = new SolidBrush(led)) g.FillEllipse(b, row.X + Ui.Sf(16), row.Y + (row.Height - d) / 2f, d, d);
                if (led.GetBrightness() > 0.85f) using (Pen pn = new Pen(Theme.Border)) g.DrawEllipse(pn, row.X + Ui.Sf(16), row.Y + (row.Height - d) / 2f, d, d);

                int tx = row.X + Ui.S(42);
                int tw = row.Width - Ui.S(42) - Ui.S(44);
                if (p.Id != editingId)
                {
                    string name = p.Name;
                    if (active && ActiveDirty) name += " •";
                    Ui.Text(g, name, Ui.BodyBold, new Rectangle(tx, row.Y + Ui.S(10), tw, Ui.S(20)), Theme.TextPrimary, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                }
                string sub = active ? (ActiveDirty ? "Active · unsaved changes" : "Active · " + Describe(shown)) : Describe(p);
                Ui.Text(g, sub, Ui.Small, new Rectangle(tx, row.Y + Ui.S(31), tw, Ui.S(18)), active ? (ActiveDirty ? Theme.Warning : Theme.Accent) : Theme.TextSecondary,
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

                if (hov || active || (Focused && i == focusIndex))
                {
                    Rectangle mr = MoreRect(row);
                    if (hov && hoverMore) Ui.FillRound(g, Theme.SurfaceHover, mr, Ui.Sf(6));
                    Ui.Glyph(g, Glyphs.More, Ui.IconSmall, mr, hov && hoverMore ? Theme.TextPrimary : Theme.TextSecondary);
                }
            }

            if (ContentHeight > Height && Height > 0)
            {
                float h = Math.Max(Ui.Sf(24), Height * Height / (float)ContentHeight);
                float y = (Height - h) * scrollY / (float)Math.Max(1, ContentHeight - Height);
                Ui.FillRound(g, Theme.SurfaceHover, new RectangleF(Width - Ui.Sf(6), y, Ui.Sf(3), h), Ui.Sf(1.5f));
            }
        }

        public static Color ParseHex(string hex)
        {
            try
            {
                string c = (hex ?? "").Trim().TrimStart('#');
                if (c.Length == 6) return Color.FromArgb(Convert.ToInt32(c.Substring(0, 2), 16), Convert.ToInt32(c.Substring(2, 2), 16), Convert.ToInt32(c.Substring(4, 2), 16));
            }
            catch { }
            return Color.FromArgb(190, 89, 255);
        }
    }

    // ─── Left hero: LED-tinted gradient, mouse render, brand, live readout ───
    public class HeroPanel : Control, IBackgroundPainter
    {
        public Bitmap Image;
        public Color Led = Color.FromArgb(190, 89, 255);
        public int Brightness = 100;
        public bool Connected;
        public string ProfileName = "";
        public string Readout = "";
        public string Version = "";
        public Rectangle ImageRect;

        public HeroPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        // Greyscale copy of Image for the disconnected state, built on first use.
        private Bitmap greyImage;
        private Bitmap greySource;

        private Bitmap GreyImage()
        {
            if (Image == null) return null;
            if (greyImage != null && greySource == Image) return greyImage;
            if (greyImage != null) greyImage.Dispose();
            greySource = Image;
            greyImage = new Bitmap(Image.Width, Image.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            const float k = 0.8f;
            System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix(new float[][]
            {
                new float[] { 0.299f * k, 0.299f * k, 0.299f * k, 0, 0 },
                new float[] { 0.587f * k, 0.587f * k, 0.587f * k, 0, 0 },
                new float[] { 0.114f * k, 0.114f * k, 0.114f * k, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });
            using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
            using (Graphics g = Graphics.FromImage(greyImage))
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(Image, new Rectangle(0, 0, Image.Width, Image.Height), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, ia);
            }
            return greyImage;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && greyImage != null) { greyImage.Dispose(); greyImage = null; greySource = null; }
            base.Dispose(disposing);
        }

        private void PaintScene(Graphics g)
        {
            float bf = Connected ? 0.35f + 0.65f * Math.Max(5, Math.Min(100, Brightness)) / 100f : 0.45f;
            Color led = Connected ? Led : Color.FromArgb(128, 132, 140);
            Color baseC = Color.FromArgb(12, 13, 16);
            Color top = Theme.Blend(baseC, led, 0.50f * bf);
            Color bottom = Theme.Blend(baseC, led, 0.10f * bf);
            using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), top, bottom, LinearGradientMode.Vertical))
                g.FillRectangle(lg, 0, 0, Width, Height);

            if (ImageRect.Width > 0)
            {
                RectangleF glow = RectangleF.Inflate(ImageRect, ImageRect.Width * 0.75f, ImageRect.Height * 0.18f);
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(glow);
                    using (PathGradientBrush pg = new PathGradientBrush(gp))
                    {
                        pg.CenterColor = Color.FromArgb((int)(150 * bf), led);
                        pg.SurroundColors = new Color[] { Color.FromArgb(0, led) };
                        pg.FocusScales = new PointF(0.15f, 0.35f);
                        g.FillPath(pg, gp);
                    }
                }
            }
        }

        public void PaintBackgroundFor(Control child, Graphics g)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(-child.Left, -child.Top);
            PaintScene(g);
            g.Restore(st);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            PaintScene(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            Bitmap img = Connected ? Image : GreyImage();
            if (img != null) g.DrawImageUnscaled(img, ImageRect.X, ImageRect.Y);

            Color white = Color.White, soft = Color.FromArgb(200, 255, 255, 255), faint = Color.FromArgb(150, 255, 255, 255);
            Ui.Text(g, "REDGEAR", Ui.Brand, new Rectangle(Ui.S(24), Ui.S(22), Ui.S(200), Ui.S(16)), soft, TextFormatFlags.SingleLine);
            Ui.Text(g, "A20", Ui.Title, new Rectangle(Ui.S(24), Ui.S(38), Ui.S(200), Ui.S(28)), white, TextFormatFlags.SingleLine);

            Color dot = Connected ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
            using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, Ui.Sf(25), Ui.Sf(76), Ui.Sf(8), Ui.Sf(8));
            Ui.Text(g, Connected ? "Connected" : "Not connected", Ui.Small, new Rectangle(Ui.S(40), Ui.S(70), Ui.S(160), Ui.S(20)), soft,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);

            Ui.Text(g, ProfileName, Ui.HeroName, new Rectangle(Ui.S(16), Ui.S(470), Width - Ui.S(32), Ui.S(28)), white,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            Ui.Text(g, Readout, Ui.Small, new Rectangle(Ui.S(16), Ui.S(500), Width - Ui.S(32), Ui.S(20)), faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            Ui.Text(g, Version, Ui.Small, new Rectangle(Width - Ui.S(120), Height - Ui.S(52), Ui.S(96), Ui.S(32)), faint,
                TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Main GUI Application Form (Native Windows Forms, Dark Theme)
    // ═════════════════════════════════════════════════════════════════════════
    public class MainForm : Form
    {
        // ─── UI (built when the window is shown, torn down when hidden; null while hidden) ───
        private bool uiBuilt = false;
        private bool rebuildPending = false;
        private bool suppressUiEvents = false;
        private bool systemThemeHooked = false;
        private ToolTip tips;
        private Bitmap heroImage;
        private HeroPanel hero;
        private UiButton btnGitHub, btnSettings, btnRefresh, btnUpdatePill;
        private Panel pnlProfiles, pnlRight, pnlActions, pnlSettings;
        private Panel[] pages;
        private ProfileListView profileList;
        private UiButton btnNewProfile;
        private Label lblPageTitle, lblPageSubtitle, lblDirty;
        private UiButton btnPreview, btnSave;
        private UiTabBar tabBar;
        private int currentPage = 0; // 0 Lighting, 1 DPI, 2 Buttons, 3 Settings
        private int lastTabPage = 0;
        private string statusText;

        // Lighting
        private SwatchRow swatches;
        private TextBox txtHex;
        private UiSlider sliderBrightness;
        private Label lblBrightnessVal;
        // DPI
        private UiToggle tglDpi;
        private StageTile[] stageTiles;
        private UiButton btnTestDpi;
        // Buttons
        private BindingListView bindingList;
        private UiButton btnButtonDefaults;
        // Settings
        private UiSegmented segTheme;
        private OptionCard rbStealth, rbTrayAgent, rbDisabled;
        private Label lblUpdateStatus;
        private UiButton btnCheckUpdates, btnFactoryReset;
        private RichTextBox rtbLog;

        private static readonly string[] PresetNames = { "Lavender", "Purple", "Cyan", "Ice Blue", "Mint", "Green", "Gold", "Pink", "White" };
        private static readonly Color[] PresetColors = {
            Color.FromArgb(190, 89, 255), Color.FromArgb(180, 0, 255), Color.FromArgb(0, 255, 255),
            Color.FromArgb(64, 165, 255), Color.FromArgb(50, 255, 150), Color.FromArgb(0, 255, 0),
            Color.FromArgb(255, 180, 0), Color.FromArgb(255, 50, 140), Color.FromArgb(255, 255, 255)
        };
        private static readonly int[] StageDpi = { 600, 1000, 1200, 1600, 2400, 3200 };
        private static readonly Color[] StageColors = {
            Color.FromArgb(59, 130, 246), Color.FromArgb(250, 204, 21), Color.FromArgb(34, 197, 94),
            Color.FromArgb(168, 85, 247), Color.FromArgb(34, 211, 238), Color.FromArgb(229, 231, 235)
        };
        private const string REPO_URL = "https://github.com/" + UpdateManager.REPO;

        // Diagnostic log: bounded in-memory ring, mirrored into the Settings terminal when it exists.
        private struct LogLine { public string Text; public Color Color; }
        private readonly Queue<LogLine> logLines = new Queue<LogLine>();
        private const int LOG_CAPACITY = 300;

        // Updates (stable channel via GitHub Releases)
        private System.Windows.Forms.Timer updateTimer;
        private UpdateInfo pendingUpdate;
        private bool updatePillHidden = false;
        private string updateProgressText;
        private Version balloonNotifiedVersion;
        private bool updateBalloonActive = false;
        private bool isCheckingUpdate = false;
        private bool isUpdating = false;
        private bool justUpdated = false;
        private const int UPDATE_RECHECK_MS = 24 * 60 * 60 * 1000;

        // System Tray & Background Watcher
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private System.Windows.Forms.Timer usbDebounceTimer;

        // Cross-process "show control center" / "exit" signals (named events)
        private EventWaitHandle showSignalEvent;
        private RegisteredWaitHandle showSignalWait;
        private EventWaitHandle exitSignalEvent;
        private RegisteredWaitHandle exitSignalWait;
        public const string EXIT_EVENT_NAME = @"Local\RedgearA20_Exit_Event";
        public const string SINGLE_INSTANCE_MUTEX = "RedgearA20ControlCenter_SingleInstance";

        // Internal State
        // volatile: written from background stealth-startup Task.Run and hotplug retries,
        // read from the UI thread (Win32 message pump + async continuations).
        private volatile string cachedDevicePath = null;
        private volatile int cachedUsagePage = 0;
        private volatile bool isSavingInProgress = false;
        private AppProfile currentProfile; // saved active profile (what the watcher applies)
        private AppProfile draft;          // editor copy of the active profile
        private ProfileLibrary library;
        private bool mouseShowsDraft = false;
        private bool startHidden = false;
        private bool isStealth = false;
        private bool isExplicitlyRestoring = false;
        private bool balloonShownOnce = false;
        private volatile bool isExiting = false;
        private volatile bool forcedExit = false;
        private bool isHotplugHandling = false;
        private volatile bool pendingShowSignal = false;

        private const int EEPROM_WRITE_DELAY_MS = 100;

        // Win32 constants & P/Invoke
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVNODES_CHANGED = 0x0007;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        private const string RUN_REG_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_REG_NAME = "RedgearA20ControlCenter";

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(int message, uint dwFlag);
        private const uint MSGFLT_ADD = 1;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        internal static void ApplyDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    int useDarkMode = Theme.IsDark ? 1 : 0;
                    if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                    }
                }
            }
            catch { }
        }

        // Registered message kept for backwards compatibility with older builds.
        private static int wmShowControlCenter = 0;
        private static Mutex appMutex = null;
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDarkTitleBar(this.Handle);

            // A second instance may signal before this form has a window handle.
            // Remember it and replay the request as soon as the handle exists.
            if (pendingShowSignal)
            {
                pendingShowSignal = false;
                try { BeginInvoke(new Action(RestoreGui)); } catch { }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Windows shutdown / Task Manager must never be blocked by our
            // "keep running in background" behavior.
            bool systemClose = e.CloseReason == CloseReason.WindowsShutDown ||
                               e.CloseReason == CloseReason.TaskManagerClosing ||
                               forcedExit;
            bool exitAuthorized = isExiting ||
                                  e.CloseReason == CloseReason.ApplicationExitCall ||
                                  systemClose;

            if (isSavingInProgress && !systemClose)
            {
                MessageBox.Show("Please wait until the operation completes before exiting.", "Busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                // Do not leave a stale exit authorization behind: the next [X]
                // must still minimize to the watcher as usual.
                isExiting = false;
                return;
            }

            if (!systemClose && !isUpdating && uiBuilt && this.Visible)
            {
                if (profileList != null) profileList.CommitEdit();
                CommitHexEdit(false);
                if (IsDirty() && !ResolveDirtyOnClose(e)) return;
            }

            if (!exitAuthorized && currentProfile != null && (currentProfile.Mode == PersistenceMode.StealthWatcher || currentProfile.Mode == PersistenceMode.TrayAgent))
            {
                e.Cancel = true;
                MinimizeToWatcher(showBalloon: currentProfile.Mode == PersistenceMode.TrayAgent);
                return;
            }

            if (trayMenu != null)
            {
                trayMenu.Dispose();
                trayMenu = null;
            }

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }

            base.OnFormClosing(e);
        }

        // Returns true when closing may continue right away; otherwise the close is cancelled
        // and (for Save / revert) re-issued once the async work has finished.
        private bool ResolveDirtyOnClose(FormClosingEventArgs e)
        {
            DialogResult dr = MessageBox.Show(this,
                string.Format("Save the changes to \"{0}\"?", currentProfile.Name),
                "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            bool wasExit = isExiting || e.CloseReason == CloseReason.ApplicationExitCall;
            if (dr == DialogResult.Cancel)
            {
                e.Cancel = true;
                isExiting = false;
                return false;
            }

            Func<Task<bool>> work;
            if (dr == DialogResult.Yes)
            {
                work = SaveDraftAsync;
            }
            else
            {
                draft = currentProfile.Clone();
                statusText = null;
                LoadDraftIntoUi();
                if (!mouseShowsDraft || string.IsNullOrEmpty(cachedDevicePath))
                {
                    mouseShowsDraft = false;
                    return true;
                }
                // The mouse still shows the discarded preview: put the saved profile back first.
                mouseShowsDraft = false;
                work = delegate { return ApplyProfileAsync(currentProfile, true); };
            }

            e.Cancel = true;
            isExiting = false;
            BeginInvoke(new Action(async delegate
            {
                bool ok = await work();
                if (dr == DialogResult.Yes && !ok) return;
                if (wasExit) RequestExit();
                else Close();
            }));
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (trayMenu != null)
                {
                    trayMenu.Dispose();
                    trayMenu = null;
                }
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }
                if (usbDebounceTimer != null)
                {
                    usbDebounceTimer.Stop();
                    usbDebounceTimer.Dispose();
                    usbDebounceTimer = null;
                }
                if (updateTimer != null)
                {
                    updateTimer.Stop();
                    updateTimer.Dispose();
                    updateTimer = null;
                }
                if (showSignalWait != null)
                {
                    try { showSignalWait.Unregister(null); } catch { }
                    showSignalWait = null;
                }
                if (showSignalEvent != null)
                {
                    try { showSignalEvent.Close(); } catch { }
                    showSignalEvent = null;
                }
                if (exitSignalWait != null)
                {
                    try { exitSignalWait.Unregister(null); } catch { }
                    exitSignalWait = null;
                }
                if (exitSignalEvent != null)
                {
                    try { exitSignalEvent.Close(); } catch { }
                    exitSignalEvent = null;
                }
                if (systemThemeHooked)
                {
                    SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                    systemThemeHooked = false;
                }
                if (heroImage != null) { heroImage.Dispose(); heroImage = null; }
                if (tips != null) { tips.Dispose(); tips = null; }
            }
            base.Dispose(disposing);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized && currentProfile != null && currentProfile.Mode == PersistenceMode.TrayAgent)
            {
                MinimizeToWatcher(false);
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !isExplicitlyRestoring)
            {
                value = false;
                if (!this.IsHandleCreated) CreateHandle();
            }
            if (value && !uiBuilt)
            {
                if (!this.IsHandleCreated) CreateHandle(); // per-monitor DPI needs the window handle
                BuildUi();
            }
            base.SetVisibleCore(value);
        }

        private const int WM_DPICHANGED = 0x02E0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                // Rebuild at the new scale, then take the position Windows suggests.
                if (uiBuilt)
                {
                    rebuildPending = true;
                    RebuildUi();
                }
                RECT r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);

            if (m.Msg == WM_DEVICECHANGE)
            {
                int wp = m.WParam.ToInt32();
                if (wp == DBT_DEVNODES_CHANGED || wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEREMOVECOMPLETE)
                {
                    if (usbDebounceTimer != null)
                    {
                        usbDebounceTimer.Stop();
                        usbDebounceTimer.Start();
                    }
                }
            }
            else if (wmShowControlCenter != 0 && m.Msg == wmShowControlCenter)
            {
                RestoreGui();
            }
        }

        private static void TrimMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                // Process.GetCurrentProcess() owns an OS handle; dispose it after use.
                using (Process current = Process.GetCurrentProcess())
                {
                    SetProcessWorkingSetSize(current.Handle, new IntPtr(-1), new IntPtr(-1));
                }
            }
            catch { }
        }

        private static void SetAutoStart(PersistenceMode mode)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RUN_REG_KEY))
                {
                    if (key != null)
                    {
                        if (mode == PersistenceMode.StealthWatcher)
                        {
                            string targetPath;
                            string deployErr;
                            if (!TaskSchedulerHelper.DeployDriverBinary(out targetPath, out deployErr))
                            {
                                targetPath = Application.ExecutablePath;
                            }
                            key.SetValue(APP_REG_NAME, string.Format("\"{0}\" --stealth", targetPath));
                        }
                        else if (mode == PersistenceMode.TrayAgent)
                        {
                            string targetPath;
                            string deployErr;
                            if (!TaskSchedulerHelper.DeployDriverBinary(out targetPath, out deployErr))
                            {
                                targetPath = Application.ExecutablePath;
                            }
                            key.SetValue(APP_REG_NAME, string.Format("\"{0}\" --tray", targetPath));
                        }
                        else
                        {
                            key.DeleteValue(APP_REG_NAME, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Registry auto-start error: " + ex.Message);
            }
        }

        private void InitTrayIcon()
        {
            trayMenu = new ContextMenuStrip();

            ToolStripMenuItem miOpen = new ToolStripMenuItem("Open Control Center");
            miOpen.Font = new Font(trayMenu.Font, FontStyle.Bold);
            miOpen.Click += delegate { RestoreGui(); };
            trayMenu.Items.Add(miOpen);

            ToolStripMenuItem miApply = new ToolStripMenuItem("Apply Profile Now");
            miApply.Click += async delegate { await ApplyProfileAsync(currentProfile, false); };
            trayMenu.Items.Add(miApply);

            ToolStripMenuItem miUpdates = new ToolStripMenuItem("Check for Updates");
            miUpdates.Click += async delegate
            {
                RestoreGui();
                await CheckForUpdatesAsync(interactive: true);
            };
            trayMenu.Items.Add(miUpdates);

            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem("Exit Driver (Stop Persistence)");
            miExit.Click += delegate
            {
                DialogResult dr = MessageBox.Show(
                    "⚠️ WARNING: This will completely stop the background driver!\n\n" +
                    "• Your mouse settings will NOT persist across USB unplugs or PC restarts.\n\n" +
                    "Are you sure you want to exit and stop persistence?",
                    "Exit Driver Confirmation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2
                );
                if (dr == DialogResult.Yes)
                {
                    RequestExit();
                }
            };
            trayMenu.Items.Add(miExit);

            trayIcon = new NotifyIcon
            {
                Icon = this.Icon != null ? this.Icon : SystemIcons.Application,
                Text = "Redgear A20 Control Center",
                ContextMenuStrip = trayMenu,
                Visible = (currentProfile != null && currentProfile.Mode == PersistenceMode.TrayAgent)
            };

            trayIcon.DoubleClick += delegate { RestoreGui(); };
            trayIcon.BalloonTipClicked += delegate
            {
                if (!updateBalloonActive) return;
                EndUpdateBalloon();
                RestoreGui();
            };
            trayIcon.BalloonTipClosed += delegate { if (updateBalloonActive) EndUpdateBalloon(); };
        }

        // ─── UPDATES ───
        private void InitUpdateChecker()
        {
            updateTimer = new System.Windows.Forms.Timer();
            // Background starts wait for sign-in networking to settle; the GUI checks almost at once.
            updateTimer.Interval = startHidden ? 60 * 1000 : 3000;
            updateTimer.Tick += async delegate
            {
                updateTimer.Interval = UPDATE_RECHECK_MS;
                await CheckForUpdatesAsync(interactive: false);
            };
            updateTimer.Start();
        }

        private async Task CheckForUpdatesAsync(bool interactive)
        {
            if (isCheckingUpdate || isUpdating) return;
            isCheckingUpdate = true;
            if (btnCheckUpdates != null) { btnCheckUpdates.Enabled = false; btnCheckUpdates.Text = "Checking..."; }
            try
            {
                UpdateInfo info = await Task.Run(() => UpdateManager.CheckLatest());
                if (info == null)
                {
                    if (interactive)
                    {
                        Log("Update check: you are on the latest stable version.", Theme.TextSecondary);
                        MessageBox.Show(string.Format("You're on the latest stable version ({0}).", UpdateManager.Display(UpdateManager.CurrentVersion)),
                            "No Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                Log(string.Format("Update available: {0} (installed {1}).", UpdateManager.Display(info.Version), UpdateManager.Display(UpdateManager.CurrentVersion)), Theme.Accent);
                ShowUpdateBanner(info);
                if (!this.Visible)
                {
                    NotifyUpdateInBackground(info);
                }
                else if (interactive)
                {
                    await PromptAndInstallUpdateAsync(info);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Update check failed: " + ex.Message);
                if (interactive)
                {
                    Log("Update check failed: " + ex.Message, Theme.Danger);
                    MessageBox.Show("Could not check for updates:\n\n" + ex.Message + "\n\nCheck your internet connection and try again.",
                        "Update Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                isCheckingUpdate = false;
                if (btnCheckUpdates != null && !btnCheckUpdates.IsDisposed) { btnCheckUpdates.Enabled = true; btnCheckUpdates.Text = "Check for Updates"; }
                if (!this.Visible) TrimMemory();
            }
        }

        private void ShowUpdateBanner(UpdateInfo info)
        {
            bool isNew = pendingUpdate == null || !pendingUpdate.Version.Equals(info.Version);
            pendingUpdate = info;
            if (isNew) updatePillHidden = false;
            RefreshUpdatePill();
        }

        private void HideUpdateBanner()
        {
            updatePillHidden = true;
            RefreshUpdatePill();
        }

        // Stealth mode has no tray icon, so it is shown just long enough to carry the notification.
        private void NotifyUpdateInBackground(UpdateInfo info)
        {
            if (trayIcon == null || info.Version.Equals(balloonNotifiedVersion)) return;
            balloonNotifiedVersion = info.Version;
            updateBalloonActive = true;
            trayIcon.Visible = true;
            trayIcon.ShowBalloonTip(15000, "Redgear A20 Control Center",
                string.Format("{0} is available. Click here to update.", UpdateManager.Display(info.Version)), ToolTipIcon.Info);
        }

        private void EndUpdateBalloon()
        {
            updateBalloonActive = false;
            if (trayIcon != null) trayIcon.Visible = currentProfile != null && currentProfile.Mode == PersistenceMode.TrayAgent;
        }

        private async void BtnUpdateNow_Click(object sender, EventArgs e)
        {
            if (pendingUpdate != null) await PromptAndInstallUpdateAsync(pendingUpdate);
        }

        private async Task PromptAndInstallUpdateAsync(UpdateInfo info)
        {
            if (isUpdating) return;
            if (info.DownloadUrl == null)
            {
                UpdateManager.OpenInBrowser(info.PageUrl);
                return;
            }
            if (isSavingInProgress)
            {
                MessageBox.Show("Please wait until the current operation completes, then try again.", "Busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult dr = MessageBox.Show(
                string.Format("Download and install {0} now?\n\nThe Control Center will restart automatically. Your saved profile and settings are kept.",
                    UpdateManager.Display(info.Version)),
                "Update to " + UpdateManager.Display(info.Version),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;

            await InstallUpdateAsync(info);
        }

        private async Task InstallUpdateAsync(UpdateInfo info)
        {
            // isSavingInProgress blocks hardware operations and exit while files are being swapped.
            isUpdating = true;
            isSavingInProgress = true;
            SetUiBusy(true);
            updateProgressText = "Updating…";
            RefreshUpdatePill();
            bool restarting = false;

            try
            {
                Action<string> progress = delegate(string s)
                {
                    try { BeginInvoke(new Action(delegate { updateProgressText = s; RefreshUpdatePill(); Log(s, Theme.TextSecondary); })); } catch { }
                };

                UpdateResult result;
                try
                {
                    result = await Task.Run(() => UpdateManager.Install(info, progress));
                }
                catch (Exception ex)
                {
                    Log("❌ Update failed: " + ex.Message, Theme.Danger);
                    ShowUpdateBanner(info);
                    DialogResult dr = MessageBox.Show(
                        "The update could not be installed:\n\n" + ex.Message +
                        "\n\nYour current version was not changed. Open the download page to update manually?",
                        "Update Failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (dr == DialogResult.Yes) UpdateManager.OpenInBrowser(info.PageUrl);
                    return;
                }

                if (result.Warning != null) Log("⚠️ " + result.Warning, Theme.Warning);
                Log(string.Format("✅ {0} installed. Restarting...", UpdateManager.Display(info.Version)), Theme.Accent);

                string args = "--updated";
                if (!this.Visible && currentProfile != null)
                {
                    if (currentProfile.Mode == PersistenceMode.StealthWatcher) args = "--stealth " + args;
                    else if (currentProfile.Mode == PersistenceMode.TrayAgent) args = "--tray " + args;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(result.RelaunchPath, args)
                    {
                        WorkingDirectory = Path.GetDirectoryName(result.RelaunchPath)
                    });
                }
                catch (Exception ex)
                {
                    Log("Could not restart automatically: " + ex.Message, Theme.Warning);
                    MessageBox.Show(string.Format("{0} was installed, but the app could not restart automatically.\n\nPlease close and reopen the Control Center.", UpdateManager.Display(info.Version)),
                        "Update Installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // The new process waits for this one to release the single-instance lock.
                restarting = true;
                isSavingInProgress = false;
                isExiting = true;
                Application.Exit();
            }
            finally
            {
                isUpdating = false;
                if (!restarting)
                {
                    isSavingInProgress = false;
                    updateProgressText = null;
                    SetUiBusy(false);
                    RefreshUpdatePill();
                }
            }
        }

        private void RequestExit()
        {
            if (isSavingInProgress)
            {
                MessageBox.Show("Please wait until the current operation completes before exiting.", "Busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            isExiting = true;
            Application.Exit();
        }

        private void MinimizeToWatcher(bool showBalloon)
        {
            // Restore Normal state BEFORE hiding. Setting WindowState after Hide()
            // can make WinForms re-show a previously minimized window.
            if (this.WindowState != FormWindowState.Normal)
            {
                this.WindowState = FormWindowState.Normal;
            }
            this.ShowInTaskbar = false;
            this.Hide();
            isExplicitlyRestoring = false;
            if (trayIcon != null)
            {
                trayIcon.Visible = (currentProfile != null && currentProfile.Mode == PersistenceMode.TrayAgent);
                if (trayIcon.Visible && showBalloon && !balloonShownOnce)
                {
                    trayIcon.ShowBalloonTip(3000, "Redgear A20 Control Center", "Running in tray to auto-apply your RGB settings on reconnect.", ToolTipIcon.Info);
                    balloonShownOnce = true;
                }
            }
            // Free the whole UI while hidden (unless there are unsaved edits to keep).
            if (uiBuilt && !IsDirty())
            {
                try { BeginInvoke(new Action(TearDownUi)); } catch { TrimMemory(); }
            }
            else TrimMemory();
        }

        private void RestoreGui()
        {
            isExplicitlyRestoring = true;
            startHidden = false;
            // Normalize state before showing so the window cannot reappear
            // minimized or without a taskbar button.
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Show();
            this.BringToFront();
            this.Activate();
            RefreshDeviceConnection();
            RefreshDraftState();
        }

        private void InitUsbWatcher()
        {
            usbDebounceTimer = new System.Windows.Forms.Timer();
            usbDebounceTimer.Interval = 400; // 400 ms debounce after USB hotplug
            usbDebounceTimer.Tick += OnUsbDebounceTimerTick;
        }

        // ─── Cross-process GUI restore signalling ───
        // Registered window messages (>= WM_USER) cannot be delivered via HWND_BROADCAST
        // on modern Windows. A session-local named event is the deterministic IPC channel.
        private void InitShowSignal()
        {
            try
            {
                showSignalEvent = new EventWaitHandle(false, EventResetMode.AutoReset, TaskSchedulerHelper.SHOW_EVENT_NAME);
                showSignalWait = ThreadPool.RegisterWaitForSingleObject(
                    showSignalEvent,
                    OnShowSignalFired,
                    null,
                    Timeout.Infinite,
                    false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Show-signal init error: " + ex.Message);
            }

            // "RedgearA20.exe --shutdown" (used by Setup and the uninstaller) asks us to quit cleanly.
            try
            {
                exitSignalEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EXIT_EVENT_NAME);
                exitSignalWait = ThreadPool.RegisterWaitForSingleObject(exitSignalEvent, OnExitSignalFired, null, Timeout.Infinite, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exit-signal init error: " + ex.Message);
            }
        }

        private void OnExitSignalFired(object state, bool timedOut)
        {
            if (timedOut) return;
            forcedExit = true;
            isExiting = true;
            try
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;
                this.BeginInvoke(new Action(delegate { Application.Exit(); }));
            }
            catch { }
        }

        // Signals a running instance to exit and waits (up to ~5 s) for it to release its lock.
        public static int SignalShutdown()
        {
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(EXIT_EVENT_NAME, out ev))
                {
                    using (ev) ev.Set();
                }
                else return 0;
                for (int i = 0; i < 50; i++)
                {
                    Mutex m;
                    if (!Mutex.TryOpenExisting(SINGLE_INSTANCE_MUTEX, out m)) return 0;
                    m.Dispose();
                    Thread.Sleep(100);
                }
                return 1;
            }
            catch { return 1; }
        }

        private void OnShowSignalFired(object state, bool timedOut)
        {
            if (timedOut || isExiting) return;
            try
            {
                if (this.IsDisposed || this.Disposing) return;
                if (!this.IsHandleCreated)
                {
                    pendingShowSignal = true;
                    return;
                }
                this.BeginInvoke(new Action(RestoreGui));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch { }
        }

        private async void OnUsbDebounceTimerTick(object sender, EventArgs e)
        {
            usbDebounceTimer.Stop();
            if (isHotplugHandling) return;
            isHotplugHandling = true;

            try
            {
                RefreshDeviceConnection();

                if (string.IsNullOrEmpty(cachedDevicePath))
                {
                    for (int retry = 0; retry < 3; retry++)
                    {
                        await Task.Delay(300);
                        RefreshDeviceConnection();
                        if (!string.IsNullOrEmpty(cachedDevicePath)) break;
                    }
                }

                if (!string.IsNullOrEmpty(cachedDevicePath) && currentProfile != null &&
                    (currentProfile.Mode == PersistenceMode.StealthWatcher || currentProfile.Mode == PersistenceMode.TrayAgent))
                {
                    if (isSavingInProgress)
                    {
                        // Do not drop the hotplug event because another save is running.
                        usbDebounceTimer.Start();
                    }
                    else
                    {
                        // A replugged mouse is back at factory unless it still shows a preview.
                        if (await ApplyProfileAsync(currentProfile, true, false, !mouseShowsDraft)) mouseShowsDraft = false;
                        if (!this.Visible) TrimMemory();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Hotplug handle error: " + ex.Message);
            }
            finally
            {
                isHotplugHandling = false;
            }
        }

        public MainForm() : this(false, false, false)
        {
        }

        public MainForm(bool hidden, bool stealth, bool updated)
        {
            this.startHidden = hidden;
            this.isStealth = stealth;
            this.justUpdated = updated;
            this.currentProfile = AppProfile.Load();

            InitForm();
            ShortcutEngine.Log = Log;
            InitTrayIcon();
            InitUsbWatcher();
            InitShowSignal();
            InitUpdateChecker();

            RefreshDeviceConnection();

            if (justUpdated)
            {
                Log(string.Format("✅ Updated to {0}.", UpdateManager.Display(UpdateManager.CurrentVersion)), Theme.Accent);
            }

            // After an in-app update the visible GUI restarts; restore the saved profile (and the
            // shortcut listener) the way a background watcher start would.
            if (justUpdated && !startHidden)
            {
                this.Shown += async delegate
                {
                    if (currentProfile != null && currentProfile.Mode != PersistenceMode.Disabled)
                    {
                        await ApplyProfileAsync(currentProfile, true);
                    }
                };
            }

            if (startHidden)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 0; i < 15; i++)
                        {
                            ushort uPage;
                            string devPath = HidTransport.FindTargetDevicePath(out uPage);
                            if (!string.IsNullOrEmpty(devPath))
                            {
                                cachedDevicePath = devPath;
                                cachedUsagePage = uPage;
                                break;
                            }
                            await Task.Delay(200);
                        }

                        if (!string.IsNullOrEmpty(cachedDevicePath) && currentProfile != null &&
                            (currentProfile.Mode == PersistenceMode.StealthWatcher || currentProfile.Mode == PersistenceMode.TrayAgent))
                        {
                            await ApplyProfileAsync(currentProfile, true, false, true);
                        }
                        await Task.Delay(1000);
                        TrimMemory();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Stealth startup error: " + ex.Message);
                    }
                });
            }
        }

        private void InitForm()
        {
            this.Text = "Redgear A20 Control Center " + UpdateManager.Display(UpdateManager.CurrentVersion);
            this.AutoScaleMode = AutoScaleMode.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Theme.Canvas;
            this.ClientSize = new Size(1060, 640);
        }

        // ─── LAZY UI LIFECYCLE ───
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private float GetWindowScale()
        {
            try
            {
                if (IsHandleCreated)
                {
                    uint dpi = GetDpiForWindow(Handle);
                    if (dpi > 0) return dpi / 96f;
                }
            }
            catch { }
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
            }
            catch { return 1f; }
        }

        private static Bitmap LoadHeroImage(int targetHeight)
        {
            try
            {
                using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("RedgearA20.mouse.png"))
                {
                    if (s == null) return null;
                    using (Image src = Image.FromStream(s))
                    {
                        int h = targetHeight;
                        int w = Math.Max(1, (int)Math.Round(src.Width * (double)h / src.Height));
                        Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.CompositingQuality = CompositingQuality.HighQuality;
                            g.DrawImage(src, new Rectangle(0, 0, w, h));
                        }
                        return bmp;
                    }
                }
            }
            catch { return null; }
        }

        private void BuildUi()
        {
            if (uiBuilt) return;
            uiBuilt = true;
            if (currentProfile == null) currentProfile = new AppProfile();
            if (library == null) library = ProfileLibrary.Load(currentProfile);
            if (draft == null) { draft = currentProfile.Clone(); mouseShowsDraft = false; }

            Theme.Apply(Theme.ResolveDark(currentProfile.ThemePref));
            Ui.Init(GetWindowScale());

            SuspendLayout();
            BackColor = Theme.Canvas;
            ForeColor = Theme.TextPrimary;
            Font = Ui.Body;
            ClientSize = new Size(Ui.S(1060), Ui.S(640));
            tips = new ToolTip();

            BuildHero();
            BuildProfilesColumn();
            BuildRightColumn();
            ResumeLayout(false);

            if (IsHandleCreated) ApplyDarkTitleBar(Handle);
            if (!systemThemeHooked)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                systemThemeHooked = true;
            }

            LoadDraftIntoUi();
            RefreshUpdatePill();
            ShowPage(currentPage);
        }

        private void DisposeUiControls()
        {
            uiBuilt = false;
            if (systemThemeHooked)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                systemThemeHooked = false;
            }
            SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (Control c in old) c.Dispose();
            ResumeLayout(false);
            if (tips != null) { tips.Dispose(); tips = null; }
            if (heroImage != null) { heroImage.Dispose(); heroImage = null; }

            hero = null; btnGitHub = btnSettings = btnRefresh = btnUpdatePill = null;
            pnlProfiles = pnlRight = pnlActions = pnlSettings = null; pages = null;
            profileList = null; btnNewProfile = null;
            lblPageTitle = lblPageSubtitle = lblDirty = null;
            btnPreview = btnSave = null; tabBar = null;
            swatches = null; txtHex = null; sliderBrightness = null; lblBrightnessVal = null;
            tglDpi = null; stageTiles = null; btnTestDpi = null;
            bindingList = null; btnButtonDefaults = null;
            segTheme = null; rbStealth = rbTrayAgent = rbDisabled = null;
            lblUpdateStatus = null; btnCheckUpdates = btnFactoryReset = null; rtbLog = null;
        }

        private void TearDownUi()
        {
            if (!uiBuilt || this.Visible || IsDirty()) return;
            DisposeUiControls();
            draft = null;
            library = null;
            mouseShowsDraft = false;
            TrimMemory();
        }

        private void RebuildUi()
        {
            rebuildPending = false;
            if (!uiBuilt) return;
            if (profileList != null) profileList.CommitEdit();
            CommitHexEdit(false);
            DisposeUiControls();
            BuildUi();
        }

        private void ScheduleRebuild()
        {
            if (rebuildPending || !uiBuilt) return;
            rebuildPending = true;
            try { BeginInvoke(new Action(RebuildUi)); } catch { rebuildPending = false; }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (currentProfile == null || currentProfile.ThemePref != 0) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (uiBuilt && Theme.ResolveDark(0) != Theme.IsDark) ScheduleRebuild();
                }));
            }
            catch { }
        }

        // ─── UI CONSTRUCTION ───
        private Label MakeLabel(Control parent, string text, Font font, Color color, Rectangle bounds)
        {
            Label l = new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                Bounds = bounds,
                AutoSize = false,
                UseMnemonic = false,
                BackColor = parent.BackColor
            };
            parent.Controls.Add(l);
            return l;
        }

        private Label Caption(Control parent, string text, int x, int y)
        {
            return MakeLabel(parent, text.ToUpperInvariant(), Ui.Caption, Theme.TextMuted, Ui.R(x, y, 416, 18));
        }

        private Label Note(Control parent, string text, int x, int y, int h, Color color)
        {
            return MakeLabel(parent, text, Ui.Small, color, Ui.R(x, y, 416, h));
        }

        private UiButton MakeButton(Control parent, UiButtonKind kind, string text, string glyph, Rectangle bounds, string tip)
        {
            UiButton b = new UiButton(kind, text, glyph) { Bounds = bounds };
            if (!string.IsNullOrEmpty(tip))
            {
                tips.SetToolTip(b, tip);
                if (string.IsNullOrEmpty(text)) b.AccessibleName = tip;
            }
            parent.Controls.Add(b);
            return b;
        }

        private void BuildHero()
        {
            hero = new HeroPanel
            {
                Bounds = Ui.R(0, 0, 316, 640),
                Version = UpdateManager.Display(UpdateManager.CurrentVersion),
                Connected = !string.IsNullOrEmpty(cachedDevicePath)
            };
            heroImage = LoadHeroImage(Ui.S(300));
            hero.Image = heroImage;
            if (heroImage != null)
                hero.ImageRect = new Rectangle((hero.Width - heroImage.Width) / 2, Ui.S(130), heroImage.Width, heroImage.Height);

            btnGitHub = MakeButton(hero, UiButtonKind.HeroIcon, "", null, Ui.R(20, 580, 40, 40), "Open the project on GitHub");
            btnGitHub.IconPath = Ui.GitHubMark();
            btnGitHub.Click += delegate { UpdateManager.OpenInBrowser(REPO_URL); };

            btnSettings = MakeButton(hero, UiButtonKind.HeroIcon, "", Glyphs.Settings, Ui.R(66, 580, 40, 40), "Settings");
            btnSettings.Click += delegate { ShowPage(currentPage == 3 ? lastTabPage : 3); };

            btnRefresh = MakeButton(hero, UiButtonKind.HeroIcon, "", Glyphs.Refresh, Ui.R(112, 580, 40, 40), "Refresh mouse connection");
            btnRefresh.Click += delegate { RefreshDeviceConnection(); };

            btnUpdatePill = MakeButton(hero, UiButtonKind.HeroChip, "", Glyphs.Download, Ui.R(150, 22, 142, 30), "A new version is available");
            btnUpdatePill.Font = Ui.Small;
            btnUpdatePill.Visible = false;
            btnUpdatePill.Click += delegate { ShowUpdateMenu(); };

            Controls.Add(hero);
        }

        private void BuildProfilesColumn()
        {
            pnlProfiles = new Panel { Bounds = Ui.R(316, 0, 264, 640), BackColor = Theme.Surface };
            pnlProfiles.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, pnlProfiles.Width - 1, 0, pnlProfiles.Width - 1, pnlProfiles.Height);
            };

            MakeLabel(pnlProfiles, "Profiles", Ui.Title, Theme.TextPrimary, Ui.R(24, 22, 170, 30));
            MakeLabel(pnlProfiles, "Click a profile to apply it", Ui.Small, Theme.TextSecondary, Ui.R(24, 54, 190, 20));

            btnNewProfile = MakeButton(pnlProfiles, UiButtonKind.Ghost, "", Glyphs.Add, Ui.R(212, 26, 36, 36), "New profile from the current settings");
            btnNewProfile.Click += delegate { NewProfile(); };

            profileList = new ProfileListView { Bounds = Ui.R(0, 88, 263, 540), BackColor = Theme.Surface };
            profileList.ProfileClicked += ActivateProfile;
            profileList.MenuRequested += ShowProfileMenu;
            profileList.RenameCommitted += RenameProfile;
            profileList.DeleteRequested += DeleteProfile;
            pnlProfiles.Controls.Add(profileList);

            Controls.Add(pnlProfiles);
        }

        private void BuildRightColumn()
        {
            pnlRight = new Panel { Bounds = Ui.R(580, 0, 480, 640), BackColor = Theme.Canvas };

            lblPageTitle = MakeLabel(pnlRight, "", Ui.Title, Theme.TextPrimary, Ui.R(32, 22, 416, 30));
            lblPageSubtitle = MakeLabel(pnlRight, "", Ui.Subtitle, Theme.TextSecondary, Ui.R(32, 54, 416, 20));

            pages = new Panel[3];
            for (int i = 0; i < 3; i++)
            {
                pages[i] = new Panel { Bounds = Ui.R(0, 84, 480, 428), BackColor = Theme.Canvas, Visible = false };
                pnlRight.Controls.Add(pages[i]);
            }
            BuildLightingPage(pages[0]);
            BuildDpiPage(pages[1]);
            BuildButtonsPage(pages[2]);

            pnlSettings = new Panel { Bounds = Ui.R(0, 84, 480, 492), BackColor = Theme.Canvas, AutoScroll = true, Visible = false };
            BuildSettingsPage(pnlSettings);
            pnlRight.Controls.Add(pnlSettings);

            pnlActions = new Panel { Bounds = Ui.R(0, 512, 480, 64), BackColor = Theme.Canvas };
            lblDirty = MakeLabel(pnlActions, "", Ui.Small, Theme.Warning, Ui.R(32, 0, 160, 64));
            lblDirty.TextAlign = ContentAlignment.MiddleLeft;
            lblDirty.AutoEllipsis = true;
            btnPreview = MakeButton(pnlActions, UiButtonKind.Secondary, "Preview", Glyphs.Play, Ui.R(196, 14, 110, 36), "Send these settings to the mouse's RAM without saving");
            btnPreview.Click += BtnPreview_Click;
            btnSave = MakeButton(pnlActions, UiButtonKind.Primary, "Save Profile", Glyphs.Save, Ui.R(316, 14, 132, 36), "Save this profile and apply it (Ctrl+S)");
            btnSave.Click += BtnSave_Click;
            pnlRight.Controls.Add(pnlActions);

            tabBar = new UiTabBar(new string[] { "Lighting", "DPI", "Buttons" }, new string[] { Glyphs.Lighting, Glyphs.Dpi, Glyphs.Buttons })
            {
                Bounds = Ui.R(0, 576, 480, 64),
                AccessibleName = "Settings sections"
            };
            tabBar.SelectedIndexChanged += delegate { ShowPage(tabBar.SelectedIndex); };
            pnlRight.Controls.Add(tabBar);

            Controls.Add(pnlRight);
        }

        private void BuildLightingPage(Panel p)
        {
            Caption(p, "Colour", 32, 8);
            swatches = new SwatchRow { Bounds = Ui.R(28, 30, 424, 48), Colors = PresetColors, Names = PresetNames, AccessibleName = "Colour presets" };
            swatches.ColorPicked += delegate { if (!suppressUiEvents) SetDraftColor(swatches.SelectedColor); };
            swatches.CustomRequested += delegate { PickCustomColor(); };
            p.Controls.Add(swatches);

            RoundPanel hexBox = new RoundPanel { Bounds = Ui.R(32, 92, 132, 36), Fill = Theme.SurfaceElevated, Stroke = Theme.Border, BackColor = Theme.SurfaceElevated };
            txtHex = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Ui.Body,
                BackColor = Theme.SurfaceElevated,
                ForeColor = Theme.TextPrimary,
                MaxLength = 7,
                TextAlign = HorizontalAlignment.Center,
                AccessibleName = "Hex colour code"
            };
            txtHex.Bounds = new Rectangle(Ui.S(10), (Ui.S(36) - txtHex.PreferredHeight) / 2, Ui.S(112), txtHex.PreferredHeight);
            txtHex.Leave += delegate { CommitHexEdit(true); };
            txtHex.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitHexEdit(true); }
            };
            hexBox.Controls.Add(txtHex);
            p.Controls.Add(hexBox);

            UiButton btnPick = MakeButton(p, UiButtonKind.Secondary, "Custom colour", Glyphs.Color, Ui.R(176, 92, 150, 36), "Open the colour picker");
            btnPick.Click += delegate { PickCustomColor(); };

            Caption(p, "Brightness", 32, 156);
            lblBrightnessVal = MakeLabel(p, "", Ui.BodyBold, Theme.Accent, Ui.R(348, 152, 100, 22));
            lblBrightnessVal.TextAlign = ContentAlignment.MiddleRight;
            lblBrightnessVal.BringToFront();
            sliderBrightness = new UiSlider { Bounds = Ui.R(22, 178, 436, 36), Minimum = 5, Maximum = 100, AccessibleName = "Brightness" };
            sliderBrightness.ValueChanged += delegate
            {
                lblBrightnessVal.Text = sliderBrightness.Value + "%";
                if (suppressUiEvents) return;
                draft.Brightness = sliderBrightness.Value;
                OnDraftChanged();
            };
            p.Controls.Add(sliderBrightness);

            Note(p, "Colour and brightness apply to all six DPI stages as a static light. Preview sends them to the mouse's RAM; Save Profile also restores them on replug and reboot.",
                32, 232, 60, Theme.TextSecondary);
        }

        private void BuildDpiPage(Panel p)
        {
            tglDpi = new UiToggle("Send a DPI stage with this profile") { Bounds = Ui.R(32, 6, 416, 32) };
            tglDpi.CheckedChanged += delegate
            {
                UpdateDpiEnabledState();
                if (suppressUiEvents) return;
                draft.EnableDpi = tglDpi.Checked;
                OnDraftChanged();
            };
            p.Controls.Add(tglDpi);
            Note(p, "Experimental: Cmd 0x09 is not verified on every firmware revision.", 32, 42, 20, Theme.TextSecondary);

            Caption(p, "Active stage", 32, 82);
            stageTiles = new StageTile[6];
            for (int i = 0; i < 6; i++)
            {
                int stage = i;
                StageTile t = new StageTile(StageDpi[i], StageColors[i])
                {
                    Bounds = Ui.R(32 + (i % 3) * 144, 106 + (i / 3) * 88, 128, 72),
                    AccessibleName = "Stage " + i + ", " + StageDpi[i] + " DPI"
                };
                t.CheckedChanged += delegate
                {
                    if (suppressUiEvents || !t.Checked) return;
                    draft.ActiveDpiStage = stage;
                    OnDraftChanged();
                };
                stageTiles[i] = t;
                p.Controls.Add(t);
            }

            btnTestDpi = MakeButton(p, UiButtonKind.Warning, "Test stage (RAM only)", Glyphs.Play, Ui.R(32, 284, 200, 36), "Send only the selected DPI stage to the mouse");
            btnTestDpi.Click += BtnTestDpi_Click;
            Note(p, "Testing changes the mouse's RAM only. Unplugging the mouse restores its previous stage.", 32, 330, 40, Theme.Warning);
        }

        private void BuildButtonsPage(Panel p)
        {
            Note(p, "Side buttons, the wheel click and the DPI buttons can be remapped. Left and right click are locked so the mouse always stays usable.",
                32, 4, 36, Theme.TextSecondary);
            bindingList = new BindingListView { Bounds = Ui.R(32, 46, 416, 266), AccessibleName = "Button bindings · click a button to change its action" };
            bindingList.RowActivated += ShowBindingMenu;
            p.Controls.Add(bindingList);
            btnButtonDefaults = MakeButton(p, UiButtonKind.Secondary, "Restore defaults", Glyphs.Undo, Ui.R(32, 324, 180, 36), "Set every button back to its factory action");
            btnButtonDefaults.Click += delegate { RestoreDefaultBindings(); };
            Note(p, "Click a button to change its action, then Preview or Save Profile. Keyboard shortcuts work only while the app runs (Stealth watcher or Tray agent).",
                32, 368, 36, Theme.TextSecondary);
            Note(p, "Experimental (Cmd 0x10) · replug the mouse to reset its buttons.", 32, 406, 20, Theme.Warning);
        }

        private void BuildSettingsPage(Panel p)
        {
            p.AutoScrollMargin = new Size(0, Ui.S(24));

            Caption(p, "Theme", 32, 8);
            segTheme = new UiSegmented { Bounds = Ui.R(32, 32, 416, 40), AccessibleName = "Theme" };
            segTheme.Items = new string[] { "Dark", "Light", "System" };
            segTheme.SelectedIndexChanged += delegate
            {
                if (suppressUiEvents) return;
                int[] prefs = { 1, 2, 0 };
                SetThemePref(prefs[Math.Max(0, Math.Min(2, segTheme.SelectedIndex))]);
            };
            p.Controls.Add(segTheme);

            Caption(p, "Background mode", 32, 96);
            rbStealth = new OptionCard("Stealth watcher", "Invisible, no tray icon (~1.8 MB RAM). Re-applies your active profile on reboot, wake and replug.", "Recommended")
            { Bounds = Ui.R(32, 120, 416, 76) };
            rbTrayAgent = new OptionCard("Tray agent", "The same auto-apply, with a tray icon for quick access (~2.7 MB RAM).", null)
            { Bounds = Ui.R(32, 204, 416, 76) };
            rbDisabled = new OptionCard("Off", "Settings live in the mouse's RAM only and reset when it is unplugged or the PC restarts.", null)
            { Bounds = Ui.R(32, 288, 416, 76) };
            OptionCard[] cards = { rbStealth, rbTrayAgent, rbDisabled };
            PersistenceMode[] modes = { PersistenceMode.StealthWatcher, PersistenceMode.TrayAgent, PersistenceMode.Disabled };
            for (int i = 0; i < cards.Length; i++)
            {
                OptionCard c = cards[i];
                PersistenceMode m = modes[i];
                c.CheckedChanged += delegate { if (!suppressUiEvents && c.Checked) SetMode(m); };
                p.Controls.Add(c);
            }

            Caption(p, "Updates", 32, 388);
            lblUpdateStatus = MakeLabel(p, "", Ui.Body, Theme.TextSecondary, Ui.R(32, 410, 260, 40));
            lblUpdateStatus.TextAlign = ContentAlignment.MiddleLeft;
            btnCheckUpdates = MakeButton(p, UiButtonKind.Secondary, "Check for Updates", Glyphs.Refresh, Ui.R(288, 412, 160, 36), "Check GitHub for a newer stable version");
            btnCheckUpdates.Click += async delegate { await CheckForUpdatesAsync(interactive: true); };

            Caption(p, "Diagnostics · USB packet log", 32, 472);
            RoundPanel term = new RoundPanel { Bounds = Ui.R(32, 496, 416, 200), Fill = Theme.TerminalBg, Stroke = Theme.Border, BackColor = Theme.TerminalBg };
            rtbLog = new RichTextBox
            {
                Bounds = Ui.R(10, 10, 396, 180),
                ReadOnly = true,
                BackColor = Theme.TerminalBg,
                ForeColor = Theme.TerminalText,
                Font = Ui.Mono,
                BorderStyle = BorderStyle.None,
                DetectUrls = false,
                AccessibleName = "Diagnostic USB packet log"
            };
            term.Controls.Add(rtbLog);
            p.Controls.Add(term);
            UiButton btnCopyLog = MakeButton(p, UiButtonKind.Secondary, "Copy", Glyphs.Copy, Ui.R(32, 706, 100, 32), "Copy the log to the clipboard");
            btnCopyLog.Click += delegate
            {
                if (rtbLog == null || rtbLog.TextLength == 0) return;
                try { Clipboard.SetText(rtbLog.Text); SetStatus("Log copied"); }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not copy the log to the clipboard:\n\n" + ex.Message, "Clipboard Busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            UiButton btnClearLog = MakeButton(p, UiButtonKind.Ghost, "Clear", Glyphs.Delete, Ui.R(140, 706, 92, 32), "Clear the log");
            btnClearLog.Click += delegate
            {
                lock (logLines) logLines.Clear();
                if (rtbLog != null) rtbLog.Clear();
            };

            Caption(p, "Device", 32, 762);
            btnFactoryReset = MakeButton(p, UiButtonKind.Danger, "Factory reset mouse", Glyphs.Undo, Ui.R(32, 786, 200, 36), "Restore the stock breathing colours on the mouse");
            btnFactoryReset.Click += BtnReset_Click;
            UiButton btnExitDriver = MakeButton(p, UiButtonKind.Secondary, "Exit driver", Glyphs.Power, Ui.R(244, 786, 140, 36), "Stop the background driver completely");
            btnExitDriver.Click += delegate { ConfirmExitDriver(); };
            Note(p, "Factory reset keeps your saved profiles. Exit driver stops auto-apply until the Control Center is opened again; closing the window with [X] keeps it running.",
                32, 830, 52, Theme.TextSecondary);

            if (Theme.IsDark)
            {
                try { SetWindowTheme(p.Handle, "DarkMode_Explorer", null); } catch { }
                try { SetWindowTheme(rtbLog.Handle, "DarkMode_Explorer", null); } catch { }
            }
        }

        // ─── PAGES ───
        private static readonly string[] PageTitles = { "Lighting", "DPI", "Buttons", "Settings" };
        private static readonly string[] PageSubtitles = {
            "Static LED colour and brightness",
            "Sensor stage sent with the profile",
            "Hardware button remapping",
            "Appearance, background mode and updates"
        };

        private void ShowPage(int page)
        {
            if (!uiBuilt) return;
            page = Math.Max(0, Math.Min(3, page));
            if (page != 3) lastTabPage = page;
            bool changed = page != currentPage;
            currentPage = page;
            SuspendLayout();
            for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == page;
            pnlSettings.Visible = page == 3;
            pnlActions.Visible = page != 3;
            ResumeLayout(false);
            tabBar.SelectedIndex = page == 3 ? -1 : page;
            btnSettings.Active = page == 3;
            btnSettings.Invalidate();
            lblPageTitle.Text = PageTitles[page];
            lblPageSubtitle.Text = PageSubtitles[page];
            if (page == 3)
            {
                FillLog();
                if (changed) pnlSettings.AutoScrollPosition = Point.Empty;
            }
        }

        // ─── DRAFT <-> UI ───
        private bool IsDirty()
        {
            return draft != null && currentProfile != null && !draft.SameSettings(currentProfile);
        }

        private void LoadDraftIntoUi()
        {
            if (!uiBuilt || draft == null) return;
            suppressUiEvents = true;
            try
            {
                Color c = ParseColor(draft.HexColor);
                swatches.SelectedColor = c;
                txtHex.Text = FormatHexColor(c);
                sliderBrightness.Value = Math.Max(5, Math.Min(100, draft.Brightness));
                lblBrightnessVal.Text = sliderBrightness.Value + "%";
                tglDpi.Checked = draft.EnableDpi;
                int stage = Math.Max(0, Math.Min(5, draft.ActiveDpiStage));
                for (int i = 0; i < stageTiles.Length; i++) stageTiles[i].Checked = i == stage;
                UpdateDpiEnabledState();
                bindingList.Profile = draft;
                int pref = currentProfile.ThemePref;
                segTheme.SelectedIndex = pref == 1 ? 0 : (pref == 2 ? 1 : 2);
                rbStealth.Checked = currentProfile.Mode == PersistenceMode.StealthWatcher;
                rbTrayAgent.Checked = currentProfile.Mode == PersistenceMode.TrayAgent;
                rbDisabled.Checked = currentProfile.Mode == PersistenceMode.Disabled;
            }
            finally
            {
                suppressUiEvents = false;
            }
            RefreshDraftState();
        }

        private void UpdateDpiEnabledState()
        {
            if (tglDpi == null) return;
            bool on = tglDpi.Checked;
            for (int i = 0; i < stageTiles.Length; i++) stageTiles[i].Enabled = on;
            btnTestDpi.Enabled = on && !isSavingInProgress;
        }

        private void RefreshDraftState()
        {
            if (!uiBuilt || draft == null) return;
            bool dirty = IsDirty();
            hero.Led = ParseColor(draft.HexColor);
            hero.Brightness = draft.Brightness;
            hero.ProfileName = currentProfile.Name;
            hero.Readout = FormatHexColor(hero.Led) + " · " + ProfileListView.Describe(draft);
            hero.Connected = !string.IsNullOrEmpty(cachedDevicePath);
            hero.Invalidate(true);

            profileList.Items = library.Items;
            profileList.ActiveId = currentProfile.Id;
            profileList.ActiveDirty = dirty;
            profileList.ActiveDraft = draft;
            profileList.RefreshItems();

            if (dirty)
            {
                lblDirty.Text = mouseShowsDraft ? "● Previewing · not saved" : "● Unsaved changes";
                lblDirty.ForeColor = Theme.Warning;
            }
            else
            {
                lblDirty.Text = statusText ?? "";
                lblDirty.ForeColor = Theme.Accent;
            }
            bindingList.Invalidate();
            btnButtonDefaults.Enabled = !isSavingInProgress && draft.HasCustomBindings();
        }

        private void OnDraftChanged()
        {
            statusText = null;
            RefreshDraftState();
        }

        private void SetStatus(string text)
        {
            statusText = text;
            RefreshDraftState();
        }

        private void SetUiBusy(bool busy)
        {
            if (!uiBuilt) return;
            btnPreview.Enabled = btnSave.Enabled = btnFactoryReset.Enabled = bindingList.Enabled = !busy;
            btnButtonDefaults.Enabled = !busy && draft != null && draft.HasCustomBindings();
            btnTestDpi.Enabled = !busy && tglDpi.Checked;
            UseWaitCursor = busy;
        }

        private void SetConnected(bool connected)
        {
            if (hero == null) return;
            hero.Connected = connected;
            hero.Invalidate(true);
        }

        private static string FormatHexColor(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        private void SetDraftColor(Color c)
        {
            if (draft == null) return;
            draft.HexColor = FormatHexColor(c);
            if (uiBuilt)
            {
                suppressUiEvents = true;
                try
                {
                    swatches.SelectedColor = c;
                    txtHex.Text = draft.HexColor;
                }
                finally { suppressUiEvents = false; }
            }
            OnDraftChanged();
        }

        private void PickCustomColor()
        {
            if (draft == null) return;
            using (ColorDialog cd = new ColorDialog())
            {
                cd.Color = ParseColor(draft.HexColor);
                cd.FullOpen = true;
                if (cd.ShowDialog(this) == DialogResult.OK) SetDraftColor(cd.Color);
            }
        }

        // Validates the hex box. Returns false (and restores the box) when the text is not #RRGGBB.
        private bool CommitHexEdit(bool showError)
        {
            if (!uiBuilt || txtHex == null || txtHex.IsDisposed || draft == null) return true;
            string raw = txtHex.Text.Trim();
            if (!raw.StartsWith("#")) raw = "#" + raw;
            if (Regex.IsMatch(raw, "^#[0-9A-Fa-f]{6}$"))
            {
                raw = raw.ToUpperInvariant();
                if (!string.Equals(raw, draft.HexColor, StringComparison.OrdinalIgnoreCase)) SetDraftColor(ParseColor(raw));
                else txtHex.Text = raw;
                return true;
            }
            txtHex.Text = FormatHexColor(ParseColor(draft.HexColor));
            if (showError)
                MessageBox.Show(this, "Use the #RRGGBB format, for example #BE59FF.", "Invalid Colour", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // ─── LOG ───
        private void Log(string msg, Color col)
        {
            LogLine line;
            line.Text = string.Format(CultureInfo.InvariantCulture, "[{0:HH:mm:ss.fff}] {1}\n", DateTime.Now, msg);
            line.Color = col;
            lock (logLines)
            {
                logLines.Enqueue(line);
                while (logLines.Count > LOG_CAPACITY) logLines.Dequeue();
            }
            RichTextBox box = rtbLog;
            if (box == null) return;
            try
            {
                if (box.IsHandleCreated && !box.IsDisposed)
                    box.BeginInvoke(new Action(delegate { AppendLogLine(box, line); }));
            }
            catch
            {
                // Logging must never take the app down (e.g. during shutdown).
            }
        }

        private static void AppendLogLine(RichTextBox box, LogLine line)
        {
            if (box.IsDisposed) return;
            Color c = line.Color;
            if (c.GetBrightness() < 0.5f) c = Theme.Blend(c, Color.White, 0.45f); // stay readable on the dark terminal
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = c;
            box.AppendText(line.Text);
            box.ScrollToCaret();
        }

        private void FillLog()
        {
            if (rtbLog == null) return;
            LogLine[] all;
            lock (logLines) all = logLines.ToArray();
            rtbLog.SuspendLayout();
            rtbLog.Clear();
            for (int i = 0; i < all.Length; i++) AppendLogLine(rtbLog, all[i]);
            rtbLog.ResumeLayout();
        }

        private void RefreshDeviceConnection()
        {
            if (this.InvokeRequired)
            {
                try { this.Invoke(new Action(RefreshDeviceConnection)); } catch { }
                return;
            }

            ushort uPage = 0;
            try
            {
                cachedDevicePath = HidTransport.FindTargetDevicePath(out uPage);
                cachedUsagePage = uPage;
            }
            catch (Exception ex)
            {
                // A transient SetupAPI failure must not take the UI down.
                cachedDevicePath = null;
                cachedUsagePage = 0;
                Debug.WriteLine("Device enumeration error: " + ex.Message);
                Log("Device enumeration error: " + ex.Message, Color.FromArgb(255, 80, 80));
            }

            if (cachedDevicePath != null)
            {
                Log(string.Format("Hardware detected: {0} (UsagePage 0x{1:X4})", cachedDevicePath, cachedUsagePage), Theme.Accent);
            }
            else
            {
                Log("Device search completed: Redgear A20 (18F8:0FC0) not currently enumerated.", Theme.Danger);
            }
            SetConnected(cachedDevicePath != null);
            ShortcutEngine.Rebind(cachedDevicePath);
        }

        // ─── PROFILE LIBRARY ACTIONS ───
        private bool SaveDraftToDisk()
        {
            if (draft == null) return true;
            currentProfile.CopySettingsFrom(draft);
            AppProfile entry = library != null ? library.Find(currentProfile.Id) : null;
            string err = null;
            bool ok = true;
            if (library != null)
            {
                if (entry == null)
                {
                    entry = currentProfile.Clone();
                    library.Items.Add(entry);
                }
                else
                {
                    entry.CopySettingsFrom(currentProfile);
                    entry.Name = currentProfile.Name;
                }
                ok = library.Write(entry, out err);
                currentProfile.Id = entry.Id;
            }
            if (ok) ok = currentProfile.Save(out err);
            if (!ok)
            {
                Log("❌ Failed to save profile to disk: " + err, Color.FromArgb(255, 80, 80));
                MessageBox.Show(this, "Failed to save the profile to disk:\n\n" + err, "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            Log(string.Format("Profile \"{0}\" saved to {1}", currentProfile.Name, AppProfile.GetProfilePath()), Color.FromArgb(140, 200, 255));
            return true;
        }

        // Returns false when the user cancels.
        private bool ResolveUnsavedChanges()
        {
            if (!CommitHexEdit(true)) return false;
            if (!IsDirty()) return true;
            DialogResult dr = MessageBox.Show(this,
                string.Format("\"{0}\" has unsaved changes.\n\nSave them before switching?", currentProfile.Name),
                "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (dr == DialogResult.Cancel) return false;
            if (dr == DialogResult.Yes) return SaveDraftToDisk();
            return true;
        }

        private void ActivateProfile(string id)
        {
            if (isSavingInProgress || library == null || currentProfile == null) return;
            if (id == currentProfile.Id) return;
            AppProfile target = library.Find(id);
            if (target == null) return;
            if (!ResolveUnsavedChanges()) return;
            ActivateProfileCore(target);
        }

        private async void ActivateProfileCore(AppProfile target)
        {
            currentProfile.CopySettingsFrom(target);
            currentProfile.Id = target.Id;
            currentProfile.Name = target.Name;
            string err;
            if (!currentProfile.Save(out err)) Log("❌ Failed to save the active profile: " + err, Color.FromArgb(255, 80, 80));
            SetAutoStart(currentProfile.Mode);
            draft = currentProfile.Clone();
            mouseShowsDraft = false;
            statusText = null;
            LoadDraftIntoUi();
            if (profileList != null) profileList.EnsureVisible(target.Id);
            Log(string.Format("Active profile: \"{0}\"", currentProfile.Name), Theme.Accent);

            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                SetStatus("Applies when the mouse is connected");
                return;
            }
            bool ok = await ApplyProfileAsync(currentProfile, false, true);
            if (draft != null && draft.Id == target.Id) SetStatus(ok ? "Applied to mouse ✓" : "Could not apply · see Settings log");
        }

        private async void NewProfile()
        {
            if (isSavingInProgress || library == null) return;
            if (profileList != null) profileList.CommitEdit();
            CommitHexEdit(false);
            AppProfile p = draft.Clone();
            p.Id = ProfileLibrary.NewId();
            p.Name = library.UniqueName("Profile");
            string err;
            library.Add(p, out err);
            if (err != null) Log("❌ Failed to save the new profile: " + err, Color.FromArgb(255, 80, 80));

            currentProfile.CopySettingsFrom(p);
            currentProfile.Id = p.Id;
            currentProfile.Name = p.Name;
            if (!currentProfile.Save(out err)) Log("❌ Failed to save the active profile: " + err, Color.FromArgb(255, 80, 80));
            SetAutoStart(currentProfile.Mode);
            draft = currentProfile.Clone();
            statusText = null;
            LoadDraftIntoUi();
            if (profileList != null) profileList.BeginRename(p.Id);
            Log(string.Format("Created profile \"{0}\"", p.Name), Theme.Accent);

            if (!string.IsNullOrEmpty(cachedDevicePath) && mouseShowsDraft == false)
            {
                if (await ApplyProfileAsync(currentProfile, true)) mouseShowsDraft = false;
            }
            else mouseShowsDraft = false;
        }

        private void DuplicateProfile(string id)
        {
            if (library == null) return;
            AppProfile src = library.Find(id);
            if (src == null) return;
            AppProfile p = src.Clone();
            p.Id = ProfileLibrary.NewId();
            string baseName = AppProfile.SanitizeName(src.Name);
            if (baseName.Length > AppProfile.MAX_NAME_LENGTH - 5) baseName = baseName.Substring(0, AppProfile.MAX_NAME_LENGTH - 5).Trim();
            string name = baseName + " copy";
            bool taken = false;
            for (int i = 0; i < library.Items.Count; i++)
                if (string.Equals(library.Items[i].Name, name, StringComparison.OrdinalIgnoreCase)) taken = true;
            p.Name = taken ? library.UniqueName(name) : name;
            string err;
            library.Add(p, out err);
            if (err != null) Log("❌ Failed to save the duplicate: " + err, Color.FromArgb(255, 80, 80));
            RefreshDraftState();
            if (profileList != null) profileList.EnsureVisible(p.Id);
        }

        private void DeleteProfile(string id)
        {
            if (isSavingInProgress || library == null) return;
            AppProfile p = library.Find(id);
            if (p == null) return;
            if (library.Items.Count <= 1)
            {
                MessageBox.Show(this, "At least one profile is needed. Create another profile before deleting this one.", "Delete Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, string.Format("Delete \"{0}\"? This cannot be undone.", p.Name), "Delete Profile",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            bool wasActive = id == currentProfile.Id;
            int idx = library.IndexOf(id);
            library.Delete(id);
            Log(string.Format("Deleted profile \"{0}\"", p.Name), Theme.TextSecondary);
            if (wasActive)
            {
                AppProfile next = library.Items[Math.Min(idx, library.Items.Count - 1)];
                ActivateProfileCore(next);
            }
            else RefreshDraftState();
        }

        private void RenameProfile(string id, string text)
        {
            if (library == null) return;
            string name = AppProfile.SanitizeName(text);
            AppProfile p = library.Find(id);
            if (p == null || name.Length == 0 || name == p.Name) { RefreshDraftState(); return; }
            p.Name = name;
            string err;
            if (!library.Write(p, out err)) Log("❌ Failed to rename profile: " + err, Color.FromArgb(255, 80, 80));
            if (currentProfile != null && id == currentProfile.Id)
            {
                currentProfile.Name = name;
                if (draft != null) draft.Name = name;
                currentProfile.Save(out err);
            }
            RefreshDraftState();
        }

        private void ShowProfileMenu(string id, Point screen)
        {
            if (library == null) return;
            bool active = currentProfile != null && id == currentProfile.Id;
            ContextMenuStrip m = UiMenuRenderer.Create();
            UiMenuRenderer.Item(m, active ? "Re-apply to mouse" : "Apply", delegate
            {
                if (active) ReapplyActiveProfile();
                else ActivateProfile(id);
            }).Enabled = !isSavingInProgress;
            UiMenuRenderer.Item(m, "Rename", delegate
            {
                BeginInvoke(new Action(delegate { if (profileList != null) profileList.BeginRename(id); }));
            });
            UiMenuRenderer.Item(m, "Duplicate", delegate { DuplicateProfile(id); });
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem del = UiMenuRenderer.Item(m, "Delete", delegate { DeleteProfile(id); });
            del.Tag = "danger";
            del.Enabled = library.Items.Count > 1 && !isSavingInProgress;
            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
            m.Show(screen);
        }

        private async void ReapplyActiveProfile()
        {
            if (isSavingInProgress) return;
            bool ok = await ApplyProfileAsync(currentProfile, false);
            if (ok)
            {
                mouseShowsDraft = false;
                SetStatus("Applied to mouse ✓");
            }
        }

        private void SetMode(PersistenceMode mode)
        {
            if (currentProfile.Mode == mode) return;
            currentProfile.Mode = mode;
            if (draft != null) draft.Mode = mode;
            string err;
            if (!currentProfile.Save(out err)) Log("❌ Failed to save background mode: " + err, Color.FromArgb(255, 80, 80));
            string dummy;
            TaskSchedulerHelper.RemoveTask(out dummy);
            SetAutoStart(mode);
            if (trayIcon != null) trayIcon.Visible = mode == PersistenceMode.TrayAgent && !this.Visible;
            if (mode == PersistenceMode.StealthWatcher)
                Log("🚀 Stealth Driver Watcher auto-start registered (Invisible, No Tray Icon, ~1.8 MB RAM).", Color.FromArgb(80, 230, 130));
            else if (mode == PersistenceMode.TrayAgent)
                Log("📌 Tray Agent startup registered in Windows Run key (~2.7 MB RAM).", Color.FromArgb(140, 200, 255));
            else
                Log("Auto-apply disabled. Mouse will use volatile RAM only.", Color.FromArgb(200, 200, 220));
        }

        private void SetThemePref(int pref)
        {
            if (currentProfile.ThemePref == pref) return;
            currentProfile.ThemePref = pref;
            if (draft != null) draft.ThemePref = pref;
            string err;
            currentProfile.Save(out err);
            if (Theme.ResolveDark(pref) != Theme.IsDark) ScheduleRebuild();
        }

        private void ConfirmExitDriver()
        {
            DialogResult dr = MessageBox.Show(this,
                "This shuts down the Control Center and the background driver.\n\n" +
                "Your mouse settings will NOT be re-applied after a USB replug or restart until you open the Control Center again.\n\n" +
                "To keep auto-apply running, close the window with [X] instead.\n\nExit the driver now?",
                "Exit Driver",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (dr == DialogResult.Yes) RequestExit();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S) && uiBuilt && !isSavingInProgress)
            {
                BtnSave_Click(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ─── UPDATE PILL ───
        private void RefreshUpdatePill()
        {
            if (lblUpdateStatus != null)
            {
                string installed = "Installed " + UpdateManager.Display(UpdateManager.CurrentVersion);
                lblUpdateStatus.Text = pendingUpdate != null
                    ? installed + " · " + UpdateManager.Display(pendingUpdate.Version) + " available"
                    : installed + " · stable channel";
                lblUpdateStatus.ForeColor = pendingUpdate != null ? Theme.Accent : Theme.TextSecondary;
            }
            if (btnUpdatePill == null) return;
            bool show = pendingUpdate != null && (!updatePillHidden || isUpdating);
            if (show)
            {
                string text = isUpdating && updateProgressText != null ? updateProgressText : UpdateManager.Display(pendingUpdate.Version) + " available";
                if (text.Length > 28) text = text.Substring(0, 27) + "…";
                btnUpdatePill.Text = text;
                int w = TextRenderer.MeasureText(text, btnUpdatePill.Font, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(48);
                w = Math.Min(w, hero.Width - Ui.S(130));
                btnUpdatePill.Bounds = new Rectangle(hero.Width - Ui.S(20) - w, Ui.S(22), w, Ui.S(30));
                btnUpdatePill.Enabled = !isUpdating;
            }
            btnUpdatePill.Visible = show;
        }

        private void ShowUpdateMenu()
        {
            if (pendingUpdate == null || isUpdating) return;
            ContextMenuStrip m = UiMenuRenderer.Create();
            UiMenuRenderer.Item(m, pendingUpdate.DownloadUrl != null ? "Update now" : "Open download page", BtnUpdateNow_Click);
            UiMenuRenderer.Item(m, "What's new", delegate
            {
                UpdateManager.OpenInBrowser(pendingUpdate != null ? pendingUpdate.PageUrl : UpdateManager.RELEASES_PAGE);
            });
            m.Items.Add(new ToolStripSeparator());
            UiMenuRenderer.Item(m, "Hide until next start", delegate { HideUpdateBanner(); });
            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
            m.Show(btnUpdatePill, new Point(0, btnUpdatePill.Height + Ui.S(4)));
        }

        private static string TranslateWin32Error(int errCode)
        {
            switch (errCode)
            {
                case 0:
                    return "Operation succeeded.";
                case 2:
                    return "The system cannot find the file specified (ERROR_FILE_NOT_FOUND). The HID device path is stale; reconnect the mouse and click Refresh.";
                case 5:
                    return "Access denied (ERROR_ACCESS_DENIED). Another process (e.g. Gaming Mouse 2.0.exe) may be holding the HID endpoint.";
                case 6:
                    return "Invalid handle (ERROR_INVALID_HANDLE). Device communication handle was closed or invalidated.";
                case 31:
                    return "A device attached to the system is not functioning (ERROR_GEN_FAILURE). Microcontroller may have stalled.";
                case 32:
                    return "The device is being used by another process (ERROR_SHARING_VIOLATION). Close other mouse utilities and retry.";
                case 87:
                    return "The parameter is incorrect (ERROR_INVALID_PARAMETER). USB report buffer length or Report ID mismatch.";
                case 1167:
                    return "The device is not connected (ERROR_DEVICE_NOT_CONNECTED). Mouse was physically disconnected.";
                default:
                    return string.Format("System error code {0}", errCode);
            }
        }

        private static string DescribePipelineFailure(PipelineResult res)
        {
            if (res == null) return "Unknown hardware failure.";
            if (res.Win32ErrorCode != 0) return TranslateWin32Error(res.Win32ErrorCode);
            return string.IsNullOrEmpty(res.ErrorMessage) ? "Hardware operation failed." : res.ErrorMessage;
        }

        // Sends Cmd 0x10 packets with mandatory pacing. Caller must already hold the hardware mutex.
        // Returns null on success, or the failure result.
        private static PipelineResult TransmitButtonBindings(string devicePath, ButtonBinding[] binds, Action<string, Color> logCallback, ref int packetIndex)
        {
            if (binds == null) return null;
            for (int i = 0; i < binds.Length; i++)
            {
                ButtonBinding bind = binds[i];

                // LAYER 3 of the interlock: the dispatcher is the last gate before the wire.
                // A corrupted profile or a future UI regression still cannot rebind a primary
                // click or emit an action code the firmware would collapse to 0x00.
                if (ButtonMap.IsLocked(bind.ButtonId))
                {
                    logCallback(string.Format("Cmd 0x10 BLOCKED: Button 0x{0:X2} is a protected primary click.", bind.ButtonId), Color.FromArgb(255, 120, 120));
                    continue;
                }
                ButtonMap.ButtonInfo info = ButtonMap.Find(bind.ButtonId);
                if (info == null || !ButtonMap.IsKnownAction(bind.Action))
                {
                    logCallback(string.Format("Cmd 0x10 BLOCKED: unverified binding (Button 0x{0:X2} -> 0x{1:X2}) discarded.", bind.ButtonId, (byte)bind.Action), Color.FromArgb(255, 120, 120));
                    continue;
                }

                byte[] bindPacket = new byte[] { HidTransport.REPORT_ID, 0x10, bind.ButtonId, (byte)bind.Action, 0x00, 0x00, 0x00, 0x00 };
                int win32Err;
                string err;
                bool ok = HidTransport.SendSinglePacket(devicePath, bindPacket, out win32Err, out err);
                if (!ok)
                {
                    logCallback(string.Format("Cmd 0x10 [{0} -> {1}]: {2} -> FAIL ({3})", info.DisplayName, ButtonMap.Describe(bind.Action), FormatHex(bindPacket), err), Color.FromArgb(255, 80, 80));
                    return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = win32Err, ErrorMessage = err };
                }

                logCallback(string.Format("Cmd 0x10 [{0} -> {1}]: {2} -> OK", info.DisplayName, ButtonMap.Describe(bind.Action), FormatHex(bindPacket)), Color.FromArgb(255, 200, 80));
                packetIndex++;
                Thread.Sleep(50);
            }
            return null;
        }

        private static PipelineResult ExecuteHardwarePipeline(string devicePath, SaveParameters p, Action<string, Color> logCallback)
        {
            bool lockAcquired = false;
            Mutex hwMutex = null;

            try
            {
                try
                {
                    hwMutex = new Mutex(false, TaskSchedulerHelper.HARDWARE_MUTEX_NAME);
                    try { lockAcquired = hwMutex.WaitOne(3000, false); }
                    catch (AbandonedMutexException) { lockAcquired = true; }
                }
                catch { lockAcquired = false; }

                if (!lockAcquired)
                {
                    return new PipelineResult { Success = false, FailingPacketIndex = -1, Win32ErrorCode = 0, ErrorMessage = "Hardware access lock timeout (Device busy)." };
                }

                int packetIndex = 0;

                logCallback(p.CommitToFlash ?
                    "--- Executing Hardware Pipeline (With Flash Commit 0x20) ---" :
                    "--- Applying Verified Settings to RAM (No EEPROM Write) ---",
                    p.CommitToFlash ? Color.FromArgb(255, 215, 0) : Color.FromArgb(255, 220, 100));

                // 1. Calculate Active-Low PWM values (with defensive bounds clamping)
                int safeBrightness = Math.Max(0, Math.Min(100, p.Brightness));
                float b = safeBrightness / 100.0f;
                int rStep = Math.Max(0, Math.Min(15, (int)Math.Round((p.Color.R / 255.0f) * b * 15.0f)));
                int gStep = Math.Max(0, Math.Min(15, (int)Math.Round((p.Color.G / 255.0f) * b * 15.0f)));
                int bStep = Math.Max(0, Math.Min(15, (int)Math.Round((p.Color.B / 255.0f) * b * 15.0f)));

                byte rInv = (byte)((15 - rStep) & 0x0F);
                byte gInv = (byte)((15 - gStep) & 0x0F);
                byte bInv = (byte)((15 - bStep) & 0x0F);

                logCallback(string.Format("Active-Low PWM Steps: R={0}/15 (0x{1:X}) G={2}/15 (0x{3:X}) B={4}/15 (0x{5:X})",
                    rStep, rInv, gStep, gInv, bStep, bInv), Color.FromArgb(180, 180, 200));

                // 2. Transmit Command 0x14 across all 6 DPI stages
                for (byte stage = 0; stage < 6; stage++)
                {
                    byte b2 = (byte)(((stage & 0x07) << 5) | (gInv & 0x0F));
                    byte b3 = (byte)(((rInv & 0x0F) << 4) | (bInv & 0x0F));
                    byte[] packet = new byte[] { HidTransport.REPORT_ID, 0x14, b2, b3, 0x00, 0x00, 0x00, 0x00 };

                    int win32Err;
                    string err;
                    bool ok = HidTransport.SendSinglePacket(devicePath, packet, out win32Err, out err);
                    if (!ok)
                    {
                        logCallback(string.Format("Cmd 0x14 [Stage {0}]: {1} -> FAIL ({2})", stage, FormatHex(packet), err), Color.FromArgb(255, 80, 80));
                        return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = win32Err, ErrorMessage = err };
                    }

                    logCallback(string.Format("Cmd 0x14 [Stage {0}]: {1} -> OK", stage, FormatHex(packet)), Color.FromArgb(80, 230, 130));
                    packetIndex++;
                    Thread.Sleep(50);
                }

                // 3. Set Static Solid Mode with Command 0x13
                byte[] modePacket = new byte[] { HidTransport.REPORT_ID, 0x13, 0x7F, 0x86, 0x00, 0x00, 0x00, 0x00 };
                {
                    int win32Err;
                    string err;
                    bool ok = HidTransport.SendSinglePacket(devicePath, modePacket, out win32Err, out err);
                    if (!ok)
                    {
                        logCallback(string.Format("Cmd 0x13 [Mode 0x86 Static]: {0} -> FAIL ({1})", FormatHex(modePacket), err), Color.FromArgb(255, 80, 80));
                        return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = win32Err, ErrorMessage = err };
                    }

                    logCallback(string.Format("Cmd 0x13 [Mode 0x86 Static]: {0} -> OK", FormatHex(modePacket)), Color.FromArgb(80, 230, 130));
                    packetIndex++;
                    Thread.Sleep(50);
                }

                // 4. If SendDpi enabled, transmit Command 0x09
                if (p.SendDpi)
                {
                    byte b2 = (byte)(0x40 | (p.ActiveDpiIndex & 0x07));
                    byte[] dpiPacket = new byte[] { HidTransport.REPORT_ID, 0x09, b2, 0x08, p.DpiMask, 0x00, 0x00, 0x00 };
                    int win32Err;
                    string err;
                    bool ok = HidTransport.SendSinglePacket(devicePath, dpiPacket, out win32Err, out err);
                    if (!ok)
                    {
                        logCallback(string.Format("Cmd 0x09 [Stage {0}]: {1} -> FAIL ({2})", p.ActiveDpiIndex, FormatHex(dpiPacket), err), Color.FromArgb(255, 80, 80));
                        return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = win32Err, ErrorMessage = err };
                    }
                    logCallback(string.Format("Cmd 0x09 [Stage {0}]: {1} -> OK", p.ActiveDpiIndex, FormatHex(dpiPacket)), Color.FromArgb(255, 200, 80));
                    packetIndex++;
                    Thread.Sleep(50);
                }

                // 5. If enabled, transmit Command 0x10 button bindings (one packet per remapped button)
                if (p.SendButtonBindings && p.ButtonBindings != null)
                {
                    PipelineResult bindFail = TransmitButtonBindings(devicePath, p.ButtonBindings, logCallback, ref packetIndex);
                    if (bindFail != null) return bindFail;
                }

                // 6. If CommitToFlash, send Command 0x20 sync latch and verify MCU health
                if (p.CommitToFlash)
                {
                    byte[] commitPacket = new byte[] { HidTransport.REPORT_ID, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                    int win32Err;
                    string err;
                    bool ok = HidTransport.SendSinglePacket(devicePath, commitPacket, out win32Err, out err);
                    if (!ok)
                    {
                        logCallback(string.Format("Cmd 0x20 [Sync Latch]: {0} -> FAIL ({1})", FormatHex(commitPacket), err), Color.FromArgb(255, 80, 80));
                        return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = win32Err, ErrorMessage = err };
                    }

                    logCallback(string.Format("Cmd 0x20 [Sync Latch]: {0} -> SENT", FormatHex(commitPacket)), Color.FromArgb(255, 215, 0));
                    packetIndex++;

                    // Wait for latch
                    Thread.Sleep(EEPROM_WRITE_DELAY_MS);

                    // Verify MCU transport health via HidD_GetFeature
                    logCallback("Verifying device transport health via HidD_GetFeature(0x07)...", Color.FromArgb(200, 200, 220));
                    int healthErr;
                    string healthErrMsg;
                    bool healthOk = HidTransport.VerifyDeviceTransportHealth(devicePath, out healthErr, out healthErrMsg);
                    if (!healthOk)
                    {
                        logCallback(string.Format("Device health check failed after sync write: {0}", healthErrMsg), Color.FromArgb(255, 120, 120));
                        return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = healthErr, ErrorMessage = healthErrMsg };
                    }
                    logCallback("Device transport health verified: MCU is responsive.", Color.FromArgb(80, 230, 130));
                }

                return new PipelineResult { Success = true, FailingPacketIndex = -1, Win32ErrorCode = 0, ErrorMessage = null };
            }
            finally
            {
                if (hwMutex != null)
                {
                    if (lockAcquired)
                    {
                        try { hwMutex.ReleaseMutex(); } catch { }
                    }
                    hwMutex.Close();
                }
            }
        }

        private static PipelineResult ExecuteFactoryReset(string devicePath, Action<string, Color> logCallback)
        {
            bool lockAcquired = false;
            Mutex hwMutex = null;

            try
            {
                try
                {
                    hwMutex = new Mutex(false, TaskSchedulerHelper.HARDWARE_MUTEX_NAME);
                    try { lockAcquired = hwMutex.WaitOne(3000, false); }
                    catch (AbandonedMutexException) { lockAcquired = true; }
                }
                catch { lockAcquired = false; }

                if (!lockAcquired)
                {
                    return new PipelineResult { Success = false, FailingPacketIndex = -1, Win32ErrorCode = 0, ErrorMessage = "Hardware access lock timeout (Device busy)." };
                }

                int packetIndex = 0;
                logCallback("--- Restoring Factory Profile (Stock Multi-Stage Colors + Breathing Mode) ---", Color.FromArgb(255, 180, 80));

                // Stock stages (Active-Low PWM):
                // Stage 0: 600 DPI  -> Blue   (gInv=15, rInv=15, bInv=0  -> b2=0x0F, b3=0xF0)
                // Stage 1: 1000 DPI -> Yellow (gInv=0,  rInv=0,  bInv=15 -> b2=0x00, b3=0x0F)
                // Stage 2: 1200 DPI -> Green  (gInv=0,  rInv=15, bInv=15 -> b2=0x00, b3=0xFF)
                // Stage 3: 1600 DPI -> Purple (gInv=15, rInv=0,  bInv=0  -> b2=0x0F, b3=0x00)
                // Stage 4: 2400 DPI -> Cyan   (gInv=0,  rInv=15, bInv=0  -> b2=0x00, b3=0xF0)
                // Stage 5: 3200 DPI -> White  (gInv=0,  rInv=0,  bInv=0  -> b2=0x00, b3=0x00)
                byte[][] stockStages = new byte[][] {
                    new byte[] { 0x0F, 0xF0 }, // Stage 0: Blue
                    new byte[] { 0x00, 0x0F }, // Stage 1: Yellow
                    new byte[] { 0x00, 0xFF }, // Stage 2: Green
                    new byte[] { 0x0F, 0x00 }, // Stage 3: Purple
                    new byte[] { 0x00, 0xF0 }, // Stage 4: Cyan
                    new byte[] { 0x00, 0x00 }  // Stage 5: White
                };

                for (byte stage = 0; stage < 6; stage++)
                {
                    byte b2 = (byte)(((stage & 0x07) << 5) | (stockStages[stage][0] & 0x0F));
                    byte b3 = stockStages[stage][1];
                    byte[] packet = new byte[] { HidTransport.REPORT_ID, 0x14, b2, b3, 0x00, 0x00, 0x00, 0x00 };

                    int win32Err;
                    string err;
                    if (!HidTransport.SendSinglePacket(devicePath, packet, out win32Err, out err))
                    {
                        logCallback(string.Format("Factory Cmd 0x14 [Stage {0}]: {1} -> FAIL ({2})", stage, FormatHex(packet), err), Color.FromArgb(255, 80, 80));
                        return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = win32Err, ErrorMessage = err };
                    }
                    logCallback(string.Format("Factory Cmd 0x14 [Stage {0}]: {1} -> OK", stage, FormatHex(packet)), Color.FromArgb(160, 220, 255));
                    packetIndex++;
                    Thread.Sleep(50);
                }

                // Restore stock mode: Breathing / Spectrum cycle (0x80)
                byte[] modePacket = new byte[] { HidTransport.REPORT_ID, 0x13, 0x7F, 0x80, 0x00, 0x00, 0x00, 0x00 };
                {
                    int win32Err;
                    string err;
                    if (!HidTransport.SendSinglePacket(devicePath, modePacket, out win32Err, out err))
                    {
                        logCallback(string.Format("Factory Cmd 0x13 [Mode 0x80]: {0} -> FAIL ({1})", FormatHex(modePacket), err), Color.FromArgb(255, 80, 80));
                        return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = win32Err, ErrorMessage = err };
                    }
                    logCallback(string.Format("Factory Cmd 0x13 [Mode 0x80]: {0} -> OK", FormatHex(modePacket)), Color.FromArgb(160, 220, 255));
                    packetIndex++;
                    Thread.Sleep(50);
                }

                // Sync latch: 0x20
                byte[] commitPacket = new byte[] { HidTransport.REPORT_ID, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                {
                    int win32Err;
                    string err;
                    if (!HidTransport.SendSinglePacket(devicePath, commitPacket, out win32Err, out err))
                    {
                        logCallback(string.Format("Factory Cmd 0x20 [Sync]: {0} -> FAIL ({1})", FormatHex(commitPacket), err), Color.FromArgb(255, 80, 80));
                        return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = win32Err, ErrorMessage = err };
                    }
                    logCallback(string.Format("Factory Cmd 0x20 [Sync]: {0} -> SENT", FormatHex(commitPacket)), Color.FromArgb(255, 215, 0));
                    packetIndex++;
                    Thread.Sleep(EEPROM_WRITE_DELAY_MS);
                }

                // Health check
                int hErr;
                string hErrMsg;
                if (!HidTransport.VerifyDeviceTransportHealth(devicePath, out hErr, out hErrMsg))
                {
                    logCallback(string.Format("Device health check failed after factory reset: {0}", hErrMsg), Color.FromArgb(255, 120, 120));
                    return new PipelineResult { Success = false, FailingPacketIndex = packetIndex, Win32ErrorCode = hErr, ErrorMessage = hErrMsg };
                }

                logCallback("Device transport health verified: MCU is responsive.", Color.FromArgb(80, 230, 130));
                return new PipelineResult { Success = true, FailingPacketIndex = -1, Win32ErrorCode = 0, ErrorMessage = null };
            }
            finally
            {
                if (hwMutex != null)
                {
                    if (lockAcquired)
                    {
                        try { hwMutex.ReleaseMutex(); } catch { }
                    }
                    hwMutex.Close();
                }
            }
        }

        // ─── SAFE PROFILE EXECUTION: APPLY TO MOUSE ───
        // silent: no UI and no logging (safe from background threads).
        // quietErrors: log failures but don't pop a MessageBox.
        // freshDevice: the mouse is known to be at factory bindings, so only remaps are sent.
        private async Task<bool> ApplyProfileAsync(AppProfile profile, bool silent, bool quietErrors = false, bool freshDevice = false)
        {
            if (isSavingInProgress || profile == null) return false;
            bool showErrors = !silent && !quietErrors;

            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                RefreshDeviceConnection();
                if (string.IsNullOrEmpty(cachedDevicePath))
                {
                    if (showErrors)
                    {
                        MessageBox.Show(this, "Redgear A20 is not connected. Connect it via USB and press Refresh.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return false;
                }
            }

            // Snapshot everything before the first await: the editor may keep changing the draft.
            ButtonBinding[] binds = profile.GetBindingsToTransmit(true, !freshDevice);
            Dictionary<byte, KeyShortcut> shortcuts = profile.GetActiveShortcuts();
            binds = AddStaleShortcutResets(binds, shortcuts);

            SaveParameters p = new SaveParameters
            {
                Color = ParseColor(profile.HexColor),
                Brightness = Math.Max(5, Math.Min(100, profile.Brightness)),
                ActiveDpiIndex = (byte)Math.Max(0, Math.Min(5, profile.ActiveDpiStage)),
                DpiMask = 0x3F,
                CommitToFlash = false, // Volatile RAM - Instant A704 registers
                SendDpi = profile.EnableDpi,
                SendButtonBindings = binds.Length > 0,
                ButtonBindings = binds
            };

            string targetPath = cachedDevicePath;
            bool onUi = !silent && !this.InvokeRequired;
            if (onUi) SetUiBusy(true);
            isSavingInProgress = true;

            try
            {
                Action<string, Color> logger = silent ? (Action<string, Color>)delegate(string s, Color c) { } : new Action<string, Color>(Log);
                PipelineResult res = await Task.Run(() => ExecuteHardwarePipeline(targetPath, p, logger));
                if (res.Success)
                {
                    ShortcutEngine.Configure(shortcuts, targetPath);
                    if (!silent)
                    {
                        Log("✅ Applied to mouse successfully! (Volatile RAM registers updated).", Color.FromArgb(140, 240, 255));
                    }
                    return true;
                }

                string friendlyMsg = DescribePipelineFailure(res);
                Log(string.Format("❌ Apply failed at packet #{0}: {1} (Win32 {2}: {3})", res.FailingPacketIndex, res.ErrorMessage, res.Win32ErrorCode, friendlyMsg), Color.FromArgb(255, 80, 80));

                if (res.Win32ErrorCode == 1167 || res.Win32ErrorCode == 2 || res.Win32ErrorCode == 6)
                {
                    cachedDevicePath = null;
                    if (!this.InvokeRequired) SetConnected(false);
                }

                if (showErrors)
                {
                    MessageBox.Show(this, string.Format("Failed to apply settings at packet #{0}.\n\nError: {1}\n{2}", res.FailingPacketIndex, res.ErrorMessage, friendlyMsg), "Apply Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }
            catch (Exception ex)
            {
                Log("❌ Apply failed with an unexpected error: " + ex.Message, Color.FromArgb(255, 80, 80));
                if (showErrors)
                {
                    MessageBox.Show(this, "Failed to apply settings:\n\n" + ex.Message, "Apply Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }
            finally
            {
                isSavingInProgress = false;
                if (onUi) SetUiBusy(false);
            }
        }

        // ─── ACTION BUTTON HANDLERS ───
        private async void BtnPreview_Click(object sender, EventArgs e)
        {
            if (isSavingInProgress || draft == null) return;
            if (!CommitHexEdit(true)) return;
            bool ok = await ApplyProfileAsync(draft.Clone(), false);
            if (!ok) return;
            mouseShowsDraft = true;
            SetStatus("Sent to mouse ✓");
        }

        private async void BtnSave_Click(object sender, EventArgs e)
        {
            await SaveDraftAsync();
        }

        // Saves the draft into the active profile (works offline), then applies it when the mouse is present.
        private async Task<bool> SaveDraftAsync()
        {
            if (isSavingInProgress || draft == null) return false;
            if (profileList != null) profileList.CommitEdit();
            if (!CommitHexEdit(true)) return false;
            if (!SaveDraftToDisk()) return false;

            // Clean up any legacy Task Scheduler task
            string dummy;
            TaskSchedulerHelper.RemoveTask(out dummy);
            SetAutoStart(currentProfile.Mode);
            if (trayIcon != null) trayIcon.Visible = currentProfile.Mode == PersistenceMode.TrayAgent && !this.Visible;
            statusText = null;
            RefreshDraftState();

            if (string.IsNullOrEmpty(cachedDevicePath)) RefreshDeviceConnection();
            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                SetStatus("Saved · applies when the mouse is connected");
            }
            else
            {
                bool ok = await ApplyProfileAsync(currentProfile, false, true);
                if (ok) mouseShowsDraft = false;
                SetStatus(ok ? "Saved and applied ✓" : "Saved · could not apply, see Settings log");
            }
            TrimMemory();
            return true;
        }

        private async void BtnReset_Click(object sender, EventArgs e)
        {
            if (isSavingInProgress) return;

            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                RefreshDeviceConnection();
                if (string.IsNullOrEmpty(cachedDevicePath))
                {
                    MessageBox.Show(this, "Redgear A20 is not connected.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            DialogResult dr = MessageBox.Show(this,
                "This restores the mouse to factory defaults (stock per-stage DPI colours and breathing mode) and turns auto-apply off.\n\nYour saved profiles are kept. Proceed?",
                "Confirm Factory Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (dr != DialogResult.Yes) return;

            string targetPath = cachedDevicePath;
            SetUiBusy(true);
            isSavingInProgress = true;
            bool success = false;
            PipelineResult res = null;

            try
            {
                res = await Task.Run(() => ExecuteFactoryReset(targetPath, Log));
                success = res.Success;
            }
            catch (Exception ex)
            {
                Log("❌ Factory reset failed with an unexpected error: " + ex.Message, Color.FromArgb(255, 80, 80));
            }
            finally
            {
                isSavingInProgress = false;
                SetUiBusy(false);
            }

            if (success)
            {
                SetMode(PersistenceMode.Disabled);
                LoadDraftIntoUi();
                mouseShowsDraft = false;
                Log("✅ Factory profile restored!", Color.FromArgb(80, 230, 130));
                MessageBox.Show(this, "Factory profile restored.\n\nThe mouse is back in its default multi-stage breathing mode and auto-apply is off. Click a profile to apply it again.",
                    "Factory Reset Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (res != null)
            {
                string friendlyMsg = DescribePipelineFailure(res);
                Log(string.Format("❌ Factory reset failed at packet #{0}: {1} (Win32 {2}: {3})", res.FailingPacketIndex, res.ErrorMessage, res.Win32ErrorCode, friendlyMsg), Color.FromArgb(255, 80, 80));
                if (res.Win32ErrorCode == 1167 || res.Win32ErrorCode == 2 || res.Win32ErrorCode == 6)
                {
                    cachedDevicePath = null;
                    SetConnected(false);
                }
                MessageBox.Show(this, string.Format("Factory reset failed at packet #{0}.\n\nError: {1}\n{2}", res.FailingPacketIndex, res.ErrorMessage, friendlyMsg), "Reset Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ─── UNVERIFIED / EXPERIMENTAL SECTION: DPI TESTING ───
        private async void BtnTestDpi_Click(object sender, EventArgs e)
        {
            if (draft == null || !draft.EnableDpi) return;
            if (isSavingInProgress)
            {
                Log("Another hardware operation is in progress. Please wait and retry.", Color.FromArgb(255, 200, 80));
                return;
            }
            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                MessageBox.Show(this, "Redgear A20 is not connected.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int selectedStage = draft.ActiveDpiStage;
            if (selectedStage < 0 || selectedStage > 5) selectedStage = 0;
            string targetPath = cachedDevicePath;

            btnTestDpi.Enabled = false;

            try
            {
                Log("⚠️ --- Transmitting EXPERIMENTAL DPI Command 0x09 (RAM Only, Never Flashed) ---", Color.FromArgb(255, 120, 50));

                byte b2 = (byte)(0x40 | (selectedStage & 0x07));
                byte[] dpiPacket = new byte[] { HidTransport.REPORT_ID, 0x09, b2, 0x08, 0x3F, 0x00, 0x00, 0x00 };

                bool lockAcquired = false;
                string err = null;
                bool ok = false;

                bool lockSuccess = await Task.Run(() =>
                {
                    Mutex hwMutex = null;
                    try
                    {
                        try
                        {
                            hwMutex = new Mutex(false, TaskSchedulerHelper.HARDWARE_MUTEX_NAME);
                            try { lockAcquired = hwMutex.WaitOne(3000, false); }
                            catch (AbandonedMutexException) { lockAcquired = true; }
                        }
                        catch { lockAcquired = false; }

                        if (!lockAcquired) return false;

                        try
                        {
                            ok = HidTransport.SendSinglePacket(targetPath, dpiPacket, out err);
                        }
                        catch (Exception exSend)
                        {
                            ok = false;
                            err = "Unexpected transport failure: " + exSend.Message;
                        }
                        return true;
                    }
                    finally
                    {
                        if (hwMutex != null)
                        {
                            if (lockAcquired) { try { hwMutex.ReleaseMutex(); } catch { } }
                            hwMutex.Close();
                        }
                    }
                });

                if (!lockSuccess)
                {
                    Log("Cmd 0x09: Hardware lock timeout (Device busy).", Color.FromArgb(255, 80, 80));
                    return;
                }

                if (ok)
                {
                    mouseShowsDraft = true;
                    Log(string.Format("Cmd 0x09 [Stage {0}]: {1} -> OK", selectedStage, FormatHex(dpiPacket)), Color.FromArgb(255, 200, 80));
                    Log("Test completed. Test mouse sensitivity now.", Color.FromArgb(200, 200, 220));
                    SetStatus(string.Format("Stage {0} sent to mouse ✓", selectedStage));
                }
                else
                {
                    Log(string.Format("Cmd 0x09 [Stage {0}]: {1} -> FAIL ({2})", selectedStage, FormatHex(dpiPacket), err), Color.FromArgb(255, 80, 80));
                    SetStatus("DPI test failed · see Settings log");
                }
            }
            finally
            {
                // Respect the toggle if the user disabled DPI mid-operation (or the UI was rebuilt).
                UpdateDpiEnabledState();
            }
        }

        // ─── BUTTON REMAPPING ───
        // Binding edits only touch the draft; Preview / Save Profile send them like any other change.
        private void ShowBindingMenu(byte id, Rectangle row)
        {
            ButtonMap.ButtonInfo info = ButtonMap.Find(id);
            if (draft == null || info == null || info.Locked || isSavingInProgress) return;
            ButtonAction current = draft.GetBinding(id);
            ContextMenuStrip m = UiMenuRenderer.Create();
            m.ShowCheckMargin = true;
            ButtonAction[] actions = ButtonMap.Selectable;
            for (int i = 0; i < actions.Length; i++)
            {
                ButtonAction a = actions[i];
                if (a == ButtonAction.Disabled || a == ButtonAction.Shortcut) m.Items.Add(new ToolStripSeparator());
                string text = ButtonMap.Describe(a);
                KeyShortcut ks;
                if (a == ButtonAction.Shortcut && current == a && draft.TryGetShortcut(id, out ks)) text = "Keyboard Shortcut: " + KeyShortcuts.Describe(ks) + "…";
                if (a == info.FactoryAction) text += "  (default)";
                ToolStripMenuItem it = UiMenuRenderer.Item(m, text, delegate
                {
                    if (a == ButtonAction.Shortcut) BeginInvoke(new Action(delegate { EditBindingShortcut(id); }));
                    else SetDraftBinding(id, a);
                });
                it.Checked = a == current;
            }
            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
            m.Show(new Point(row.Right, row.Bottom), ToolStripDropDownDirection.BelowLeft);
        }

        private void SetDraftBinding(byte id, ButtonAction a)
        {
            if (draft == null) return;
            draft.SetBinding(id, a);
            OnDraftChanged();
        }

        private void EditBindingShortcut(byte id)
        {
            if (draft == null || isSavingInProgress) return;
            KeyShortcut current;
            bool has = draft.TryGetShortcut(id, out current);
            using (ShortcutPickerForm picker = new ShortcutPickerForm(ButtonMap.Find(id).DisplayName, has ? current : new KeyShortcut()))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                draft.SetShortcut(id, picker.Result);
            }
            SetDraftBinding(id, ButtonAction.Shortcut);
        }

        private void RestoreDefaultBindings()
        {
            if (draft == null || isSavingInProgress) return;
            ButtonMap.ButtonInfo[] all = ButtonMap.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].Locked) draft.SetBinding(all[i].Id, all[i].FactoryAction);
            }
            OnDraftChanged();
        }

        // A button the listener currently serves (e.g. from an unsaved preview) but which the
        // profile no longer binds to Shortcut gets an explicit factory reset, so it can't go dead.
        private static ButtonBinding[] AddStaleShortcutResets(ButtonBinding[] binds, Dictionary<byte, KeyShortcut> keep)
        {
            List<ButtonBinding> list = new List<ButtonBinding>(binds);
            foreach (byte id in ShortcutEngine.ActiveButtonIds())
            {
                if (keep.ContainsKey(id)) continue;
                bool listed = false;
                for (int i = 0; i < list.Count; i++) if (list[i].ButtonId == id) listed = true;
                ButtonMap.ButtonInfo info = ButtonMap.Find(id);
                if (!listed && info != null && !info.Locked) list.Add(new ButtonBinding(id, info.FactoryAction));
            }
            return list.ToArray();
        }

        internal static string FormatHex(byte[] bytes)
        {
            if (bytes == null) return "";
            string[] hex = new string[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) hex[i] = bytes[i].ToString("X2");
            return string.Join(" ", hex);
        }

        private static Color ParseColor(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return Color.FromArgb(190, 89, 255);
                string clean = hex.Trim().TrimStart('#');
                if (clean.Length != 6) return Color.FromArgb(190, 89, 255);
                int r = Convert.ToInt32(clean.Substring(0, 2), 16);
                int g = Convert.ToInt32(clean.Substring(2, 2), 16);
                int b = Convert.ToInt32(clean.Substring(4, 2), 16);
                return Color.FromArgb(r, g, b);
            }
            catch
            {
                return Color.FromArgb(190, 89, 255);
            }
        }

        private static int HeadlessApplyAndExit()
        {
            try
            {
                string devicePath = null;
                ushort uPage = 0;
                const int MAX_WAIT_MS = 2500;
                const int POLL_INTERVAL_MS = 150;
                int elapsed = 0;

                while (elapsed < MAX_WAIT_MS)
                {
                    devicePath = HidTransport.FindTargetDevicePath(out uPage);
                    if (!string.IsNullOrEmpty(devicePath)) break;
                    Thread.Sleep(POLL_INTERVAL_MS);
                    elapsed += POLL_INTERVAL_MS;
                }

                if (string.IsNullOrEmpty(devicePath)) return 1;

                // USB buffer stabilization delay
                Thread.Sleep(100);

                AppProfile profile = AppProfile.Load();
                Color color = ParseColor(profile.HexColor);
                ButtonBinding[] binds = profile.GetBindingsToTransmit(false);

                SaveParameters p = new SaveParameters
                {
                    Color = color,
                    Brightness = profile.Brightness,
                    ActiveDpiIndex = (byte)Math.Max(0, Math.Min(5, profile.ActiveDpiStage)),
                    DpiMask = 0x3F,
                    CommitToFlash = false,
                    SendDpi = profile.EnableDpi,
                    SendButtonBindings = binds.Length > 0,
                    ButtonBindings = binds
                };

                PipelineResult res = ExecuteHardwarePipeline(devicePath, p, delegate(string s, Color c) { });
                return res.Success ? 0 : 2;
            }
            catch
            {
                return 3;
            }
        }

        private static void LogUnhandledException(Exception ex, bool isFatal)
        {
            if (ex == null) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RedgearA20");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string logFile = Path.Combine(dir, "crash.log");
                string entry = string.Format(CultureInfo.InvariantCulture, "[{0:yyyy-MM-dd HH:mm:ss}] {1}Unhandled Exception: {2}\n{3}\n\n",
                    DateTime.Now, isFatal ? "FATAL " : "", ex.Message, ex.StackTrace);
                File.AppendAllText(logFile, entry, Encoding.UTF8);
            }
            catch { }
        }

        // ─── Single-instance activation ───
        // Primary channel: session-local named event (survives window-handle recreation).
        // Fallback: direct PostMessage to the known control-center window, for older builds
        // that only listen for the registered window message.
        private static void SignalExistingInstanceToShow()
        {
            const int MAX_ATTEMPTS = 12;
            const int RETRY_DELAY_MS = 150;

            for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
            {
                try
                {
                    using (EventWaitHandle ev = EventWaitHandle.OpenExisting(TaskSchedulerHelper.SHOW_EVENT_NAME))
                    {
                        ev.Set();
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException) { }
                catch (UnauthorizedAccessException) { }
                catch { }

                if (PostShowMessageToExistingWindow()) return;
                Thread.Sleep(RETRY_DELAY_MS);
            }
        }

        private static bool PostShowMessageToExistingWindow()
        {
            bool posted = false;
            try
            {
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    if (posted) return false;

                    int len = GetWindowTextLength(hWnd);
                    if (len <= 0) return true;

                    StringBuilder title = new StringBuilder(len + 1);
                    if (GetWindowText(hWnd, title, title.Capacity) <= 0) return true;
                    if (!title.ToString().StartsWith("Redgear A20 Control Center", StringComparison.OrdinalIgnoreCase)) return true;

                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    try
                    {
                        using (Process owner = Process.GetProcessById((int)pid))
                        {
                            if (!owner.ProcessName.StartsWith("RedgearA20", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        return true;
                    }

                    posted = PostMessage(hWnd, wmShowControlCenter, IntPtr.Zero, IntPtr.Zero);
                    return !posted;
                }, IntPtr.Zero);
            }
            catch { }
            return posted;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            bool startHidden = false;
            bool isStealth = false;
            bool applyAndExit = false;
            bool justUpdated = false;

            bool installTask = false;
            bool removeTask = false;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a == "--shutdown" || a == "/shutdown")
                    {
                        Environment.Exit(MainForm.SignalShutdown());
                        return;
                    }
                }

                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a == "--stealth" || a == "-stealth" || a == "/stealth" ||
                        a == "--silent" || a == "-silent" || a == "/silent" ||
                        a == "--watcher" || a == "-watcher" || a == "/watcher")
                    {
                        startHidden = true;
                        isStealth = true;
                    }
                    else if (a == "--tray" || a == "-tray" || a == "/tray" || a == "--minimized")
                    {
                        startHidden = true;
                        isStealth = false;
                    }
                    else if (a == "--apply-and-exit" || a == "-apply" || a == "/apply")
                    {
                        applyAndExit = true;
                    }
                    else if (a == "--updated")
                    {
                        justUpdated = true;
                    }
                    else if (a == "--install-task" || a == "-install-task" || a == "/install-task" || a == "--install")
                    {
                        installTask = true;
                    }
                    else if (a == "--remove-task" || a == "-remove-task" || a == "/remove-task" || a == "--remove")
                    {
                        removeTask = true;
                    }
                }
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                LogUnhandledException(e.Exception, false);
                if (!startHidden && e.Exception != null)
                {
                    MessageBox.Show("An unexpected error occurred:\n\n" + e.Exception.Message + "\n\nDiagnostic details written to %LocalAppData%\\RedgearA20\\crash.log",
                        "Redgear A20 Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                LogUnhandledException(ex, true);
                if (!startHidden && ex != null)
                {
                    MessageBox.Show("A fatal error occurred:\n\n" + ex.Message + "\n\nDiagnostic details written to %LocalAppData%\\RedgearA20\\crash.log",
                        "Redgear A20 Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            if (installTask)
            {
                Console.WriteLine("Note: Task Scheduler XML installer is deprecated in favor of lightweight Stealth Watcher persistence (--stealth).");
                Console.WriteLine("Configuring auto-start watcher in Windows Run registry key...");
                SetAutoStart(PersistenceMode.StealthWatcher);
                Environment.Exit(0);
                return;
            }

            if (removeTask)
            {
                string err;
                bool ok = TaskSchedulerHelper.RemoveTask(out err);
                if (!ok) Console.Error.WriteLine("Failed to remove task: " + err);
                Environment.Exit(ok ? 0 : 1);
                return;
            }

            if (applyAndExit)
            {
                int exitCode = HeadlessApplyAndExit();
                Environment.Exit(exitCode);
                return;
            }

            if (startHidden)
            {
                AppProfile p = AppProfile.Load();
                if (p.Mode == PersistenceMode.Disabled)
                {
                    SetAutoStart(PersistenceMode.Disabled);
                    return;
                }
                if (isStealth && p.Mode == PersistenceMode.TrayAgent)
                {
                    isStealth = false;
                }
                else if (!isStealth && p.Mode == PersistenceMode.StealthWatcher)
                {
                    isStealth = true;
                }
            }

            wmShowControlCenter = RegisterWindowMessage("RedgearA20_ShowControlCenter");
            try
            {
                ChangeWindowMessageFilter(wmShowControlCenter, MSGFLT_ADD);
                ChangeWindowMessageFilter(WM_DEVICECHANGE, MSGFLT_ADD);
            }
            catch { }

            bool createdNew = false;
            try
            {
                appMutex = new Mutex(true, MainForm.SINGLE_INSTANCE_MUTEX, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                // Previous GUI instance terminated abruptly while holding mutex; ownership successfully acquired
                createdNew = true;
            }

            if (!createdNew && justUpdated)
            {
                // The previous version is still shutting down; take over once it releases the lock.
                try { createdNew = appMutex.WaitOne(15000, false); }
                catch (AbandonedMutexException) { createdNew = true; }
            }

            if (!createdNew)
            {
                if (!startHidden)
                {
                    // HWND_BROADCAST silently drops registered messages (>= WM_USER) on
                    // modern Windows. Signal the running instance deterministically.
                    SignalExistingInstanceToShow();
                }
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                UpdateManager.CleanupOldBinaries();
                Application.Run(new MainForm(startHidden, isStealth, justUpdated));
            }
            finally
            {
                try { ShortcutEngine.Shutdown(); } catch { }
                if (appMutex != null)
                {
                    try { appMutex.ReleaseMutex(); } catch { }
                    appMutex.Close();
                }
            }
        }
    }

    // ─── Shortcut picker pieces ───
    // Modifier chip (a checkbox drawn as a pill).
    public class UiChip : CheckBox
    {
        private bool hover;
        public UiChip(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Ui.BodyBold;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = Ui.Sf(6);
            Color fill = Checked ? Theme.AccentDim : (hover ? Theme.SurfaceHover : Theme.SurfaceElevated);
            Ui.FillRound(g, fill, r, radius);
            Ui.DrawRound(g, Checked ? Theme.Accent : Theme.Border, r, radius, 1f);
            if (Focused && ShowFocusCues) Ui.DrawRound(g, Theme.Accent, new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f), radius, Math.Max(1.5f, Ui.Sf(1.5f)));
            Color fore = !Enabled ? Theme.TextDisabled : (Checked ? Theme.Accent : Theme.TextSecondary);
            Size ts = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int icon = Checked ? Ui.S(14) + Ui.S(6) : 0;
            int x = (Width - ts.Width - icon) / 2;
            if (Checked)
            {
                Ui.Glyph(g, Glyphs.Check, Ui.IconSmall, new Rectangle(x, 0, Ui.S(14), Height), fore);
                x += icon;
            }
            Ui.Text(g, Text, Font, new Rectangle(x, 0, ts.Width + 2, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // Focusable field that swallows every key (Tab, Enter, Esc, Alt+F4 ...) and shows the combo as keycaps.
    public class ShortcutCaptureBox : Control
    {
        private static readonly string[] ModNames = { "Ctrl", "Shift", "Alt", "Win" };
        public KeyShortcut Value;
        public bool HasValue, Invalid;
        private byte held;
        private bool winDown, hover;

        public event KeyEventHandler KeyCaptured;
        public bool WinHeld { get { return winDown; } }

        public ShortcutCaptureBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Text;
        }

        public static bool IsModifierKey(Keys k)
        {
            return k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin ||
                   k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.LShiftKey || k == Keys.RShiftKey ||
                   k == Keys.LMenu || k == Keys.RMenu;
        }

        protected override bool IsInputKey(Keys keyData) { return true; }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { held = 0; winDown = false; Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { held = 0; winDown = false; Invalidate(); base.OnLostFocus(e); }

        private void UpdateHeld(KeyEventArgs e)
        {
            byte m = 0;
            if (e.Control) m |= KeyShortcuts.MOD_CTRL;
            if (e.Shift) m |= KeyShortcuts.MOD_SHIFT;
            if (e.Alt) m |= KeyShortcuts.MOD_ALT;
            if (winDown) m |= KeyShortcuts.MOD_WIN;
            if (m != held) { held = m; Invalidate(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) winDown = true;
            if (IsModifierKey(e.KeyCode)) { UpdateHeld(e); return; }
            if (KeyCaptured != null) KeyCaptured(this, e);
        }

        // Print Screen only produces a key-up in WinForms.
        protected override void OnKeyUp(KeyEventArgs e)
        {
            e.Handled = true;
            if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) winDown = false;
            if (e.KeyCode == Keys.PrintScreen && KeyCaptured != null) KeyCaptured(this, e);
            UpdateHeld(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e) { e.Handled = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = Ui.Sf(8);
            Ui.FillRound(g, hover && !Focused ? Theme.SurfaceHover : Theme.Surface, r, radius);
            if (Focused) Ui.DrawRound(g, Theme.Accent, new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f), radius, Math.Max(1.5f, Ui.Sf(1.5f)));
            else Ui.DrawRound(g, Invalid ? Theme.Danger : Theme.Border, r, radius, 1f);

            List<string> caps = new List<string>();
            bool pending = Focused && held != 0;
            byte mods = pending ? held : Value.Mods;
            if (pending || HasValue)
            {
                for (int bit = 0; bit < 4; bit++) if ((mods & (1 << bit)) != 0) caps.Add(ModNames[bit]);
                caps.Add(pending ? null : KeyShortcuts.KeyName(Value.Vk));
            }
            else
            {
                Ui.Text(g, Focused ? "Press a key combination…" : "Click here, then press the keys", Ui.Body, ClientRectangle, Focused ? Theme.TextSecondary : Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }

            int capH = Ui.S(34), pad = Ui.S(12), plusW = Ui.S(22), depth = Ui.S(3);
            int[] widths = new int[caps.Count];
            int total = 0;
            for (int i = 0; i < caps.Count; i++)
            {
                string t = caps[i] ?? "key";
                widths[i] = Math.Max(capH, TextRenderer.MeasureText(g, t, Ui.BodyBold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + pad * 2);
                total += widths[i] + (i > 0 ? plusW : 0);
            }
            int x = (Width - total) / 2, y = (Height - capH - depth) / 2;
            for (int i = 0; i < caps.Count; i++)
            {
                if (i > 0)
                {
                    Ui.Text(g, "+", Ui.Body, new Rectangle(x, y, plusW, capH), Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    x += plusW;
                }
                RectangleF cap = new RectangleF(x + 0.5f, y + 0.5f, widths[i] - 1f, capH - 1f);
                if (caps[i] == null)
                {
                    using (GraphicsPath p = Ui.RoundRect(cap, Ui.Sf(6)))
                    using (Pen pen = new Pen(Theme.TextMuted, 1f) { DashStyle = DashStyle.Dash })
                        g.DrawPath(pen, p);
                    Ui.Text(g, "key", Ui.Body, Rectangle.Round(cap), Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                else
                {
                    Ui.FillRound(g, Theme.Border, new RectangleF(cap.X, cap.Y + depth, cap.Width, cap.Height), Ui.Sf(6));
                    Ui.FillRound(g, Theme.SurfaceElevated, cap, Ui.Sf(6));
                    Ui.DrawRound(g, Invalid && !pending ? Theme.Danger : Theme.Border, cap, Ui.Sf(6), 1f);
                    Ui.Text(g, caps[i], Ui.BodyBold, Rectangle.Round(cap), Invalid && !pending ? Theme.Danger : Theme.TextPrimary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                x += widths[i];
            }
        }
    }

    // Captures one key combination. Rules: 0-4 modifiers plus exactly one allow-listed main key,
    // Ctrl+Alt+Del refused, window-closing / lock combos confirmed. Win is also a chip because the
    // Windows shell swallows most Win+key presses before any app can see them.
    public class ShortcutPickerForm : Form
    {
        private static readonly string[] ModNames = { "Ctrl", "Shift", "Alt", "Win" };
        private readonly UiChip[] modChips = new UiChip[4];
        private readonly ShortcutCaptureBox capture;
        private readonly Label lblHint, lblHintIcon;
        private readonly UiButton btnOk;
        private byte vk;
        private bool suppress;

        public KeyShortcut Result;

        public ShortcutPickerForm(string buttonName, KeyShortcut initial)
        {
            Text = "Keyboard shortcut · " + buttonName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.S(420), Ui.S(346));
            BackColor = Theme.Canvas;
            ForeColor = Theme.TextPrimary;
            Font = Ui.Body;

            AddLabel(this, "Keyboard shortcut", Ui.Big, Theme.TextPrimary, Ui.R(24, 20, 372, 26));
            AddLabel(this, "Pressing " + buttonName + " sends this key combination.", Ui.Small, Theme.TextSecondary, Ui.R(24, 48, 372, 18));

            capture = new ShortcutCaptureBox { Bounds = Ui.R(24, 80, 372, 76), AccessibleName = "Shortcut capture. Press the key combination." };
            capture.KeyCaptured += Capture_KeyCaptured;
            Controls.Add(capture);

            AddLabel(this, "MODIFIERS", Ui.Caption, Theme.TextMuted, Ui.R(24, 172, 372, 16));
            for (int i = 0; i < 4; i++)
            {
                modChips[i] = new UiChip(ModNames[i])
                {
                    Bounds = Ui.R(24 + i * 96, 192, 84, 32),
                    Checked = (initial.Mods & (1 << i)) != 0
                };
                modChips[i].CheckedChanged += delegate { if (!suppress) UpdateState(); };
                // Keep keyboard capture live after a chip is clicked with the mouse.
                modChips[i].MouseUp += delegate { capture.Focus(); };
                Controls.Add(modChips[i]);
            }

            lblHintIcon = AddLabel(this, "", Ui.IconSmall, Theme.TextMuted, Ui.R(24, 240, 18, 18));
            lblHint = AddLabel(this, "", Ui.Small, Theme.TextSecondary, Ui.R(48, 239, 348, 34));

            Panel footer = new Panel { Bounds = new Rectangle(0, Ui.S(282), ClientSize.Width, ClientSize.Height - Ui.S(282)), BackColor = Theme.Surface };
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            Controls.Add(footer);

            UiButton btnClear = new UiButton(UiButtonKind.Ghost, "Clear", Glyphs.Delete) { Bounds = Ui.R(16, 14, 92, 36) };
            btnClear.Click += delegate
            {
                vk = 0;
                suppress = true;
                for (int i = 0; i < 4; i++) modChips[i].Checked = false;
                suppress = false;
                UpdateState();
                capture.Focus();
            };
            footer.Controls.Add(btnClear);

            UiButton btnCancel = new UiButton(UiButtonKind.Secondary, "Cancel", null) { Bounds = Ui.R(188, 14, 100, 36), DialogResult = DialogResult.Cancel };
            footer.Controls.Add(btnCancel);
            btnOk = new UiButton(UiButtonKind.Primary, "Assign", Glyphs.Check) { Bounds = Ui.R(300, 14, 100, 36) };
            btnOk.Click += BtnOk_Click;
            footer.Controls.Add(btnOk);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            vk = initial.Vk;
            UpdateState();
        }

        private static Label AddLabel(Control parent, string text, Font font, Color color, Rectangle bounds)
        {
            Label l = new Label { Text = text, Font = font, ForeColor = color, Bounds = bounds, AutoSize = false, UseMnemonic = false, BackColor = parent.BackColor };
            parent.Controls.Add(l);
            return l;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            MainForm.ApplyDarkTitleBar(this.Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            capture.Focus();
        }

        private void Capture_KeyCaptured(object sender, KeyEventArgs e)
        {
            suppress = true;
            modChips[0].Checked = e.Control;
            modChips[1].Checked = e.Shift;
            modChips[2].Checked = e.Alt;
            if (capture.WinHeld) modChips[3].Checked = true;
            suppress = false;
            vk = (byte)((int)e.KeyCode & 0xFF);
            UpdateState();
        }

        private KeyShortcut Current()
        {
            byte mods = 0;
            for (int i = 0; i < 4; i++) if (modChips[i].Checked) mods |= (byte)(1 << i);
            return new KeyShortcut(mods, vk);
        }

        private void UpdateState()
        {
            KeyShortcut ks = Current();
            bool ok = false;
            string hint;
            if (vk == 0)
                hint = "Hold any modifiers and press one key. Windows grabs most Win + key presses, so add Win with its chip instead.";
            else if (KeyShortcuts.VkToUsage(vk) == 0)
                hint = KeyShortcuts.KeyName(vk) + " can't be used. Lock keys, media keys and lone modifiers are not supported.";
            else if (KeyShortcuts.IsBlocked(ks))
                hint = "Ctrl + Alt + Del is reserved by Windows and can't be assigned.";
            else
            {
                ok = true;
                hint = "Fires once per button press. Some combos (e.g. Alt + Tab) are handled by Windows itself.";
            }
            capture.Value = ks;
            capture.HasValue = vk != 0;
            capture.Invalid = vk != 0 && !ok;
            capture.AccessibleDescription = vk != 0 ? KeyShortcuts.Describe(ks) : "Empty";
            capture.Invalidate();
            lblHint.Text = hint;
            Color c = ok ? Theme.Accent : (vk == 0 ? Theme.TextMuted : Theme.Danger);
            lblHint.ForeColor = ok || vk == 0 ? Theme.TextSecondary : Theme.Danger;
            lblHintIcon.ForeColor = c;
            lblHintIcon.Text = ok ? Glyphs.Check : (vk == 0 ? Glyphs.Info : Glyphs.Warning);
            btnOk.Enabled = ok;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            KeyShortcut ks = Current();
            if (!KeyShortcuts.IsAllowed(ks)) return;
            string warning = KeyShortcuts.Warning(ks);
            if (warning != null)
            {
                DialogResult r = MessageBox.Show(this,
                    KeyShortcuts.Describe(ks) + " " + warning + "\n\nAssign it anyway?",
                    "Confirm Shortcut", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            Result = ks;
            DialogResult = DialogResult.OK;
        }
    }
}
