using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

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

        public static string FindTargetDevicePath(out ushort detectedUsagePage)
        {
            detectedUsagePage = 0;
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

                                                if (caps.UsagePage == USAGE_PAGE_PRIMARY)
                                                {
                                                    primaryPath = path;
                                                    detectedUsagePage = USAGE_PAGE_PRIMARY;
                                                    break; // Ideal match found
                                                }
                                                else if (caps.UsagePage == USAGE_PAGE_FALLBACK && fallbackPath == null)
                                                {
                                                    fallbackPath = path;
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

            if (primaryPath != null) return primaryPath;
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
            AppProfile p = new AppProfile();
            try
            {
                string file = GetProfilePath();
                if (File.Exists(file))
                {
                    string json = File.ReadAllText(file);
                    Match mHex = Regex.Match(json, "\"HexColor\"\\s*:\\s*\"([^\"]+)\"");
                    if (mHex.Success) p.HexColor = mHex.Groups[1].Value;

                    Match mBri = Regex.Match(json, "\"Brightness\"\\s*:\\s*(\\d+)");
                    if (mBri.Success) p.Brightness = Math.Max(5, Math.Min(100, int.Parse(mBri.Groups[1].Value)));

                    Match mDpiStage = Regex.Match(json, "\"ActiveDpiStage\"\\s*:\\s*(\\d+)");
                    if (mDpiStage.Success) p.ActiveDpiStage = Math.Max(0, Math.Min(5, int.Parse(mDpiStage.Groups[1].Value)));

                    Match mEnDpi = Regex.Match(json, "\"EnableDpi\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
                    if (mEnDpi.Success) p.EnableDpi = bool.Parse(mEnDpi.Groups[1].Value);

                    Match mMode = Regex.Match(json, "\"(?:PersistenceMode|Mode)\"\\s*:\\s*(\\d+)");
                    if (mMode.Success)
                    {
                        int modeVal = int.Parse(mMode.Groups[1].Value);
                        if (Enum.IsDefined(typeof(PersistenceMode), modeVal))
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
            catch { }
            return p;
        }

        public bool Save(out string error)
        {
            error = null;
            string tmpFile = null;
            try
            {
                string file = GetProfilePath();
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                int bri = Math.Max(5, Math.Min(100, Brightness));
                int dpi = Math.Max(0, Math.Min(5, ActiveDpiStage));
                string json = string.Format("{{\n  \"HexColor\": \"{0}\",\n  \"Brightness\": {1},\n  \"ActiveDpiStage\": {2},\n  \"EnableDpi\": {3},\n  \"PersistenceMode\": {4}\n}}\n",
                    HexColor, bri, dpi, EnableDpi.ToString().ToLower(), (int)Mode);

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
                        File.Delete(file);
                        File.Move(tmpFile, file);
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

    public static class TaskSchedulerHelper
    {
        public const string TASK_NAME = "RedgearA20_AutoApply";
        public const string HARDWARE_MUTEX_NAME = @"Local\RedgearA20_HardwareLock";

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

    // ═════════════════════════════════════════════════════════════════════════
    //  UI Design System Tokens (Zero-Allocation GDI+ Theme)
    // ═════════════════════════════════════════════════════════════════════════
    public static class Theme
    {
        // ─── Colors: Base Neutrals ───
        public static readonly Color Canvas = Color.FromArgb(18, 19, 22);          // #121316
        public static readonly Color Surface = Color.FromArgb(26, 28, 34);         // #1A1C22
        public static readonly Color SurfaceElevated = Color.FromArgb(36, 39, 49); // #242731
        public static readonly Color SurfaceHover = Color.FromArgb(45, 49, 62);    // #2D313E
        public static readonly Color Border = Color.FromArgb(43, 47, 58);          // #2B2F3A
        public static readonly Color BorderSubtle = Color.FromArgb(32, 35, 42);    // #20232A

        // ─── Colors: Text Neutrals ───
        public static readonly Color TextPrimary = Color.FromArgb(241, 243, 247);  // #F1F3F7
        public static readonly Color TextSecondary = Color.FromArgb(141, 147, 165);// #8D93A5
        public static readonly Color TextMuted = Color.FromArgb(90, 96, 114);       // #5A6072
        public static readonly Color TextDisabled = Color.FromArgb(82, 88, 102);   // #525866

        // ─── Colors: Single Primary Accent (Emerald / Hardware Mint) ───
        public static readonly Color Accent = Color.FromArgb(16, 185, 129);        // #10B981
        public static readonly Color AccentHover = Color.FromArgb(5, 150, 105);    // #059669
        public static readonly Color AccentText = Color.White;                     // #FFFFFF
        public static readonly Color AccentDim = Color.FromArgb(6, 78, 59);        // #064E3B

        // ─── Colors: Functional Semantic States ───
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);       // #F59E0B
        public static readonly Color WarningSurface = Color.FromArgb(35, 28, 19);  // #231C13
        public static readonly Color WarningBorder = Color.FromArgb(92, 61, 16);   // #5C3D10
        public static readonly Color Danger = Color.FromArgb(239, 68, 68);         // #EF4444
        public static readonly Color DangerSurface = Color.FromArgb(40, 20, 22);   // #281416
        public static readonly Color DangerBorder = Color.FromArgb(77, 29, 34);    // #4D1D22
        public static readonly Color TerminalBg = Color.FromArgb(14, 15, 18);      // #0E0F12
        public static readonly Color TerminalText = Color.FromArgb(16, 185, 129);  // #10B981

        // ─── Fonts: System Fonts Only (Pre-allocated Static Instances) ───
        public static readonly Font FontHeading = new Font("Segoe UI", 9.75f, FontStyle.Bold);      // 13px bold
        public static readonly Font FontBodyPrimary = new Font("Segoe UI", 9.0f, FontStyle.Regular);// 12px
        public static readonly Font FontBodyBold = new Font("Segoe UI", 9.0f, FontStyle.Bold);       // 12px bold
        public static readonly Font FontBodySecondary = new Font("Segoe UI", 8.25f, FontStyle.Regular); // 11px
        public static readonly Font FontBadge = new Font("Segoe UI", 7.5f, FontStyle.Bold);         // 10px bold
        public static readonly Font FontMono = new Font("Consolas", 8.25f, FontStyle.Regular);      // 11px mono

        // ─── Cached GDI Brushes & Pens (Never reallocated in Paint) ───
        public static readonly SolidBrush BrushCanvas = new SolidBrush(Canvas);
        public static readonly SolidBrush BrushSurface = new SolidBrush(Surface);
        public static readonly SolidBrush BrushSurfaceElevated = new SolidBrush(SurfaceElevated);
        public static readonly SolidBrush BrushAccent = new SolidBrush(Accent);
        public static readonly SolidBrush BrushWarning = new SolidBrush(Warning);
        public static readonly SolidBrush BrushDanger = new SolidBrush(Danger);
        public static readonly SolidBrush BrushTextPrimary = new SolidBrush(TextPrimary);
        public static readonly SolidBrush BrushTextSecondary = new SolidBrush(TextSecondary);
        public static readonly Pen PenBorder = new Pen(Border, 1);
        public static readonly Pen PenBorderSubtle = new Pen(BorderSubtle, 1);
        public static readonly Pen PenAccent = new Pen(Accent, 1);
        public static readonly Pen PenWarning = new Pen(Warning, 1);
        public static readonly Pen PenDanger = new Pen(Danger, 1);

        // ─── Spacing Scale (Pixels) ───
        public const int Space1 = 4;
        public const int Space2 = 8;
        public const int Space3 = 12;
        public const int Space4 = 16;
        public const int Space5 = 20;

        // ─── Geometry ───
        public const int RadiusDefault = 4;
        public const int BorderWidth = 1;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Main GUI Application Form (Native Windows Forms, Dark Theme)
    // ═════════════════════════════════════════════════════════════════════════
    public class MainForm : Form
    {
        // UI Controls
        private Panel body;
        private Label lblStatus;
        private Panel pnlColorPreview;
        private TextBox txtHex;
        private Button btnPickColor;
        private TrackBar tbBrightness;
        private Label lblBrightnessVal;
        private Button btnApply;
        private Button btnSave;
        private Button btnReset;

        // Persistence & Startup UI (3-Mode Selection)
        private Panel gbPersistence;
        private RadioButton rbStealth;
        private RadioButton rbTrayAgent;
        private RadioButton rbDisabled;

        // Experimental DPI section
        private CheckBox cbEnableDpi;
        private Panel gbDpi;
        private ComboBox cmbDpiStages;
        private Button btnTestDpi;

        // Diagnostic Log Panel
        private RichTextBox rtbLog;
        private Button btnClearLog;
        private Button btnCopyLog;
        private Button btnRefresh;
        private Button btnExit;
        private Button[] presetButtons;

        // System Tray & Background Watcher
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private System.Windows.Forms.Timer usbDebounceTimer;

        // Internal State
        private Color currentColor = Color.FromArgb(190, 89, 255); // Soft Lavender default
        private int currentBrightness = 100;
        private string cachedDevicePath = null;
        private ushort cachedUsagePage = 0;
        private volatile bool isSavingInProgress = false;
        private AppProfile currentProfile;
        private bool startHidden = false;
        private bool isStealth = false;
        private bool isExplicitlyRestoring = false;
        private bool balloonShownOnce = false;
        private bool isExiting = false;
        private bool isHotplugHandling = false;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(int message, uint dwFlag);
        private const uint MSGFLT_ADD = 1;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        private static int wmShowControlCenter = 0;
        private static Mutex appMutex = null;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    int useDarkMode = 1;
                    if (DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                    }
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isSavingInProgress)
            {
                MessageBox.Show("Please wait until the operation completes before exiting.", "Busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }

            if (!isExiting && currentProfile != null && (currentProfile.Mode == PersistenceMode.StealthWatcher || currentProfile.Mode == PersistenceMode.TrayAgent))
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
            base.SetVisibleCore(value);
        }

        protected override void WndProc(ref Message m)
        {
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
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        private static void SetAutoStart(PersistenceMode mode)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_REG_KEY, true))
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
            miApply.Click += async delegate { await ApplyCurrentProfileAsync(silent: false); };
            trayMenu.Items.Add(miApply);

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
                    isExiting = true;
                    if (trayIcon != null) trayIcon.Visible = false;
                    Application.Exit();
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
        }

        private void MinimizeToWatcher(bool showBalloon)
        {
            this.Hide();
            this.ShowInTaskbar = false;
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
            TrimMemory();
        }

        private void RestoreGui()
        {
            isExplicitlyRestoring = true;
            startHidden = false;
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.BringToFront();
            this.Activate();
            RefreshDeviceConnection();
            UpdateColorPreview();
        }

        private void InitUsbWatcher()
        {
            usbDebounceTimer = new System.Windows.Forms.Timer();
            usbDebounceTimer.Interval = 400; // 400 ms debounce after USB hotplug
            usbDebounceTimer.Tick += OnUsbDebounceTimerTick;
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
                    await ApplyCurrentProfileAsync(silent: true);
                    TrimMemory();
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

        public MainForm() : this(false, false)
        {
        }

        public MainForm(bool hidden, bool stealth)
        {
            this.startHidden = hidden;
            this.isStealth = stealth;
            this.currentProfile = AppProfile.Load();
            this.currentColor = ParseColor(currentProfile.HexColor);
            this.currentBrightness = Math.Max(5, Math.Min(100, currentProfile.Brightness));

            InitForm();
            InitTrayIcon();
            InitUsbWatcher();

            RefreshDeviceConnection();
            UpdateColorPreview();

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
                            await ApplyCurrentProfileAsync(silent: true);
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

        private Panel CreateSectionPanel(Point loc, Size size, string title, string badge, bool isWarning)
        {
            Panel pnl = new Panel
            {
                Location = loc,
                Size = size,
                BackColor = isWarning ? Theme.WarningSurface : Theme.Surface,
                BorderStyle = BorderStyle.None
            };

            Pen pen = isWarning ? Theme.PenWarning : Theme.PenBorder;
            pnl.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);
            };

            Label lblTitle = new Label
            {
                Text = title,
                Location = new Point(12, 8),
                AutoSize = true,
                Font = Theme.FontHeading,
                ForeColor = isWarning ? Theme.Warning : Theme.TextPrimary
            };
            pnl.Controls.Add(lblTitle);

            if (!string.IsNullOrEmpty(badge))
            {
                int badgeWidth = badge.Length * 7 + 16;
                Label lblBadge = new Label
                {
                    Text = badge,
                    Location = new Point(size.Width - 12 - badgeWidth, 8),
                    Size = new Size(badgeWidth, 18),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = Theme.FontBadge,
                    BackColor = isWarning ? Color.FromArgb(48, 34, 16) : Theme.SurfaceElevated,
                    ForeColor = isWarning ? Theme.Warning : Theme.TextSecondary
                };
                Pen badgePen = isWarning ? Theme.PenWarning : Theme.PenBorderSubtle;
                lblBadge.Paint += delegate(object s, PaintEventArgs e)
                {
                    e.Graphics.DrawRectangle(badgePen, 0, 0, lblBadge.Width - 1, lblBadge.Height - 1);
                };
                pnl.Controls.Add(lblBadge);
            }

            return pnl;
        }

        private void OnPresetButtonClick(Button b, Color color)
        {
            currentColor = color;
            txtHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", currentColor.R, currentColor.G, currentColor.B);
            UpdateColorPreview();

            if (presetButtons != null)
            {
                for (int j = 0; j < presetButtons.Length; j++)
                {
                    bool isActive = (presetButtons[j] == b);
                    presetButtons[j].FlatAppearance.BorderColor = isActive ? Theme.Accent : Theme.Border;
                    presetButtons[j].ForeColor = isActive ? Color.White : Theme.TextSecondary;
                }
            }
        }

        private void InitForm()
        {
            this.Text = "Redgear A20 Control Center v0.2.0";
            this.Size = new Size(580, 835);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Theme.Canvas;
            this.ForeColor = Theme.TextPrimary;
            this.Font = Theme.FontBodyPrimary;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // 1. Connection Header Banner
            Panel pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(24, 26, 32)
            };
            pnlHeader.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(Theme.PenBorderSubtle, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };

            lblStatus = new Label
            {
                Text = "● Searching for Redgear A20...",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 175, 0),
                Font = Theme.FontBodyBold,
                ForeColor = Theme.TextSecondary
            };

            btnRefresh = new Button
            {
                Text = "Refresh",
                Size = new Size(75, 26),
                Location = new Point(390, 9),
                BackColor = Theme.SurfaceElevated,
                ForeColor = Theme.TextPrimary,
                Font = Theme.FontBodySecondary,
                FlatStyle = FlatStyle.Flat,
                TabIndex = 0,
                AccessibleName = "Refresh USB HID connection status"
            };
            btnRefresh.FlatAppearance.BorderColor = Theme.Border;
            btnRefresh.FlatAppearance.BorderSize = 1;
            btnRefresh.Click += delegate { RefreshDeviceConnection(); };

            btnExit = new Button
            {
                Text = "Exit App",
                Size = new Size(75, 26),
                Location = new Point(475, 9),
                BackColor = Theme.DangerSurface,
                ForeColor = Theme.Danger,
                Font = Theme.FontBodySecondary,
                FlatStyle = FlatStyle.Flat,
                TabIndex = 1,
                AccessibleName = "Completely exit application and stop background watcher"
            };
            btnExit.FlatAppearance.BorderColor = Theme.DangerBorder;
            btnExit.FlatAppearance.BorderSize = 1;

            ToolTip tipExit = new ToolTip();
            tipExit.SetToolTip(btnExit, "Completely stops the driver and background persistence.\nTo close this window while keeping persistence active, click the [X] button.");
            btnExit.Click += delegate
            {
                string warningText =
                    "⚠️ WARNING: This will completely SHUT DOWN Redgear A20 Control Center AND STOP the background persistence watcher!\n\n" +
                    "• If you terminate the driver, your mouse settings will NOT persist across USB unplugs, port switches, or system reboots.\n\n" +
                    "💡 RECOMMENDED ACTION:\n" +
                    "If you only want to close this window and keep auto-apply persistence running silently in the background (~1.8 MB RAM, 0% CPU):\n" +
                    "👉 Click 'No' and simply close this window using the Windows Close [X] button in the top-right corner.\n\n" +
                    "Are you sure you want to completely exit and STOP the persistence facility?";

                DialogResult dr = MessageBox.Show(
                    warningText,
                    "⚠️ Stop Driver Persistence & Exit?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2
                );

                if (dr == DialogResult.Yes)
                {
                    isExiting = true;
                    if (trayIcon != null) trayIcon.Visible = false;
                    Application.Exit();
                }
            };

            pnlHeader.Controls.Add(btnRefresh);
            pnlHeader.Controls.Add(btnExit);
            pnlHeader.Controls.Add(lblStatus);
            this.Controls.Add(pnlHeader);

            // Container Panel
            body = new Panel { Location = new Point(15, 52), Size = new Size(535, 735) };

            // ─── Group 1: Colour Selection ───
            Panel gbColor = CreateSectionPanel(new Point(0, 0), new Size(535, 115), "1. LED Colour Selection", "Verified Cmd 0x14", false);

            pnlColorPreview = new Panel
            {
                Location = new Point(12, 34),
                Size = new Size(52, 32),
                BorderStyle = BorderStyle.None,
                BackColor = currentColor,
                AccessibleName = "Current RGB Color Swatch Preview"
            };
            pnlColorPreview.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawRectangle(Theme.PenBorder, 0, 0, pnlColorPreview.Width - 1, pnlColorPreview.Height - 1);
            };
            gbColor.Controls.Add(pnlColorPreview);

            btnPickColor = new Button
            {
                Text = "Pick Colour...",
                Location = new Point(72, 35),
                Size = new Size(95, 28),
                BackColor = Theme.SurfaceElevated,
                ForeColor = Theme.TextPrimary,
                Font = Theme.FontBodySecondary,
                FlatStyle = FlatStyle.Flat,
                TabIndex = 2,
                AccessibleName = "Open Windows Color Picker dialog"
            };
            btnPickColor.FlatAppearance.BorderColor = Theme.Border;
            btnPickColor.FlatAppearance.BorderSize = 1;
            btnPickColor.Click += BtnPickColor_Click;
            gbColor.Controls.Add(btnPickColor);

            Label lblHex = new Label
            {
                Text = "Hex:",
                Location = new Point(178, 41),
                AutoSize = true,
                ForeColor = Theme.TextSecondary,
                Font = Theme.FontBodySecondary
            };
            txtHex = new TextBox
            {
                Text = currentProfile != null ? currentProfile.HexColor : "#BE59FF",
                Location = new Point(212, 37),
                Size = new Size(85, 24),
                BackColor = Theme.SurfaceElevated,
                ForeColor = Theme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.FontMono,
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 7,
                TabIndex = 3,
                AccessibleName = "Hex Color Code"
            };
            txtHex.Leave += TxtHex_Leave;
            txtHex.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) TxtHex_Leave(s, e); };
            gbColor.Controls.Add(lblHex);
            gbColor.Controls.Add(txtHex);

            // Quick Preset Buttons (9 Presets, 53px width each, 4px spacing)
            string[] presetNames = new string[] { "Lavender", "Purple", "Cyan", "Ice Blue", "Mint", "Green", "Gold", "Pink", "White" };
            Color[] presetColors = new Color[] {
                Color.FromArgb(190, 89, 255), Color.FromArgb(180, 0, 255), Color.FromArgb(0, 255, 255),
                Color.FromArgb(64, 165, 255), Color.FromArgb(50, 255, 150), Color.FromArgb(0, 255, 0),
                Color.FromArgb(255, 180, 0), Color.FromArgb(255, 50, 140), Color.FromArgb(255, 255, 255)
            };

            presetButtons = new Button[presetNames.Length];
            int qx = 12;
            for (int i = 0; i < presetNames.Length; i++)
            {
                Color pColor = presetColors[i];
                bool isInitSelected = (currentColor.ToArgb() == pColor.ToArgb());
                Button btnPreset = new Button
                {
                    Text = presetNames[i],
                    Location = new Point(qx, 76),
                    Size = new Size(53, 24),
                    BackColor = Theme.SurfaceElevated,
                    ForeColor = isInitSelected ? Color.White : Theme.TextSecondary,
                    Font = Theme.FontBadge,
                    FlatStyle = FlatStyle.Flat,
                    Tag = pColor,
                    TabIndex = 4 + i,
                    AccessibleName = presetNames[i] + " color preset"
                };
                btnPreset.FlatAppearance.BorderColor = isInitSelected ? Theme.Accent : Theme.Border;
                btnPreset.FlatAppearance.BorderSize = 1;
                btnPreset.Click += delegate(object s, EventArgs e)
                {
                    Button b = (Button)s;
                    OnPresetButtonClick(b, (Color)b.Tag);
                };
                presetButtons[i] = btnPreset;
                gbColor.Controls.Add(btnPreset);
                qx += 57;
            }
            body.Controls.Add(gbColor);

            // ─── Group 2: Brightness Control ───
            Panel gbBright = CreateSectionPanel(new Point(0, 123), new Size(535, 68), "2. Brightness Slider", "PWM Proportion Scaling", false);

            tbBrightness = new TrackBar
            {
                Location = new Point(12, 30),
                Size = new Size(445, 30),
                Minimum = 5,
                Maximum = 100,
                Value = currentBrightness,
                TickFrequency = 10,
                TickStyle = TickStyle.None,
                BackColor = Theme.Surface,
                TabIndex = 13,
                AccessibleName = "Brightness Percentage Slider"
            };
            tbBrightness.ValueChanged += delegate
            {
                currentBrightness = tbBrightness.Value;
                lblBrightnessVal.Text = currentBrightness.ToString() + "%";
                UpdateColorPreview();
            };
            gbBright.Controls.Add(tbBrightness);

            lblBrightnessVal = new Label
            {
                Text = currentBrightness.ToString() + "%",
                Location = new Point(462, 32),
                Size = new Size(60, 24),
                TextAlign = ContentAlignment.MiddleRight,
                Font = Theme.FontHeading,
                ForeColor = Theme.Accent
            };
            gbBright.Controls.Add(lblBrightnessVal);
            body.Controls.Add(gbBright);

            // ─── Action Buttons (Safe Decoupled Architecture) ───
            Panel pnlActions = new Panel { Location = new Point(0, 199), Size = new Size(535, 36) };

            btnApply = new Button
            {
                Text = "▶ Apply (RAM)",
                Location = new Point(0, 0),
                Size = new Size(155, 36),
                BackColor = Theme.SurfaceElevated,
                ForeColor = Theme.TextPrimary,
                Font = Theme.FontBodyBold,
                FlatStyle = FlatStyle.Flat,
                TabIndex = 14,
                AccessibleName = "Apply settings to mouse volatile RAM"
            };
            btnApply.FlatAppearance.BorderColor = Theme.Border;
            btnApply.FlatAppearance.BorderSize = 1;
            btnApply.Click += BtnApply_Click;
            pnlActions.Controls.Add(btnApply);

            btnSave = new Button
            {
                Text = "💾 Save Profile",
                Location = new Point(163, 0),
                Size = new Size(209, 36),
                BackColor = Theme.Accent,
                ForeColor = Theme.AccentText,
                Font = Theme.FontBodyBold,
                FlatStyle = FlatStyle.Flat,
                TabIndex = 15,
                AccessibleName = "Save profile and enable auto-apply persistence"
            };
            btnSave.FlatAppearance.BorderColor = Theme.AccentHover;
            btnSave.FlatAppearance.BorderSize = 1;
            btnSave.Click += BtnSave_Click;
            pnlActions.Controls.Add(btnSave);

            btnReset = new Button
            {
                Text = "↺ Reset Profile",
                Location = new Point(380, 0),
                Size = new Size(155, 36),
                BackColor = Theme.Surface,
                ForeColor = Theme.TextSecondary,
                Font = Theme.FontBodyPrimary,
                FlatStyle = FlatStyle.Flat,
                TabIndex = 16,
                AccessibleName = "Reset mouse to factory default profile"
            };
            btnReset.FlatAppearance.BorderColor = Theme.Border;
            btnReset.FlatAppearance.BorderSize = 1;
            btnReset.Click += BtnReset_Click;
            pnlActions.Controls.Add(btnReset);

            body.Controls.Add(pnlActions);

            // ─── Group 3: Persistence Mode (Instant A704 Auto-Apply) ───
            gbPersistence = CreateSectionPanel(new Point(0, 243), new Size(535, 96), "3. Persistence Mode", "Instant A704 Auto-Apply", false);

            rbStealth = new RadioButton
            {
                Text = "Stealth Driver Watcher (Invisible, No Tray Icon, ~1.8 MB RAM — Recommended)",
                Location = new Point(14, 28),
                Size = new Size(505, 20),
                ForeColor = Theme.TextPrimary,
                Font = Theme.FontBodyBold,
                Checked = currentProfile == null || currentProfile.Mode == PersistenceMode.StealthWatcher,
                TabIndex = 17
            };
            gbPersistence.Controls.Add(rbStealth);

            rbTrayAgent = new RadioButton
            {
                Text = "Tray Agent Mode (~2.7 MB RAM, quick access tray icon)",
                Location = new Point(14, 49),
                Size = new Size(505, 20),
                ForeColor = Theme.TextSecondary,
                Font = Theme.FontBodyPrimary,
                Checked = currentProfile != null && currentProfile.Mode == PersistenceMode.TrayAgent,
                TabIndex = 18
            };
            gbPersistence.Controls.Add(rbTrayAgent);

            rbDisabled = new RadioButton
            {
                Text = "Disabled (RAM only - profile resets when mouse is unplugged or PC restarts)",
                Location = new Point(14, 70),
                Size = new Size(505, 20),
                ForeColor = Theme.TextMuted,
                Font = Theme.FontBodySecondary,
                Checked = currentProfile != null && currentProfile.Mode == PersistenceMode.Disabled,
                TabIndex = 19
            };
            gbPersistence.Controls.Add(rbDisabled);
            body.Controls.Add(gbPersistence);

            // ─── Group 4: EXPERIMENTAL - DPI Controls ───
            gbDpi = CreateSectionPanel(new Point(0, 347), new Size(535, 78), "4. Experimental DPI Stages", "Cmd 0x09", true);

            cbEnableDpi = new CheckBox
            {
                Text = "Enable Experimental DPI Controls (unverified Cmd 0x09)",
                Location = new Point(14, 25),
                Size = new Size(505, 20),
                ForeColor = Theme.Warning,
                Font = Theme.FontBodyBold,
                Checked = currentProfile != null && currentProfile.EnableDpi,
                TabIndex = 20
            };
            cbEnableDpi.CheckedChanged += delegate
            {
                bool enabled = cbEnableDpi.Checked;
                cmbDpiStages.Enabled = enabled;
                btnTestDpi.Enabled = enabled;
            };
            gbDpi.Controls.Add(cbEnableDpi);

            Label lblSelectDpi = new Label
            {
                Text = "Target Stage:",
                Location = new Point(14, 48),
                Size = new Size(85, 20),
                ForeColor = Theme.TextSecondary,
                Font = Theme.FontBodySecondary
            };
            cmbDpiStages = new ComboBox
            {
                Location = new Point(102, 46),
                Size = new Size(278, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.SurfaceElevated,
                ForeColor = Theme.TextPrimary,
                Font = Theme.FontBodyPrimary,
                Enabled = currentProfile != null && currentProfile.EnableDpi,
                TabIndex = 21
            };
            cmbDpiStages.Items.AddRange(new object[] {
                "Stage 0: 600 DPI (Default Blue)",
                "Stage 1: 1000 DPI (Default Yellow)",
                "Stage 2: 1200 DPI (Default Green)",
                "Stage 3: 1600 DPI (Default Purple)",
                "Stage 4: 2400 DPI (Default Cyan)",
                "Stage 5: 3200 DPI (Default White)"
            });
            cmbDpiStages.SelectedIndex = currentProfile != null ? Math.Max(0, Math.Min(5, currentProfile.ActiveDpiStage)) : 1;
            try { SetWindowTheme(cmbDpiStages.Handle, "DarkMode_Explorer", null); } catch { }

            gbDpi.Controls.Add(lblSelectDpi);
            gbDpi.Controls.Add(cmbDpiStages);

            btnTestDpi = new Button
            {
                Text = "Test DPI (RAM)",
                Location = new Point(390, 44),
                Size = new Size(133, 26),
                BackColor = Color.FromArgb(48, 32, 12),
                ForeColor = Theme.Warning,
                Font = Theme.FontBodyBold,
                FlatStyle = FlatStyle.Flat,
                Enabled = currentProfile != null && currentProfile.EnableDpi,
                TabIndex = 22,
                AccessibleName = "Test selected DPI stage in volatile RAM"
            };
            btnTestDpi.FlatAppearance.BorderColor = Theme.WarningBorder;
            btnTestDpi.FlatAppearance.BorderSize = 1;
            btnTestDpi.Click += BtnTestDpi_Click;
            gbDpi.Controls.Add(btnTestDpi);

            body.Controls.Add(gbDpi);

            // ─── Group 5: Live Diagnostic Log Panel ───
            Panel gbLog = CreateSectionPanel(new Point(0, 433), new Size(535, 295), "5. Diagnostic USB Packet Log", "Report ID 0x07", false);

            rtbLog = new RichTextBox
            {
                Location = new Point(12, 32),
                Size = new Size(511, 224),
                ReadOnly = true,
                BackColor = Theme.TerminalBg,
                ForeColor = Theme.TerminalText,
                Font = Theme.FontMono,
                BorderStyle = BorderStyle.None,
                TabIndex = 23,
                AccessibleName = "Diagnostic USB HID packet return code log"
            };
            gbLog.Controls.Add(rtbLog);

            btnCopyLog = new Button
            {
                Text = "Copy Log",
                Location = new Point(355, 262),
                Size = new Size(80, 25),
                BackColor = Theme.SurfaceElevated,
                ForeColor = Theme.TextPrimary,
                Font = Theme.FontBodySecondary,
                FlatStyle = FlatStyle.Flat,
                TabIndex = 24,
                AccessibleName = "Copy diagnostic log to clipboard"
            };
            btnCopyLog.FlatAppearance.BorderColor = Theme.Border;
            btnCopyLog.FlatAppearance.BorderSize = 1;
            btnCopyLog.Click += delegate
            {
                if (!string.IsNullOrEmpty(rtbLog.Text))
                {
                    Clipboard.SetText(rtbLog.Text);
                    MessageBox.Show("Diagnostic log copied to clipboard!", "Log Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            gbLog.Controls.Add(btnCopyLog);

            btnClearLog = new Button
            {
                Text = "Clear Log",
                Location = new Point(443, 262),
                Size = new Size(80, 25),
                BackColor = Theme.Surface,
                ForeColor = Theme.TextSecondary,
                Font = Theme.FontBodySecondary,
                FlatStyle = FlatStyle.Flat,
                TabIndex = 25,
                AccessibleName = "Clear diagnostic log text"
            };
            btnClearLog.FlatAppearance.BorderColor = Theme.Border;
            btnClearLog.FlatAppearance.BorderSize = 1;
            btnClearLog.Click += delegate { rtbLog.Clear(); };
            gbLog.Controls.Add(btnClearLog);

            body.Controls.Add(gbLog);
            this.Controls.Add(body);
        }

        private void Log(string msg, Color col)
        {
            if (rtbLog == null) return;
            if (rtbLog.InvokeRequired)
            {
                rtbLog.Invoke(new Action<string, Color>(Log), msg, col);
                return;
            }
            if (rtbLog.TextLength > 50000) rtbLog.Clear();
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionLength = 0;
            rtbLog.SelectionColor = col;
            rtbLog.AppendText(string.Format(CultureInfo.InvariantCulture, "[{0:HH:mm:ss.fff}] {1}\n", DateTime.Now, msg));
            rtbLog.ScrollToCaret();
        }

        private void RefreshDeviceConnection()
        {
            if (this.InvokeRequired)
            {
                try { this.Invoke(new Action(RefreshDeviceConnection)); } catch { }
                return;
            }

            ushort uPage;
            cachedDevicePath = HidTransport.FindTargetDevicePath(out uPage);
            cachedUsagePage = uPage;

            if (cachedDevicePath != null)
            {
                lblStatus.Text = string.Format("● Connected: Redgear A20 (VID:18F8 PID:0FC0 UsagePage:0x{0:X4})", cachedUsagePage);
                lblStatus.ForeColor = Theme.Accent;
                Log(string.Format("Hardware detected: {0} (UsagePage 0x{1:X4})", cachedDevicePath, cachedUsagePage), Theme.Accent);
            }
            else
            {
                lblStatus.Text = "○ Device Not Found. Is USB Connected?";
                lblStatus.ForeColor = Theme.Danger;
                Log("Device search completed: Redgear A20 (18F8:0FC0) not currently enumerated.", Theme.Danger);
            }
        }

        private void UpdateColorPreview()
        {
            // Calculate scaled visual preview
            float b = currentBrightness / 100.0f;
            Color scaled = Color.FromArgb(
                (int)Math.Round(currentColor.R * b),
                (int)Math.Round(currentColor.G * b),
                (int)Math.Round(currentColor.B * b)
            );
            pnlColorPreview.BackColor = scaled;
        }

        private void BtnPickColor_Click(object sender, EventArgs e)
        {
            using (ColorDialog cd = new ColorDialog())
            {
                cd.Color = currentColor;
                cd.FullOpen = true;
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    currentColor = cd.Color;
                    txtHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", currentColor.R, currentColor.G, currentColor.B);
                    UpdateColorPreview();
                }
            }
        }

        private bool TrySyncHexColor(out string error)
        {
            error = null;
            if (txtHex == null) return true;
            string raw = txtHex.Text.Trim();
            if (!raw.StartsWith("#")) raw = "#" + raw;

            if (Regex.IsMatch(raw, "^#([0-9A-Fa-f]{6})$"))
            {
                int r = Convert.ToInt32(raw.Substring(1, 2), 16);
                int g = Convert.ToInt32(raw.Substring(3, 2), 16);
                int b = Convert.ToInt32(raw.Substring(5, 2), 16);
                currentColor = Color.FromArgb(r, g, b);
                txtHex.Text = raw.ToUpper();
                UpdateColorPreview();
                return true;
            }
            else
            {
                error = "Invalid Hex Color format. Please use #RRGGBB (e.g. #BE59FF)";
                return false;
            }
        }

        private void TxtHex_Leave(object sender, EventArgs e)
        {
            string err;
            if (!TrySyncHexColor(out err))
            {
                MessageBox.Show(err, "Format Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", currentColor.R, currentColor.G, currentColor.B);
            }
        }

        private static string TranslateWin32Error(int errCode)
        {
            switch (errCode)
            {
                case 0:
                    return "Operation succeeded.";
                case 5:
                    return "Access denied (ERROR_ACCESS_DENIED). Another process (e.g. Gaming Mouse 2.0.exe) may be holding the HID endpoint.";
                case 6:
                    return "Invalid handle (ERROR_INVALID_HANDLE). Device communication handle was closed or invalidated.";
                case 31:
                    return "A device attached to the system is not functioning (ERROR_GEN_FAILURE). Microcontroller may have stalled.";
                case 87:
                    return "The parameter is incorrect (ERROR_INVALID_PARAMETER). USB report buffer length or Report ID mismatch.";
                case 1167:
                    return "The device is not connected (ERROR_DEVICE_NOT_CONNECTED). Mouse was physically disconnected.";
                default:
                    return string.Format("System error code {0}", errCode);
            }
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

                // 5. If CommitToFlash, send Command 0x20 sync latch and verify MCU health
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
        private async Task<bool> ApplyCurrentProfileAsync(bool silent)
        {
            if (isSavingInProgress) return false;

            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                RefreshDeviceConnection();
                if (string.IsNullOrEmpty(cachedDevicePath))
                {
                    if (!silent)
                    {
                        MessageBox.Show("Redgear A20 is not connected. Connect via USB and click Refresh.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return false;
                }
            }

            byte activeDpi = (byte)Math.Max(0, Math.Min(5, currentProfile != null ? currentProfile.ActiveDpiStage : 1));
            bool sendDpi = currentProfile != null && currentProfile.EnableDpi;

            if (!silent && !this.InvokeRequired)
            {
                if (cmbDpiStages != null) activeDpi = (byte)Math.Max(0, Math.Min(5, cmbDpiStages.SelectedIndex));
                if (cbEnableDpi != null) sendDpi = cbEnableDpi.Checked;
            }

            SaveParameters p = new SaveParameters
            {
                Color = currentColor,
                Brightness = currentBrightness,
                ActiveDpiIndex = activeDpi,
                DpiMask = 0x3F,
                CommitToFlash = false, // Volatile RAM - Instant A704 registers
                SendDpi = sendDpi
            };

            string targetPath = cachedDevicePath;
            if (!silent) body.Enabled = false;
            isSavingInProgress = true;

            try
            {
                Action<string, Color> logger = silent ? (Action<string, Color>)delegate(string s, Color c) { } : new Action<string, Color>(Log);
                PipelineResult res = await Task.Run(() => ExecuteHardwarePipeline(targetPath, p, logger));
                if (res.Success)
                {
                    if (!silent)
                    {
                        Log("✅ Applied to mouse successfully! (Volatile RAM registers updated).", Color.FromArgb(140, 240, 255));
                    }
                    return true;
                }
                else
                {
                    string friendlyMsg = TranslateWin32Error(res.Win32ErrorCode);
                    Log(string.Format("❌ Apply failed at packet #{0}: {1} (Win32 {2}: {3})", res.FailingPacketIndex, res.ErrorMessage, res.Win32ErrorCode, friendlyMsg), Color.FromArgb(255, 80, 80));

                    if (res.Win32ErrorCode == 1167 || res.Win32ErrorCode == 2 || res.Win32ErrorCode == 6)
                    {
                        cachedDevicePath = null;
                        if (!silent && !this.InvokeRequired && lblStatus != null)
                        {
                            lblStatus.Text = "○ Device Not Found. Is USB Connected?";
                            lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
                        }
                    }

                    if (!silent)
                    {
                        MessageBox.Show(string.Format("Failed to apply settings at packet #{0}.\n\nError: {1}\n{2}", res.FailingPacketIndex, res.ErrorMessage, friendlyMsg), "Apply Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return false;
                }
            }
            finally
            {
                isSavingInProgress = false;
                if (!silent) body.Enabled = true;
            }
        }

        // ─── ACTION BUTTON HANDLERS ───
        private async void BtnApply_Click(object sender, EventArgs e)
        {
            if (isSavingInProgress) return;
            string err;
            if (!TrySyncHexColor(out err))
            {
                MessageBox.Show(err, "Format Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", currentColor.R, currentColor.G, currentColor.B);
                return;
            }
            await ApplyCurrentProfileAsync(silent: false);
        }

        private async void BtnSave_Click(object sender, EventArgs e)
        {
            if (isSavingInProgress) return;

            string hexErr;
            if (!TrySyncHexColor(out hexErr))
            {
                MessageBox.Show(hexErr, "Format Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", currentColor.R, currentColor.G, currentColor.B);
                return;
            }

            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                RefreshDeviceConnection();
                if (string.IsNullOrEmpty(cachedDevicePath))
                {
                    MessageBox.Show("Redgear A20 is not connected. Connect via USB and click Refresh.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (currentProfile == null) currentProfile = new AppProfile();
            currentProfile.HexColor = txtHex.Text;
            currentProfile.Brightness = currentBrightness;
            currentProfile.ActiveDpiStage = cmbDpiStages != null ? cmbDpiStages.SelectedIndex : 1;
            currentProfile.EnableDpi = cbEnableDpi != null && cbEnableDpi.Checked;

            if (rbStealth != null && rbStealth.Checked)
                currentProfile.Mode = PersistenceMode.StealthWatcher;
            else if (rbTrayAgent != null && rbTrayAgent.Checked)
                currentProfile.Mode = PersistenceMode.TrayAgent;
            else
                currentProfile.Mode = PersistenceMode.Disabled;

            string saveErr;
            if (!currentProfile.Save(out saveErr))
            {
                Log("❌ Failed to save profile to disk: " + saveErr, Color.FromArgb(255, 80, 80));
                MessageBox.Show("Failed to save profile to disk:\n\n" + saveErr, "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Clean up any legacy Task Scheduler task
            string dummy;
            TaskSchedulerHelper.RemoveTask(out dummy);

            if (currentProfile.Mode == PersistenceMode.StealthWatcher)
            {
                if (trayIcon != null) trayIcon.Visible = false;
                SetAutoStart(PersistenceMode.StealthWatcher);
                Log("🚀 Stealth Driver Watcher auto-start registered (Invisible, No Tray Icon, ~1.8 MB RAM).", Color.FromArgb(80, 230, 130));
            }
            else if (currentProfile.Mode == PersistenceMode.TrayAgent)
            {
                if (trayIcon != null) trayIcon.Visible = true;
                SetAutoStart(PersistenceMode.TrayAgent);
                Log("📌 Tray Agent startup registered in Windows Run key (~2.7 MB RAM).", Color.FromArgb(140, 200, 255));
            }
            else
            {
                if (trayIcon != null) trayIcon.Visible = false;
                SetAutoStart(PersistenceMode.Disabled);
                Log("Auto-apply disabled. Mouse will use volatile RAM only.", Color.FromArgb(200, 200, 220));
            }

            Log(string.Format("Profile saved to {0} (Mode={1}, EnableDpi={2}, DpiStage={3})",
                AppProfile.GetProfilePath(), currentProfile.Mode, currentProfile.EnableDpi, currentProfile.ActiveDpiStage),
                Color.FromArgb(140, 200, 255));

            bool ok = await ApplyCurrentProfileAsync(silent: false);
            if (ok)
            {
                string msg;
                if (currentProfile.Mode == PersistenceMode.StealthWatcher)
                {
                    msg = "Profile saved successfully!\n\n" +
                          "✔ Color & Brightness applied to mouse.\n" +
                          (currentProfile.EnableDpi ? string.Format("✔ DPI Stage {0} applied to mouse.\n", currentProfile.ActiveDpiStage) : "") +
                          "✔ Stealth Driver Watcher active:\n" +
                          "   • Completely invisible in background (~1.8 MB RAM, 0% CPU).\n" +
                          "   • No taskbar or system tray icons.\n" +
                          "   • Auto-applies your settings on reboot, wake, and USB replug on ANY port.\n\n" +
                          "👉 You can safely close this window now using the Windows Close [X] button. Your settings will persist in the background!";
                }
                else if (currentProfile.Mode == PersistenceMode.TrayAgent)
                {
                    msg = "Profile saved successfully!\n\n" +
                          "✔ Color & Brightness applied to mouse.\n" +
                          (currentProfile.EnableDpi ? string.Format("✔ DPI Stage {0} applied to mouse.\n", currentProfile.ActiveDpiStage) : "") +
                          "✔ Tray Agent Mode active: Redgear A20 Control Center will start in the system tray (~2.7 MB RAM).\n\n" +
                          "👉 You can safely close this window using the Windows Close [X] button to minimize to tray.";
                }
                else
                {
                    msg = "Profile saved locally!\n\n" +
                          "✔ Color & Brightness applied to mouse.\n\n" +
                          "ℹ Note: Auto-apply is disabled. Lighting will reset upon mouse disconnect or PC restart.";
                }

                MessageBox.Show(msg, "Profile Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            TrimMemory();
        }

        private async void BtnReset_Click(object sender, EventArgs e)
        {
            if (isSavingInProgress) return;

            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                RefreshDeviceConnection();
                if (string.IsNullOrEmpty(cachedDevicePath))
                {
                    MessageBox.Show("Redgear A20 is not connected.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            DialogResult dr = MessageBox.Show(
                "This will restore the mouse to factory defaults (stock per-stage DPI colors and breathing mode) and reset the auto-start profile.\n\nProceed?",
                "Confirm Factory Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (dr != DialogResult.Yes) return;

            string targetPath = cachedDevicePath;
            body.Enabled = false;
            isSavingInProgress = true;
            lblStatus.Text = "⏳ Restoring factory defaults... Do not disconnect mouse.";
            lblStatus.ForeColor = Color.FromArgb(255, 200, 80);

            try
            {
                PipelineResult res = await Task.Run(() => ExecuteFactoryReset(targetPath, Log));
                if (res.Success)
                {
                    currentColor = Color.FromArgb(255, 255, 0);
                    currentBrightness = 100;
                    tbBrightness.Value = 100;
                    txtHex.Text = "#FFFF00";
                    if (rbDisabled != null) rbDisabled.Checked = true;
                    if (cbEnableDpi != null) cbEnableDpi.Checked = false;
                    if (cmbDpiStages != null) cmbDpiStages.SelectedIndex = 1;
                    UpdateColorPreview();

                    if (currentProfile != null)
                    {
                        currentProfile.HexColor = "#FFFF00";
                        currentProfile.Brightness = 100;
                        currentProfile.ActiveDpiStage = 1;
                        currentProfile.EnableDpi = false;
                        currentProfile.Mode = PersistenceMode.Disabled;
                        string saveErr;
                        if (!currentProfile.Save(out saveErr))
                        {
                            Log("⚠️ Warning: Failed to save reset profile to disk: " + saveErr, Color.FromArgb(255, 120, 50));
                        }
                    }
                    SetAutoStart(PersistenceMode.Disabled);

                    string removeErr;
                    TaskSchedulerHelper.RemoveTask(out removeErr);
                    if (trayIcon != null) trayIcon.Visible = false;

                    Log("✅ Factory profile restored!", Color.FromArgb(80, 230, 130));
                    MessageBox.Show("Factory profile restored!\n\nThe mouse is now in default multi-stage breathing mode.", "Factory Reset Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    string friendlyMsg = TranslateWin32Error(res.Win32ErrorCode);
                    Log(string.Format("❌ Factory reset failed at packet #{0}: {1} (Win32 {2}: {3})", res.FailingPacketIndex, res.ErrorMessage, res.Win32ErrorCode, friendlyMsg), Color.FromArgb(255, 80, 80));
                    if (res.Win32ErrorCode == 1167 || res.Win32ErrorCode == 2 || res.Win32ErrorCode == 6)
                    {
                        cachedDevicePath = null;
                        if (lblStatus != null)
                        {
                            lblStatus.Text = "○ Device Not Found. Is USB Connected?";
                            lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
                        }
                    }
                    MessageBox.Show(string.Format("Factory reset failed at packet #{0}.\n\nError: {1}\n{2}", res.FailingPacketIndex, res.ErrorMessage, friendlyMsg), "Reset Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                isSavingInProgress = false;
                body.Enabled = true;
                if (!string.IsNullOrEmpty(cachedDevicePath) && lblStatus != null)
                {
                    lblStatus.Text = string.Format("● Connected: Redgear A20 (VID:18F8 PID:0FC0 UsagePage:0x{0:X4})", cachedUsagePage);
                    lblStatus.ForeColor = Color.FromArgb(80, 230, 130);
                }
            }
        }

        // ─── UNVERIFIED / EXPERIMENTAL SECTION: DPI TESTING ───
        private async void BtnTestDpi_Click(object sender, EventArgs e)
        {
            if (!cbEnableDpi.Checked) return;
            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                MessageBox.Show("Redgear A20 is not connected.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int selectedStage = cmbDpiStages.SelectedIndex;
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

                        ok = HidTransport.SendSinglePacket(targetPath, dpiPacket, out err);
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
                    Log(string.Format("Cmd 0x09 [Stage {0}]: {1} -> OK", selectedStage, FormatHex(dpiPacket)), Color.FromArgb(255, 200, 80));
                    Log("Test completed. Test mouse sensitivity now.", Color.FromArgb(200, 200, 220));
                }
                else
                {
                    Log(string.Format("Cmd 0x09 [Stage {0}]: {1} -> FAIL ({2})", selectedStage, FormatHex(dpiPacket), err), Color.FromArgb(255, 80, 80));
                }
            }
            finally
            {
                btnTestDpi.Enabled = true;
            }
        }

        private static string FormatHex(byte[] bytes)
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

                SaveParameters p = new SaveParameters
                {
                    Color = color,
                    Brightness = profile.Brightness,
                    ActiveDpiIndex = (byte)Math.Max(0, Math.Min(5, profile.ActiveDpiStage)),
                    DpiMask = 0x3F,
                    CommitToFlash = false,
                    SendDpi = profile.EnableDpi
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

        [STAThread]
        public static void Main(string[] args)
        {
            bool startHidden = false;
            bool isStealth = false;
            bool applyAndExit = false;

            bool installTask = false;
            bool removeTask = false;

            if (args != null)
            {
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
                appMutex = new Mutex(true, "RedgearA20ControlCenter_SingleInstance", out createdNew);
            }
            catch (AbandonedMutexException)
            {
                // Previous GUI instance terminated abruptly while holding mutex; ownership successfully acquired
                createdNew = true;
            }

            if (!createdNew)
            {
                if (!startHidden)
                {
                    PostMessage(HWND_BROADCAST, wmShowControlCenter, IntPtr.Zero, IntPtr.Zero);
                }
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(startHidden, isStealth));
            }
            finally
            {
                if (appMutex != null)
                {
                    try { appMutex.ReleaseMutex(); } catch { }
                    appMutex.Close();
                }
            }
        }
    }
}
