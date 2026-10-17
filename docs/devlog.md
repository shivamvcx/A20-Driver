# 🛠️ Engineering DevLog: Redgear A20 Open Driver Project

---

## Project Overview
* **Target Device**: Redgear A20 RGB Gaming Mouse
* **Chipset**: Instant Microelectronics OEM USB Gaming Mouse Controller
* **Hardware Identifiers**: `VID: 0x18F8`, `PID: 0x0FC0`
* **Communication Interface**: USB HID Feature Reports, Report ID `0x07` (8 Bytes), Endpoint `UsagePage 0xFF01` (Col04)
* **Status**: v0.2.0 (Verified Solid Color & Brightness in RAM, Native Manifest & Stealth Hotplug Persistence)

---

## Chronological Development Log

### Phase 1: Problem Definition & Hardware Identification
* **The Problem**: The official Redgear A20 driver software is abandoned, unstable, and fails to configure the mouse's RGB lighting to a fixed color (e.g. Pure Purple). The mouse defaults to an unchangeable slow rainbow spectrum cycle.
* **USB Enumeration**:
  - Scanning the Windows HID tree initially revealed composite interfaces.
  - The correct mouse hardware was identified as `VID: 0x18F8 & PID: 0x0FC0` ("USB GAMING MOUSE" by manufacturer "INSTANT").
  - The mouse exposes 6 HID collections. Probing showed that standard collections (`UsagePage 0x0001`) are locked by Windows `mouhid.sys` for exclusive mouse input, while vendor collection **Col04 (`UsagePage 0xFF01`, Usage `0x0001`)** accepts 8-byte Feature Reports.

---

### Phase 2: Reverse Engineering `Gaming Mouse 2.0.exe`
* Extracted the 32-bit PE binary from the vendor's InstallShield installer (`RedGear A20 RGB Gaming Mouse.[20181212].exe`).
* Disassembled the main hardware dispatch routine starting at virtual address **`VA 0x00408700 – 0x00408A50`**:
  - Buffer allocation: Global 8-byte buffer at address `0x005C1900`.
  - Report ID: Constant byte `0x07`.
  - Dispatch mechanism: Calls `HidD_SetFeature(hDevice, buffer, 8)`.

#### Discovered Command Table:
| Opcode | Routine VA | Functional Role |
| :---: | :---: | :--- |
| `0x10` | `0x00408780` | Button remapping matrix (Button ID $\rightarrow$ Action Code) |
| `0x11` | `0x00408840` | USB Polling rate ($125\text{Hz}$, $250\text{Hz}$, $500\text{Hz}$, $1000\text{Hz}$) |
| `0x13` | `0x004088B0` | LED Lighting Mode & Cycle Speed |
| `0x09` | `0x00408938` | Sensor DPI Mode & Enabled Stages Bitmask |
| `0x14` | `0x004089CE` | DPI Stage Colors (looped 6 times for stages 0–5) |
| `0x12` | `0x004089FB` | Sensitivity, Scroll lines per notch, Pointer speed |
| `0x20` | `0x004063A2` | Non-volatile Flash / EEPROM Commit |

---

### Phase 3: The Active-Low PWM Breakthrough (The "Green" Mystery)
* **Initial Test**: When testing Command `0x14` with standard RGB values for Purple ($\text{Red}=15, \text{Green}=0, \text{Blue}=15$), the mouse unexpectedly turned **100% solid bright Green**!
* **Disassembly Inspection (`VA 0x00408D07`)**:
  Examining the driver's color-parsing function revealed the hardware architecture:
  ```x86
  8B C7          MOV EAX, EDI         ; EDI = 255 (0xFF)
  0F B6 CB       MOVZX ECX, BL        ; BL = Raw Color Component
  2B C1          SUB EAX, ECX         ; EAX = 255 - Component (INVERTED!)
  C1 F8 04       SAR EAX, 4           ; (255 - Component) / 16 -> 4-bit Active-Low!
  ```
* **Discovery**: The microcontroller's internal LED drivers use **Active-Low PWM**:
  - `0x0` = $100\%$ Full Brightness (Full ON)
  - `0xF` ($15$) = $0\%$ Brightness (Full OFF)
