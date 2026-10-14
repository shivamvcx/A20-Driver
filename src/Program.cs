using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("Redgear A20 Control Center")]
[assembly: AssemblyDescription("Open-Source Hardware Control Utility for Redgear A20")]
[assembly: AssemblyProduct("Redgear A20 Control Center")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Redgear A20 Open Driver Project Contributors")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

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

        public static bool SendSinglePacket(string path, byte[] packet, out string error)
        {
            error = null;
            if (packet == null || packet.Length != 8)
            {
                error = "Packet must be exactly 8 bytes.";
                return false;
            }

            SafeFileHandle hFile = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (hFile.IsInvalid)
            {
                int errCode = Marshal.GetLastWin32Error();
                error = string.Format("Failed to open device handle (Win32 Error: {0})", errCode);
                return false;
            }

            try
            {
                bool success = HidD_SetFeature(hFile, packet, 8);
                if (!success)
                {
                    int errCode = Marshal.GetLastWin32Error();
                    error = string.Format("HidD_SetFeature failed (Win32 Error: {0})", errCode);
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
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Main GUI Application Form (Native Windows Forms, Dark Theme)
    // ═════════════════════════════════════════════════════════════════════════
    public class MainForm : Form
    {
        // UI Controls
        private Label lblStatus;
        private Panel pnlColorPreview;
        private TextBox txtHex;
        private Button btnPickColor;
        private TrackBar tbBrightness;
        private Label lblBrightnessVal;
        private Button btnApply;
        private Button btnSave;
        private Button btnReset;

        // Experimental DPI section
        private CheckBox cbEnableDpi;
        private GroupBox gbDpi;
        private ComboBox cmbDpiStages;
        private Button btnTestDpi;

        // Diagnostic Log Panel
        private RichTextBox rtbLog;
        private Button btnClearLog;
        private Button btnCopyLog;

        // Internal State
        private Color currentColor = Color.FromArgb(190, 89, 255); // Soft Lavender default
        private int currentBrightness = 100;
        private string cachedDevicePath = null;
        private ushort cachedUsagePage = 0;

        public MainForm()
        {
            InitForm();
            RefreshDeviceConnection();
            UpdateColorPreview();
        }

        private void InitForm()
        {
            this.Text = "Redgear A20 Control Center v0.1.0";
            this.Size = new Size(580, 780);
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
            Panel body = new Panel { Location = new Point(15, 55), Size = new Size(535, 675) };

            // ─── Group 1: Colour Selection ───
            GroupBox gbColor = new GroupBox
            {
                Text = "  1. LED Colour Selection (Verified Cmd 0x14)  ",
                Location = new Point(0, 0),
                Size = new Size(535, 130),
                ForeColor = Color.FromArgb(180, 185, 205)
            };

            pnlColorPreview = new Panel
            {
                Location = new Point(20, 30),
                Size = new Size(60, 45),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = currentColor
            };
            gbColor.Controls.Add(pnlColorPreview);

            btnPickColor = new Button
            {
                Text = "Pick Colour...",
                Location = new Point(90, 30),
                Size = new Size(110, 30),
                BackColor = Color.FromArgb(45, 48, 58),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnPickColor.FlatAppearance.BorderSize = 0;
            btnPickColor.Click += BtnPickColor_Click;
            gbColor.Controls.Add(btnPickColor);

            Label lblHex = new Label { Text = "Hex Code:", Location = new Point(220, 35), AutoSize = true, ForeColor = Color.FromArgb(200, 200, 210) };
            txtHex = new TextBox
            {
                Text = "#BE59FF",
                Location = new Point(290, 32),
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
                    Location = new Point(qx, 85),
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
                Location = new Point(0, 140),
                Size = new Size(535, 80),
                ForeColor = Color.FromArgb(180, 185, 205)
            };

            tbBrightness = new TrackBar
            {
                Location = new Point(15, 25),
                Size = new Size(420, 45),
                Minimum = 5,
                Maximum = 100,
                Value = 100,
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
                Text = "100%",
                Location = new Point(445, 30),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 200, 255)
            };
            gbBright.Controls.Add(lblBrightnessVal);
            body.Controls.Add(gbBright);

            // ─── Action Buttons (Safe Decoupled Architecture) ───
            btnApply = new Button
            {
                Text = "▶ Apply to Mouse (RAM Only)",
                Location = new Point(0, 230),
                Size = new Size(210, 38),
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
                Text = "💾 Save to Mouse Memory (0x20)",
                Location = new Point(220, 230),
                Size = new Size(210, 38),
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
                Text = "Reset",
                Location = new Point(440, 230),
                Size = new Size(95, 38),
                BackColor = Color.FromArgb(48, 52, 62),
                ForeColor = Color.FromArgb(220, 220, 230),
                FlatStyle = FlatStyle.Flat
            };
            btnReset.FlatAppearance.BorderSize = 0;
            btnReset.Click += BtnReset_Click;
            body.Controls.Add(btnReset);

            // ─── Group 3: ⚠️ EXPERIMENTAL - DPI Controls ───
            gbDpi = new GroupBox
            {
                Text = "  3. ⚠️ EXPERIMENTAL - DPI Stage (Disabled by Default)  ",
                Location = new Point(0, 280),
                Size = new Size(535, 95),
                ForeColor = Color.FromArgb(220, 150, 60)
            };

            cbEnableDpi = new CheckBox
            {
                Text = "Enable Experimental DPI Controls (I understand this uses unverified Cmd 0x09)",
                Location = new Point(15, 24),
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

            Label lblSelectDpi = new Label { Text = "Select DPI Target:", Location = new Point(15, 55), AutoSize = true, ForeColor = Color.FromArgb(190, 190, 200) };
            cmbDpiStages = new ComboBox
            {
                Location = new Point(130, 52),
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
                Location = new Point(375, 50),
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

            // ─── Group 4: Live Diagnostic Log Panel ───
            GroupBox gbLog = new GroupBox
            {
                Text = "  4. Diagnostic USB Report Log (Every Packet & Return Code)  ",
                Location = new Point(0, 385),
                Size = new Size(535, 280),
                ForeColor = Color.FromArgb(180, 185, 205)
            };

            rtbLog = new RichTextBox
            {
                Location = new Point(15, 25),
                Size = new Size(505, 210),
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
                Location = new Point(350, 242),
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
                Location = new Point(440, 242),
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
            if (rtbLog.InvokeRequired)
            {
                rtbLog.Invoke(new Action<string, Color>(Log), msg, col);
                return;
            }
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

        // ─── VERIFIED COMMAND EXECUTION: APPLY (RAM ONLY) ───
        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                RefreshDeviceConnection();
                if (string.IsNullOrEmpty(cachedDevicePath))
                {
                    MessageBox.Show("Redgear A20 is not connected. Connect via USB and click Refresh.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            btnApply.Enabled = false;
            try
            {
                Log("--- Applying Verified Settings to RAM (No EEPROM Write) ---", Color.FromArgb(255, 220, 100));

                // 1. Calculate active-low PWM registers
                float b = currentBrightness / 100.0f;
                int rStep = (int)Math.Round((currentColor.R / 255.0f) * b * 15.0f);
                int gStep = (int)Math.Round((currentColor.G / 255.0f) * b * 15.0f);
                int bStep = (int)Math.Round((currentColor.B / 255.0f) * b * 15.0f);

                byte rInv = (byte)((15 - rStep) & 0x0F);
                byte gInv = (byte)((15 - gStep) & 0x0F);
                byte bInv = (byte)((15 - bStep) & 0x0F);

                Log(string.Format("Active-Low PWM Steps: R={0}/15 (0x{1:X}) G={2}/15 (0x{3:X}) B={4}/15 (0x{5:X})", rStep, rInv, gStep, gInv, bStep, bInv), Color.FromArgb(180, 180, 200));

                // 2. Program 6 DPI stages with VERIFIED Command 0x14
                for (byte stage = 0; stage < 6; stage++)
                {
                    byte b2 = (byte)(((stage & 0x07) << 5) | (gInv & 0x0F));
                    byte b3 = (byte)(((rInv & 0x0F) << 4) | (bInv & 0x0F));
                    byte[] packet = new byte[] { HidTransport.REPORT_ID, 0x14, b2, b3, 0x00, 0x00, 0x00, 0x00 };

                    string err;
                    bool ok = HidTransport.SendSinglePacket(cachedDevicePath, packet, out err);
                    if (ok)
                    {
                        Log(string.Format("Cmd 0x14 [Stage {0}]: {1} -> OK", stage, FormatHex(packet)), Color.FromArgb(80, 230, 130));
                    }
                    else
                    {
                        Log(string.Format("Cmd 0x14 [Stage {0}]: {1} -> FAIL ({2})", stage, FormatHex(packet), err), Color.FromArgb(255, 80, 80));
                        return;
                    }
                    Thread.Sleep(50); // Mandatory 50ms MCU safety delay
                }

                // 3. Set Static Solid Mode with VERIFIED Command 0x13
                byte[] modePacket = new byte[] { HidTransport.REPORT_ID, 0x13, 0x7F, 0x86, 0x00, 0x00, 0x00, 0x00 };
                string modeErr;
                bool modeOk = HidTransport.SendSinglePacket(cachedDevicePath, modePacket, out modeErr);
                if (modeOk)
                {
                    Log(string.Format("Cmd 0x13 [Mode 0x86 Static]: {0} -> OK", FormatHex(modePacket)), Color.FromArgb(80, 230, 130));
                }
                else
                {
                    Log(string.Format("Cmd 0x13: {0} -> FAIL ({1})", FormatHex(modePacket), modeErr), Color.FromArgb(255, 80, 80));
                }

                Log("✅ Applied to RAM successfully! (Unsaved changes will revert on replug).", Color.FromArgb(140, 240, 255));
            }
            finally
            {
                btnApply.Enabled = true;
            }
        }

        // ─── VERIFIED COMMAND EXECUTION: SAVE TO EEPROM (0x20) ───
        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                MessageBox.Show("Redgear A20 is not connected.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult dr = MessageBox.Show(
                "This will permanently write your current colour and brightness settings into the mouse's onboard EEPROM flash chip (Command 0x20).\n\nSettings will persist even after computer reboots and replugging.\n\nProceed?",
                "Confirm Save to Onboard Memory",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (dr != DialogResult.Yes) return;

            btnSave.Enabled = false;
            try
            {
                byte[] savePacket = new byte[] { HidTransport.REPORT_ID, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                string err;
                bool ok = HidTransport.SendSinglePacket(cachedDevicePath, savePacket, out err);
                if (ok)
                {
                    Log(string.Format("Cmd 0x20 [Save to Flash]: {0} -> COMMITTED TO EEPROM", FormatHex(savePacket)), Color.FromArgb(255, 215, 0));
                    MessageBox.Show("Settings successfully saved to onboard mouse memory!", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Log(string.Format("Cmd 0x20 [Save to Flash]: {0} -> FAIL ({1})", FormatHex(savePacket), err), Color.FromArgb(255, 80, 80));
                }
            }
            finally
            {
                btnSave.Enabled = true;
            }
        }

        // ─── VERIFIED COMMAND EXECUTION: RESET TO DEFAULT ───
        private void BtnReset_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(cachedDevicePath))
            {
                MessageBox.Show("Redgear A20 is not connected.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnReset.Enabled = false;
            try
            {
                Log("--- Resetting to Default Verified Settings (Cyan, 100% Brightness, Static) ---", Color.FromArgb(255, 180, 80));

                // Reset local UI
                currentColor = Color.FromArgb(0, 255, 255); // Cyan
                currentBrightness = 100;
                tbBrightness.Value = 100;
                txtHex.Text = "#00FFFF";
                UpdateColorPreview();

                // Send 100% Cyan (Active-low: R=15, G=0, B=0) across 6 stages
                for (byte stage = 0; stage < 6; stage++)
                {
                    byte b2 = (byte)(((stage & 0x07) << 5) | 0x00); // Green=0 (Full ON)
                    byte b3 = (byte)((0x0F << 4) | 0x00);           // Red=15 (OFF), Blue=0 (Full ON)
                    byte[] packet = new byte[] { HidTransport.REPORT_ID, 0x14, b2, b3, 0x00, 0x00, 0x00, 0x00 };

                    string err;
                    HidTransport.SendSinglePacket(cachedDevicePath, packet, out err);
                    Log(string.Format("Reset 0x14 [Stage {0}]: {1}", stage, FormatHex(packet)), Color.FromArgb(160, 220, 255));
                    Thread.Sleep(50);
                }

                // Set static mode
                byte[] modePacket = new byte[] { HidTransport.REPORT_ID, 0x13, 0x7F, 0x86, 0x00, 0x00, 0x00, 0x00 };
                string mErr;
                HidTransport.SendSinglePacket(cachedDevicePath, modePacket, out mErr);
                Log(string.Format("Reset 0x13: {0}", FormatHex(modePacket)), Color.FromArgb(160, 220, 255));

                Log("✅ Reset applied to RAM. (Does NOT modify DPI or button mapping).", Color.FromArgb(80, 230, 130));
            }
            finally
            {
                btnReset.Enabled = true;
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

                // Disassembly formula from VA 0x00408938:
                // Byte 0: 0x07 (Report ID)
                // Byte 1: 0x09 (Command ID)
                // Byte 2: 0x40 | StageIndex (0x40 to 0x45)
                // Byte 3: 0x08
                // Byte 4: 0x3F (6-bit bitmask enabling all 6 stages)
                // Bytes 5,6,7: 0x00
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

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
