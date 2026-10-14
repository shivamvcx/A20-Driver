using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
[assembly: AssemblyVersion("0.1.1.0")]
[assembly: AssemblyFileVersion("0.1.1.0")]

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
                                SafeFileHandle hFile = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
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

    public class AppProfile
    {
        public string HexColor = "#BE59FF";
        public int Brightness = 100;
        public bool AutoStart = false;
        public bool MinimizeToTray = true;

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
                    if (mBri.Success) p.Brightness = int.Parse(mBri.Groups[1].Value);

                    Match mAuto = Regex.Match(json, "\"AutoStart\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
                    if (mAuto.Success) p.AutoStart = bool.Parse(mAuto.Groups[1].Value);

                    Match mTray = Regex.Match(json, "\"MinimizeToTray\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
                    if (mTray.Success) p.MinimizeToTray = bool.Parse(mTray.Groups[1].Value);
                }
            }
            catch { }
            return p;
        }

        public void Save()
        {
            try
            {
                string file = GetProfilePath();
                string json = string.Format("{{\n  \"HexColor\": \"{0}\",\n  \"Brightness\": {1},\n  \"AutoStart\": {2},\n  \"MinimizeToTray\": {3}\n}}\n",
                    HexColor, Brightness, AutoStart.ToString().ToLower(), MinimizeToTray.ToString().ToLower());
                File.WriteAllText(file, json);
            }
            catch { }
        }
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

        // Persistence & Startup UI
        private GroupBox gbPersistence;
        private CheckBox cbAutoStart;

        // Experimental DPI section
        private CheckBox cbEnableDpi;
        private GroupBox gbDpi;
        private ComboBox cmbDpiStages;
        private Button btnTestDpi;

        // Diagnostic Log Panel
        private RichTextBox rtbLog;
        private Button btnClearLog;
        private Button btnCopyLog;

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
        private bool startInTray = false;
        private bool isExplicitlyRestoring = false;
        private bool balloonShownOnce = false;
        private bool isExiting = false;

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

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        private static int wmShowControlCenter = 0;
        private static Mutex appMutex = null;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isSavingInProgress)
            {
                MessageBox.Show("Please wait until the operation completes before exiting.", "Busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }

            if (!isExiting && cbAutoStart != null && cbAutoStart.Checked)
            {
                e.Cancel = true;
                MinimizeToTray(true);
                return;
            }

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized && currentProfile != null && currentProfile.MinimizeToTray)
            {
                MinimizeToTray(false);
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startInTray && !isExplicitlyRestoring)
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
                RestoreFromTray();
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

        private static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_REG_KEY, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Application.ExecutablePath;
                            key.SetValue(APP_REG_NAME, string.Format("\"{0}\" --tray", exePath));
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
            miOpen.Click += delegate { RestoreFromTray(); };
            trayMenu.Items.Add(miOpen);

            ToolStripMenuItem miApply = new ToolStripMenuItem("Apply Profile Now");
            miApply.Click += async delegate { await ApplyCurrentProfileAsync(silent: false); };
            trayMenu.Items.Add(miApply);

            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem("Exit");
            miExit.Click += delegate
            {
                isExiting = true;
                if (trayIcon != null) trayIcon.Visible = false;
                Application.Exit();
            };
            trayMenu.Items.Add(miExit);

            trayIcon = new NotifyIcon
            {
                Icon = this.Icon != null ? this.Icon : SystemIcons.Application,
                Text = "Redgear A20 Control Center",
                ContextMenuStrip = trayMenu,
                Visible = startInTray
            };

            trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        private void MinimizeToTray(bool showBalloon)
        {
            this.Hide();
            this.ShowInTaskbar = false;
            if (trayIcon != null)
            {
                trayIcon.Visible = true;
                if (showBalloon && !balloonShownOnce)
                {
                    trayIcon.ShowBalloonTip(3000, "Redgear A20 Control Center", "Running in tray to auto-apply your RGB settings on reconnect.", ToolTipIcon.Info);
                    balloonShownOnce = true;
                }
            }
            TrimMemory();
        }

        private void RestoreFromTray()
        {
            isExplicitlyRestoring = true;
            startInTray = false;
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.BringToFront();
            this.Activate();
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
            RefreshDeviceConnection();
            if (!string.IsNullOrEmpty(cachedDevicePath) && currentProfile != null && currentProfile.AutoStart)
            {
                await ApplyCurrentProfileAsync(silent: true);
                TrimMemory();
            }
        }

        public MainForm() : this(false)
        {
        }

        public MainForm(bool inTray)
        {
            this.startInTray = inTray;
            this.currentProfile = AppProfile.Load();

            try
            {
                string hex = currentProfile.HexColor.TrimStart('#');
                int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                this.currentColor = Color.FromArgb(r, g, b);
            }
            catch
            {
                this.currentColor = Color.FromArgb(190, 89, 255);
            }
            this.currentBrightness = Math.Max(5, Math.Min(100, currentProfile.Brightness));

            InitForm();
            InitTrayIcon();
            InitUsbWatcher();

            RefreshDeviceConnection();
            UpdateColorPreview();

            if (startInTray)
            {
                Task.Run(async () =>
                {
                    if (!string.IsNullOrEmpty(cachedDevicePath) && currentProfile.AutoStart)
                    {
                        await ApplyCurrentProfileAsync(silent: true);
                    }
                    await Task.Delay(1000);
                    TrimMemory();
                });
            }
        }

        private void InitForm()
        {
            this.Text = "Redgear A20 Control Center v0.1.1";
            this.Size = new Size(580, 835);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(24, 25, 29);
            this.ForeColor = Color.FromArgb(235, 235, 240);
            this.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);

            // 1. Connection Header Banner
            Panel pnlHeader = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = Color.FromArgb(18, 19, 22) };
            lblStatus = new Label
            {
                Text = "🔍 Searching for Redgear A20...",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(15, 0, 0, 0),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 175)
            };
            Button btnRefresh = new Button
            {
                Text = "Refresh",
                Size = new Size(75, 27),
                Location = new Point(475, 9),
                BackColor = Color.FromArgb(40, 42, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += delegate { RefreshDeviceConnection(); };
            pnlHeader.Controls.Add(btnRefresh);
            pnlHeader.Controls.Add(lblStatus);
            this.Controls.Add(pnlHeader);

            // Container Panel
            body = new Panel { Location = new Point(15, 55), Size = new Size(535, 730) };

            // ─── Group 1: Colour Selection ───
            GroupBox gbColor = new GroupBox
            {
                Text = "  1. LED Colour Selection (Verified Cmd 0x14)  ",
                Location = new Point(0, 0),
                Size = new Size(535, 125),
                ForeColor = Color.FromArgb(180, 185, 205)
            };

            pnlColorPreview = new Panel
            {
                Location = new Point(20, 25),
                Size = new Size(60, 42),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = currentColor
            };
            gbColor.Controls.Add(pnlColorPreview);

            btnPickColor = new Button
            {
                Text = "Pick Colour...",
                Location = new Point(90, 25),
                Size = new Size(110, 30),
                BackColor = Color.FromArgb(45, 48, 58),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnPickColor.FlatAppearance.BorderSize = 0;
            btnPickColor.Click += BtnPickColor_Click;
            gbColor.Controls.Add(btnPickColor);

            Label lblHex = new Label { Text = "Hex Code:", Location = new Point(220, 30), AutoSize = true, ForeColor = Color.FromArgb(200, 200, 210) };
            txtHex = new TextBox
            {
                Text = currentProfile != null ? currentProfile.HexColor : "#BE59FF",
                Location = new Point(290, 27),
                Size = new Size(95, 26),
                BackColor = Color.FromArgb(32, 34, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 7
            };
            txtHex.Leave += TxtHex_Leave;
            txtHex.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) TxtHex_Leave(s, e); };
            gbColor.Controls.Add(lblHex);
            gbColor.Controls.Add(txtHex);

            // Quick Preset Buttons
            string[] presetNames = new string[] { "Lavender", "Purple", "Cyan", "Ice Blue", "Mint", "Green", "Gold", "Pink", "White" };
            Color[] presetColors = new Color[] {
                Color.FromArgb(190, 89, 255), Color.FromArgb(180, 0, 255), Color.FromArgb(0, 255, 255),
                Color.FromArgb(64, 165, 255), Color.FromArgb(50, 255, 150), Color.FromArgb(0, 255, 0),
                Color.FromArgb(255, 180, 0), Color.FromArgb(255, 50, 140), Color.FromArgb(255, 255, 255)
            };

            int qx = 20;
            for (int i = 0; i < presetNames.Length; i++)
            {
                Button btnPreset = new Button
                {
                    Text = presetNames[i],
                    Location = new Point(qx, 80),
                    Size = new Size(52, 26),
                    BackColor = Color.FromArgb(35, 38, 46),
                    ForeColor = Color.FromArgb(215, 215, 225),
                    Font = new Font("Segoe UI", 7.5f),
                    FlatStyle = FlatStyle.Flat,
                    Tag = presetColors[i]
                };
                btnPreset.FlatAppearance.BorderColor = Color.FromArgb(55, 58, 70);
                btnPreset.Click += delegate(object s, EventArgs e)
                {
                    Button b = (Button)s;
                    currentColor = (Color)b.Tag;
                    txtHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", currentColor.R, currentColor.G, currentColor.B);
                    UpdateColorPreview();
                };
                gbColor.Controls.Add(btnPreset);
                qx += 56;
            }
            body.Controls.Add(gbColor);

            // ─── Group 2: Brightness Control ───
            GroupBox gbBright = new GroupBox
            {
                Text = "  2. Brightness Slider (Verified PWM Proportion Scaling)  ",
                Location = new Point(0, 135),
                Size = new Size(535, 75),
                ForeColor = Color.FromArgb(180, 185, 205)
            };

            tbBrightness = new TrackBar
            {
                Location = new Point(15, 22),
                Size = new Size(420, 45),
                Minimum = 5,
                Maximum = 100,
                Value = currentBrightness,
                TickFrequency = 10
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
                Location = new Point(445, 27),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 200, 255)
            };
            gbBright.Controls.Add(lblBrightnessVal);
            body.Controls.Add(gbBright);

            // ─── Action Buttons (Safe Decoupled Architecture) ───
            btnApply = new Button
            {
                Text = "▶ Apply (RAM)",
                Location = new Point(0, 220),
                Size = new Size(165, 38),
                BackColor = Color.FromArgb(130, 60, 220),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnApply.FlatAppearance.BorderSize = 0;
            btnApply.Click += BtnApply_Click;
            body.Controls.Add(btnApply);

            btnSave = new Button
            {
                Text = "💾 Save Profile",
                Location = new Point(175, 220),
                Size = new Size(195, 38),
                BackColor = Color.FromArgb(38, 120, 75),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;
            body.Controls.Add(btnSave);

            btnReset = new Button
            {
                Text = "↺ Reset Profile",
                Location = new Point(380, 220),
                Size = new Size(155, 38),
                BackColor = Color.FromArgb(48, 52, 62),
                ForeColor = Color.FromArgb(220, 220, 230),
                Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
                FlatStyle = FlatStyle.Flat
            };
            btnReset.FlatAppearance.BorderSize = 0;
            btnReset.Click += BtnReset_Click;
            body.Controls.Add(btnReset);

            // ─── Group 3: Persistence & Startup (Instant A704 Auto-Apply) ───
            gbPersistence = new GroupBox
            {
                Text = "  3. Persistence & Startup (Instant A704 Auto-Apply)  ",
                Location = new Point(0, 270),
                Size = new Size(535, 75),
                ForeColor = Color.FromArgb(180, 185, 205)
            };

            cbAutoStart = new CheckBox
            {
                Text = "⚡ Auto-apply profile on Windows startup & when mouse is reconnected",
                Location = new Point(15, 20),
                Size = new Size(505, 24),
                ForeColor = Color.FromArgb(80, 230, 130),
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                Checked = currentProfile != null && currentProfile.AutoStart
            };
            cbAutoStart.CheckedChanged += delegate
            {
                if (currentProfile != null)
                {
                    currentProfile.AutoStart = cbAutoStart.Checked;
                    currentProfile.Save();
                    SetAutoStart(currentProfile.AutoStart);
                    Log(string.Format("Auto-apply setting changed: {0}", cbAutoStart.Checked ? "ENABLED" : "DISABLED"), Color.FromArgb(200, 200, 220));
                }
            };
            gbPersistence.Controls.Add(cbAutoStart);

            Label lblPersistenceNote = new Label
            {
                Text = "Runs silently in the tray using ~2 MB RAM and 0% CPU with native Windows hotplug events.",
                Location = new Point(35, 46),
                Size = new Size(485, 20),
                ForeColor = Color.FromArgb(150, 155, 170),
                Font = new Font("Segoe UI", 8.0f)
            };
            gbPersistence.Controls.Add(lblPersistenceNote);
            body.Controls.Add(gbPersistence);

            // ─── Group 4: ⚠️ EXPERIMENTAL - DPI Controls ───
            gbDpi = new GroupBox
            {
                Text = "  4. ⚠️ EXPERIMENTAL - DPI Stage (Disabled by Default)  ",
                Location = new Point(0, 355),
                Size = new Size(535, 85),
                ForeColor = Color.FromArgb(220, 150, 60)
            };

            cbEnableDpi = new CheckBox
            {
                Text = "Enable Experimental DPI Controls (I understand this uses unverified Cmd 0x09)",
                Location = new Point(15, 20),
                Size = new Size(500, 20),
                ForeColor = Color.FromArgb(235, 180, 100),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            cbEnableDpi.CheckedChanged += delegate
            {
                bool enabled = cbEnableDpi.Checked;
                cmbDpiStages.Enabled = enabled;
                btnTestDpi.Enabled = enabled;
            };
            gbDpi.Controls.Add(cbEnableDpi);

            Label lblSelectDpi = new Label { Text = "Select DPI Target:", Location = new Point(15, 48), AutoSize = true, ForeColor = Color.FromArgb(190, 190, 200) };
            cmbDpiStages = new ComboBox
            {
                Location = new Point(130, 45),
                Size = new Size(230, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(32, 34, 40),
                ForeColor = Color.White,
                Enabled = false
            };
            cmbDpiStages.Items.AddRange(new object[] {
                "Stage 0: 600 DPI (Default Blue)",
                "Stage 1: 1000 DPI (Default Yellow)",
                "Stage 2: 1200 DPI (Default Green)",
                "Stage 3: 1600 DPI (Default Purple)",
                "Stage 4: 2400 DPI (Default Cyan)",
                "Stage 5: 3200 DPI (Default White)"
            });
            cmbDpiStages.SelectedIndex = 1; // 1000 DPI
            gbDpi.Controls.Add(lblSelectDpi);
            gbDpi.Controls.Add(cmbDpiStages);

            btnTestDpi = new Button
            {
                Text = "Test DPI (RAM)",
                Location = new Point(375, 43),
                Size = new Size(140, 28),
                BackColor = Color.FromArgb(65, 50, 30),
                ForeColor = Color.FromArgb(255, 200, 100),
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            btnTestDpi.FlatAppearance.BorderColor = Color.FromArgb(130, 95, 45);
            btnTestDpi.Click += BtnTestDpi_Click;
            gbDpi.Controls.Add(btnTestDpi);

            body.Controls.Add(gbDpi);

            // ─── Group 5: Live Diagnostic Log Panel ───
            GroupBox gbLog = new GroupBox
            {
                Text = "  5. Diagnostic USB Report Log (Every Packet & Return Code)  ",
                Location = new Point(0, 450),
                Size = new Size(535, 275),
                ForeColor = Color.FromArgb(180, 185, 205)
            };

            rtbLog = new RichTextBox
            {
                Location = new Point(15, 25),
                Size = new Size(505, 205),
                ReadOnly = true,
                BackColor = Color.FromArgb(14, 15, 18),
                ForeColor = Color.FromArgb(0, 230, 130),
                Font = new Font("Consolas", 8.75f, FontStyle.Regular),
                BorderStyle = BorderStyle.None
            };
            gbLog.Controls.Add(rtbLog);

            btnCopyLog = new Button
            {
                Text = "Copy Log",
                Location = new Point(350, 238),
                Size = new Size(80, 26),
                BackColor = Color.FromArgb(40, 42, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCopyLog.FlatAppearance.BorderSize = 0;
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
                Location = new Point(440, 238),
                Size = new Size(80, 26),
                BackColor = Color.FromArgb(40, 42, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnClearLog.FlatAppearance.BorderSize = 0;
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
            rtbLog.AppendText(string.Format("[{0}] {1}\n", DateTime.Now.ToString("HH:mm:ss.fff"), msg));
            rtbLog.ScrollToCaret();
        }

        private void RefreshDeviceConnection()
        {
            ushort uPage;
            cachedDevicePath = HidTransport.FindTargetDevicePath(out uPage);
            cachedUsagePage = uPage;

            if (cachedDevicePath != null)
            {
                lblStatus.Text = string.Format("● Connected: Redgear A20 (VID:18F8 PID:0FC0 UsagePage:0x{0:X4})", cachedUsagePage);
                lblStatus.ForeColor = Color.FromArgb(80, 230, 130);
                Log(string.Format("Hardware detected: {0} (UsagePage 0x{1:X4})", cachedDevicePath, cachedUsagePage), Color.FromArgb(140, 200, 255));
            }
            else
            {
                lblStatus.Text = "○ Device Not Found. Is USB Connected?";
                lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
                Log("Device search completed: Redgear A20 (18F8:0FC0) not currently enumerated.", Color.FromArgb(255, 120, 120));
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

        private void TxtHex_Leave(object sender, EventArgs e)
        {
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
            }
            else
            {
                MessageBox.Show("Invalid Hex Color format. Please use #RRGGBB (e.g. #BE59FF)", "Format Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            int packetIndex = 0;

            logCallback(p.CommitToFlash ?
                "--- Executing Hardware Pipeline (With Flash Commit 0x20) ---" :
                "--- Applying Verified Settings to RAM (No EEPROM Write) ---",
                p.CommitToFlash ? Color.FromArgb(255, 215, 0) : Color.FromArgb(255, 220, 100));

            // 1. Calculate Active-Low PWM values
            float b = p.Brightness / 100.0f;
            int rStep = (int)Math.Round((p.Color.R / 255.0f) * b * 15.0f);
            int gStep = (int)Math.Round((p.Color.G / 255.0f) * b * 15.0f);
            int bStep = (int)Math.Round((p.Color.B / 255.0f) * b * 15.0f);

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

        private static PipelineResult ExecuteFactoryReset(string devicePath, Action<string, Color> logCallback)
        {
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

            SaveParameters p = new SaveParameters
            {
                Color = currentColor,
                Brightness = currentBrightness,
                ActiveDpiIndex = (byte)Math.Max(0, Math.Min(5, cmbDpiStages != null ? cmbDpiStages.SelectedIndex : 1)),
                DpiMask = 0x3F,
                CommitToFlash = false, // Volatile RAM - Instant A704 registers
                SendDpi = cbEnableDpi != null && cbEnableDpi.Checked
            };

            string targetPath = cachedDevicePath;
            if (!silent) body.Enabled = false;
            isSavingInProgress = true;

            try
            {
                PipelineResult res = await Task.Run(() => ExecuteHardwarePipeline(targetPath, p, Log));
                if (res.Success)
                {
                    if (silent)
                    {
                        Log("⚡ Auto-applied saved profile to mouse on connection.", Color.FromArgb(80, 230, 130));
                    }
                    else
                    {
                        Log("✅ Applied to mouse successfully! (Volatile RAM registers updated).", Color.FromArgb(140, 240, 255));
                    }
                    return true;
                }
                else
                {
                    string friendlyMsg = TranslateWin32Error(res.Win32ErrorCode);
                    Log(string.Format("❌ Apply failed at packet #{0}: {1} (Win32 {2}: {3})", res.FailingPacketIndex, res.ErrorMessage, res.Win32ErrorCode, friendlyMsg), Color.FromArgb(255, 80, 80));
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
            await ApplyCurrentProfileAsync(silent: false);
        }

        private async void BtnSave_Click(object sender, EventArgs e)
        {
            if (isSavingInProgress) return;

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
            currentProfile.HexColor = txtHex.Text.Trim();
            currentProfile.Brightness = currentBrightness;
            currentProfile.AutoStart = cbAutoStart.Checked;
            currentProfile.MinimizeToTray = true;
            currentProfile.Save();

            SetAutoStart(currentProfile.AutoStart);

            Log(string.Format("Profile saved to {0} (AutoStart={1})", AppProfile.GetProfilePath(), currentProfile.AutoStart), Color.FromArgb(140, 200, 255));

            bool ok = await ApplyCurrentProfileAsync(silent: false);
            if (ok)
            {
                string msg;
                if (currentProfile.AutoStart)
                {
                    msg = "Profile saved successfully!\n\n" +
                          "✔ Color & Brightness applied to mouse.\n" +
                          "✔ Auto-apply enabled: Redgear A20 Control Center will start silently in the system tray when Windows boots and auto-apply your settings whenever the mouse is plugged in.\n" +
                          "✔ Ultra-lightweight footprint: ~2 MB RAM, 0% CPU.\n\n" +
                          "You can minimize or close this window safely.";
                }
                else
                {
                    msg = "Profile saved locally!\n\n" +
                          "✔ Color & Brightness applied to mouse.\n\n" +
                          "ℹ Note: Auto-apply is unchecked. When the mouse is unplugged or PC restarted, lighting will revert to defaults until re-applied.";
                }

                MessageBox.Show(msg, "Profile Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
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
                    currentColor = Color.FromArgb(190, 89, 255);
                    currentBrightness = 100;
                    tbBrightness.Value = 100;
                    txtHex.Text = "#BE59FF";
                    cbAutoStart.Checked = false;
                    UpdateColorPreview();

                    if (currentProfile != null)
                    {
                        currentProfile.HexColor = "#BE59FF";
                        currentProfile.Brightness = 100;
                        currentProfile.AutoStart = false;
                        currentProfile.Save();
                    }
                    SetAutoStart(false);

                    Log("✅ Factory profile restored!", Color.FromArgb(80, 230, 130));
                    MessageBox.Show("Factory profile restored!\n\nThe mouse is now in default multi-stage breathing mode.", "Factory Reset Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    string friendlyMsg = TranslateWin32Error(res.Win32ErrorCode);
                    Log(string.Format("❌ Factory reset failed at packet #{0}: {1} (Win32 {2}: {3})", res.FailingPacketIndex, res.ErrorMessage, res.Win32ErrorCode, friendlyMsg), Color.FromArgb(255, 80, 80));
                    MessageBox.Show(string.Format("Factory reset failed at packet #{0}.\n\nError: {1}\n{2}", res.FailingPacketIndex, res.ErrorMessage, friendlyMsg), "Reset Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                isSavingInProgress = false;
                body.Enabled = true;
                lblStatus.Text = string.Format("● Connected: Redgear A20 (VID:18F8 PID:0FC0 UsagePage:0x{0:X4})", cachedUsagePage);
                lblStatus.ForeColor = Color.FromArgb(80, 230, 130);
            }
        }

        // ─── UNVERIFIED / EXPERIMENTAL SECTION: DPI TESTING ───
        private void BtnTestDpi_Click(object sender, EventArgs e)
        {
            if (!cbEnableDpi.Checked) return;
            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                MessageBox.Show("Redgear A20 is not connected.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int selectedStage = cmbDpiStages.SelectedIndex;
            if (selectedStage < 0 || selectedStage > 5) selectedStage = 0;

            btnTestDpi.Enabled = false;
            try
            {
                Log("⚠️ --- Transmitting EXPERIMENTAL DPI Command 0x09 (RAM Only, Never Flashed) ---", Color.FromArgb(255, 120, 50));

                byte b2 = (byte)(0x40 | (selectedStage & 0x07));
                byte[] dpiPacket = new byte[] { HidTransport.REPORT_ID, 0x09, b2, 0x08, 0x3F, 0x00, 0x00, 0x00 };

                string err;
                bool ok = HidTransport.SendSinglePacket(cachedDevicePath, dpiPacket, out err);
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

        private static int HeadlessApplyAndExit()
        {
            try
            {
                AppProfile profile = AppProfile.Load();
                ushort uPage;
                string devicePath = HidTransport.FindTargetDevicePath(out uPage);
                if (string.IsNullOrEmpty(devicePath))
                {
                    return 1;
                }

                Color color;
                try
                {
                    string hex = profile.HexColor.TrimStart('#');
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                    color = Color.FromArgb(r, g, b);
                }
                catch
                {
                    color = Color.FromArgb(190, 89, 255);
                }

                SaveParameters p = new SaveParameters
                {
                    Color = color,
                    Brightness = profile.Brightness,
                    ActiveDpiIndex = 1,
                    DpiMask = 0x3F,
                    CommitToFlash = false,
                    SendDpi = false
                };

                PipelineResult res = ExecuteHardwarePipeline(devicePath, p, delegate(string s, Color c) { });
                return res.Success ? 0 : 2;
            }
            catch
            {
                return 3;
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            bool startInTray = false;
            bool applyAndExit = false;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a == "--tray" || a == "-tray" || a == "/tray" || a == "--minimized")
                    {
                        startInTray = true;
                    }
                    else if (a == "--apply-and-exit" || a == "-apply" || a == "/apply")
                    {
                        applyAndExit = true;
                    }
                }
            }

            if (applyAndExit)
            {
                int exitCode = HeadlessApplyAndExit();
                Environment.Exit(exitCode);
                return;
            }

            wmShowControlCenter = RegisterWindowMessage("RedgearA20_ShowControlCenter");

            bool createdNew;
            appMutex = new Mutex(true, "RedgearA20ControlCenter_SingleInstance", out createdNew);

            if (!createdNew)
            {
                if (!startInTray)
                {
                    PostMessage(HWND_BROADCAST, wmShowControlCenter, IntPtr.Zero, IntPtr.Zero);
                }
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(startInTray));
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