* **The Fix**:
  - Setting $\text{Green} = 0$ had turned Green to **maximum full brightness** while turning Red and Blue off.
  - Inverting the registers ($\text{Red}=0, \text{Green}=15, \text{Blue}=0$) instantly produced **Pure Solid Purple** on the mouse!

---

### Phase 4: Static Solid vs. Breathing Mode Discovery
* During initial hardware testing, we observed that with Command `0x13` mode byte `0x80`, the LED breathed (faded in and out every 6 seconds).
* Inspecting address `VA 0x00408870 – 0x00408899`:
  - When `LED_TYPE == 0` (Fixed/DPI): Mode byte is `0x80 | (CycleTime & 0x07)` (Breathing mode).
  - When `LED_TYPE == 2` (Static):
    ```x86
    BA 06 00 00 00   MOV EDX, 6
    ...
    ADD CL, DL       ; CL = 0x80 | 6 = 0x86 !
    ```
* **Result**: Mode byte **`0x86`** forces the MCU into **Static Solid mode**, completely disabling the breathing/fading cycle.

---

### Phase 5: Input Incident Post-Mortem & Safety Architecture
* **Symptom**: During rapid testing of lighting loops, the mouse cursor moved and the scroll wheel scrolled, but Left & Right clicks stopped registering. A laptop reboot completely resolved the issue.
* **Forensic Diagnosis**:
  1. *Hardware Separation*: The optical sensor (SPI interrupt) and scroll wheel (timer/encoder pins) operate on hardware interrupts. Physical button switches are scanned in software via a GPIO debounce loop in the MCU's main firmware thread.
  2. *Buffer Saturation*: Rapidly sending back-to-back Feature Reports without inter-packet delays saturated the MCU's 8-bit endpoint buffer, starving the GPIO debounce scan loop.
  3. *Handle Stalling*: Abrupt process termination left orphaned HID endpoint buffers in Windows' `hidclass.sys`.
* **Permanent Safety Guardrails Established**:
  - **No Live Slider Spamming**: Sliders only update on-screen visual preview; zero packets sent during dragging.
  - **Single Explicit Apply**: Changes only transmit to the device on user click.
  - **Mandatory $50\text{ms}$ Delays**: Enforced between individual Feature Reports.
  - **Decoupled RAM vs EEPROM**: "Apply" only writes to volatile RAM; "Save" (`0x20`) is isolated behind a separate confirmation button.
  - **Immediate Handle Disposal**: Handle is opened, written, flushed via `HidD_FlushQueue`, and closed immediately.

---

### Phase 6: v0.1.0 Desktop Application Architecture
* Implemented a zero-dependency C# Windows Forms application (`src\Program.cs`).
* Integrated a native P/Invoke Win32 HID transport (`hid.dll` / `setupapi.dll`).
* Features implemented:
  - 12-bit Active-Low Precision Color Engine (Color dialog + `#RRGGBB` input + quick presets).
  - Proportional Brightness Dimming ($5\text{–}100\%$).
  - Separate "Apply to Mouse (RAM)" and "Save to Mouse Memory (0x20)" buttons.
  - Safe Reset button (restores verified default Cyan lighting).
  - Isolated "⚠️ EXPERIMENTAL - DPI" section (Command `0x09`) behind an opt-in checkbox.
  - Real-time Diagnostic Hex Log panel with clipboard copy.
* Automated 1-click build script (`build.bat`) utilizing the built-in Windows .NET Framework `csc.exe` compiler. Output binary size: **$\approx 25\text{ KB}$**.

---

### Phase 7: Hardware EEPROM Limitations & Stealth Driver Watcher Architecture (v0.1.1)

#### 1. The EEPROM Mystery & Forensic IC Audit
* **Symptom**: Writing Report `0x20` to the device returned Win32 `TRUE`, and the mouse acknowledged the packet without error. However, upon physically unplugging the mouse or rebooting the PC, the mouse reverted to its factory-default dynamic rainbow cycling.
* **Silicon Architecture Analysis**:
  - Detailed decompilation and forensic tracing of the vendor software (`Gaming Mouse 2.0.exe`) and datasheet analysis of the **Instant Microelectronics A704 / A704F** optical gaming mouse MCU revealed that the IC does **not** feature integrated non-volatile EEPROM/Flash storage for dynamic LED registers.
  - While factory configuration tables (DPI stage presets, default lighting modes) are hard-coded in masked ROM/One-Time Programmable (OTP) storage, custom user RGB registers reside strictly in **volatile SRAM**.
  - When the USB $5\text{V}$ rail drops to $0\text{V}$, volatile SRAM discharges instantly. The official vendor software maintained persistence by running a background tray process (`Gaming Mouse 2.0.exe`) that listened for device reconnection and reapplied the configuration.

#### 2. The Task Scheduler Event 410 Fallacy
* **Initial Concept**: The team designed a "Zero-RAM" persistence architecture that avoided running background processes by registering an automated Windows Task Scheduler task triggered by device arrival.
* **The Diagnostic Roadblock**:
  - `Microsoft-Windows-Kernel-PnP/Configuration` Event `410` was configured as the trigger.
  - In testing, unplugging and replugging the mouse into the same or different USB ports **never triggered Event 410**.
  - **Forensic Diagnosis**: Windows Event Log only records Event `410` during *initial PnP driver installation/configuration*. Normal runtime USB hotplugging, hub re-enumeration, and re-insertion events do **not** write entries to `winevt` logs to avoid disk I/O thrashing. Consequently, Task Scheduler event triggers cannot detect standard USB device arrival.

#### 3. The Stealth Driver Watcher Solution
* **Win32 Message Pump (`WM_DEVICECHANGE`)**:
  - Switched to the native Win32 device notification model. In Windows, the operating system broadcasts `WM_DEVICECHANGE` (`0x0219`) with `DBT_DEVICEARRIVAL` (`0x8000`) exclusively to top-level window handles whenever any USB peripheral is plugged in.
  - Created an invisible, message-only Win32 window loop (`CreateHandle()`, `SetVisibleCore(false)`) running in the background.
  - On `WM_DEVICECHANGE`, the background watcher performs an instantaneous VID/PID scan. If `18F8:0FC0` is detected, it retransmits the saved hardware profile (`0x07` + `0x09` + `0x20`) within $50\text{ms}$.
* **Aggressive RAM Compression (~1.8 MB)**:
  - Standard .NET WinForms applications typically consume $25\text{–}45\text{ MB}$ of RAM.
  - Implemented an aggressive working set trim via Win32 P/Invoke:
    ```csharp
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
    ```
  - After minimizing to background, the process flushes unused heap and page allocations back to the OS:
    ```csharp
    GC.Collect();
    GC.WaitForPendingFinalizers();
    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
    ```
  - Result: Active memory footprint compressed to **~1.8 MB RAM** and **0.00% CPU usage**, completely invisible with zero system tray clutter (`trayIcon.Visible = false`).
* **Inter-Process Single-Instance IPC**:
  - Registered a system-wide custom window message: `RegisterWindowMessage("RedgearA20_ShowControlCenter")`.
  - Configured `ChangeWindowMessageFilter` to bypass User Interface Privilege Isolation (UIPI), ensuring standard and elevated instances can reliably signal the background watcher.
  - When the user launches Redgear A20 Control Center a second time from Desktop or Start Menu, the new instance detects the existing mutex, broadcasts the wake message, and exits immediately. The running watcher restores its GUI, displays the settings, and allows full interaction.
* **Field Verification**:
  - Verified across physical USB disconnects/reconnects across all USB 2.0 / 3.0 / Type-C hubs.
  - Verified across system reboots, hibernate, and fast-startup wake cycles via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
  - Verified 100% reliable settings persistence across physical USB reconnects, Modern Standby / sleep cycles, and system cold reboots.

---

### Phase 8: UI Overhaul, Modern Windows Integration & Production-Grade Hardening (v0.2.0)

#### 1. From Raw WinForms to Industrial Precision Utility
* **The Aesthetic Transformation**:
  - The v0.1.0 and v0.1.1 interfaces relied on default Windows Forms GroupBoxes and ad-hoc color styling, which gave the tool a prototype-grade appearance.
  - Formulated a comprehensive Design System specification (`DESIGN.md`) rooted in an "Industrial Precision Utility" philosophy:
    - Structured dark palette: `#121316` Canvas, `#1A1C22` Surface cards, `#242731` elevated inputs, `#2B2F3A` hairline borders.
    - Single functional primary accent: Emerald / Hardware Mint (`#10B981`) highlighting actionable states.
    - Flat 1px card containers (`CreateSectionPanel`) with bold section headings and metadata badges.
    - Active preset border highlighting (`OnPresetButtonClick`) giving immediate visual confirmation of the active lighting color.
    - Unified 36px action row aligning `Apply (RAM)`, `Save Profile`, and `Reset Profile` according to action severity and hierarchy.
* **Zero-Allocation GDI+ Rendering Engine**:
  - WinForms applications often experience micro-stutter when custom paint handlers dynamically instantiate pens and brushes.
  - Implemented the static `Theme` token engine with pre-cached GDI+ brushes, pens, and system fonts (`Segoe UI` and `Consolas`), guaranteeing zero heap allocation and zero GC pressure during paint cycles.

#### 2. Native Modern Windows OS Integration (DWM & Manifest)
* **Immersive Dark Title Bar (`dwmapi.dll`)**:
  - Windows Forms does not automatically style non-client window frames (title bars and borders) in dark mode.
  - Implemented `DwmSetWindowAttribute` P/Invoke targeting `DWMWA_USE_IMMERSIVE_DARK_MODE` (attribute 20 and fallback attribute 19). Windows now paints the native title bar in true dark gray/black matching the dark canvas.
  - Applied `uxtheme.dll` (`SetWindowTheme`) with `"DarkMode_Explorer"` to ensure native combo boxes conform to dark styling.
* **Per-Monitor V2 High-DPI Awareness (`src/app.manifest`)**:
  - On high-resolution displays (4K monitors, high-DPI laptops), unmanifested WinForms apps suffer from OS bitmap stretching resulting in blurry text and blurry borders.
  - Authored a dedicated native Windows application manifest declaring `PerMonitorV2, PerMonitor` DPI awareness and binding to `Microsoft.Windows.Common-Controls` v6.0.0.0.
  - Configured `AutoScaleMode = AutoScaleMode.Dpi`, ensuring razor-sharp rendering at 100%, 125%, 150%, and 200% Windows desktop scaling.

#### 3. Concurrency, Storage Safety & Fault Tolerance
* **Asynchronous Hardware Mutex Dispatch**:
  - In earlier versions, clicking "Test DPI" acquired the hardware synchronization mutex synchronously on the UI thread, causing potential GUI freezes if the device communication encountered latency.
  - Converted the test handler into an asynchronous background worker (`Task.Run` with `async`/`await`), safely isolating mutex acquisition and packet dispatch away from the message pump.
* **Atomic Profile Serialization**:
  - Replaced direct `File.WriteAllText` with a multi-stage atomic commit mechanism: profile data is written to a temporary file (`.tmp`) with UTF-8 encoding and then committed atomically via `File.Replace` (with fallback to `File.Move`).
  - Completely prevents corrupted or zero-byte JSON files if a user powers off or the system reboots while saving.
* **Graceful Disconnect Recovery & Hotplug Re-Entrancy Guard**:
  - Intercepted Win32 error codes `1167` (`ERROR_DEVICE_NOT_CONNECTED`), `2`, and `6` during hardware transmissions. The driver immediately invalidates cached device handles and transitions the UI into a clear, red-tinted disconnected alert state.
  - Introduced `isHotplugHandling` re-entrancy protection inside `HandleUsbHotplugDebounced` to serialize overlapping device notifications during rapid USB replugging.
* **Global Crash Diagnostics**:
  - Registered `Application.ThreadException` and `AppDomain.CurrentDomain.UnhandledException` in `Main()`, ensuring any unexpected error is written to `%LocalAppData%\RedgearA20\crash.log` with complete stack traces and diagnostic timestamps rather than crashing silently.
