# 📝 Road to v0.2.0: Complete Changelog & Changes Made After v0.1.1 Launch

> **Repository**: `Redgear A20 Custom Driver & Control Center`  
> **Base Release**: `v0.1.1` (Commit `cb5915b` — Thu Oct 15 04:50:38 2026)  
> **Target Release**: `v0.2.0`  
> **Branch**: `ui-hardening`  
> **Current Status**: Complete UI Modernization, Production Hardening & Native Windows Integration  

---

## Executive Summary

Following the launch of **v0.1.1** (which introduced the near-zero RAM Stealth Driver Watcher, instant USB hotplug persistence via `WM_DEVICECHANGE`, and experimental DPI stage switching), work shifted toward **production-grade hardening, UI/UX modernization, system resilience, and long-term architectural governance**.

Key milestones completed after v0.1.1:
1. **Full GUI Visual & Structural Overhaul**: Transformed the WinForms interface from standard generic controls into an engineered **Industrial Precision Utility** with 1px hairline framing, an Emerald (`#10B981`) functional accent, pre-cached GDI+ brushes, and Per-Monitor DPI scaling.
2. **Native Windows 10/11 Dark Title Bar**: Integrated `dwmapi.dll` P/Invoke to enforce immersive dark title bars and borders on modern Windows versions.
3. **Robust Hardware & Thread Safety**: Asynchronous background mutex dispatch for experimental DPI testing, non-destructive device path enumeration, auto-invalidation of stale device handles upon disconnect, and hotplug re-entrancy locking.
4. **Resilient Data Storage & Crash Diagnostics**: Implemented atomic temporary-file profile saving (`File.Replace`), input sanitization (`TrySyncHexColor`), and global unhandled exception trapping logged to `%LocalAppData%\RedgearA20\crash.log`.
5. **Modern Build & Manifest Pipeline**: Added native `src/app.manifest` (Per-Monitor V2 DPI awareness, Windows Common Controls v6) and enhanced `build.bat` with dual-architecture compiler discovery and explicit exit codes.
6. **Comprehensive Architectural & Strategic Documentation**: Authored the foundational Architecture Decisions & Developer Guardrails bible, a 70+ feature masterlist prioritization matrix, formal Design System tokens, and product specification docs.

---

## Overview of Modified & Created Files

| File | Status | Description |
|---|---|---|
| [`src/Program.cs`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/src/Program.cs) | **Modified** (+710, -348) | Complete UI modernization, `Theme` token engine, async mutex dispatch, atomic save, crash logging, hotplug guard. |
| [`build.bat`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/build.bat) | **Modified** (+11, -2) | Dual 32/64-bit `csc.exe` auto-detection, `/win32manifest` compiler integration, explicit process return codes. |
| [`src/app.manifest`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/src/app.manifest) | **New** | Native application manifest declaring Windows 7–11 OS support, Per-Monitor V2 DPI, and Common Controls v6. |
| [`README.md`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/README.md) | **Modified** | Updated documentation links, added real UI screenshot, added Feature Masterlist link. |
| [`docs/architecture-decisions-and-guardrails.md`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/docs/architecture-decisions-and-guardrails.md) | **New** (Commit `9135296`) | 414-line architectural bible detailing hardware realities, USB HID transport, and the 7 developer guardrails. |
| [`docs/features-masterlist.md`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/docs/features-masterlist.md) | **New** | 89-line masterlist categorizing 70+ peripheral features across 4 strategic priority tiers. |
| [`DESIGN.md`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/DESIGN.md) | **New** | Formal design system specification (colors, typography, GDI+ tokens, contrast ratios). |
| [`PRODUCT.md`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/PRODUCT.md) | **New** | Formal product definition (users, purpose, positioning, capabilities, constraints). |
| [`redesign-preview.html`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/redesign-preview.html) | **New** | Interactive HTML/CSS prototype mirroring the exact 580px desktop window layout and color scheme. |
| [`assets/control-center-ui.png`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/assets/control-center-ui.png) | **New** (Commit `88e7ed2`) | High-resolution screenshot of the Control Center interface. |
| [`assets/Before vs After.png`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/assets/Before%20vs%20After.png) | **New** | Visual graphic comparing the legacy UI against the redesigned modern utility. |
| [`assets/Design Assets.figcraft`](file:///c:/Users/Admin/Desktop/Projects/A20%20Driver/assets/Design%20Assets.figcraft) | **New** | Design asset workspace container. |

---

## 1. Core Application & Driver Engine (`src/Program.cs`)

### 1.1. Native Windows 10 & 11 Dark Title Bar Integration
- Added Win32 P/Invoke for `dwmapi.dll` (`DwmSetWindowAttribute`).
- In `OnHandleCreated`, applied `DWMWA_USE_IMMERSIVE_DARK_MODE` (attribute `20`, with fallback to attribute `19` for earlier Windows 10 builds).
- Eliminates the jarring bright-white OS title bar, ensuring seamless visual integration on dark-themed desktops.

```csharp
[DllImport("dwmapi.dll", PreserveSig = true)]
private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

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
```

### 1.2. Complete Tokenized Theme Engine (`Theme` Class)
- Built an internal `Theme` static class providing strict design tokens:
  - **Base Canvas**: `#121316`
  - **Surface Panels**: `#1A1C22`
  - **Elevated Surfaces / Inputs**: `#242731`
  - **Hairline Borders**: `#2B2F3A` (subtle `#20232A`)
  - **Primary Functional Accent**: Emerald / Hardware Mint (`#10B981`)
  - **Semantic States**: Warning (`#F59E0B`), Danger (`#EF4444`), Monospace Terminal (`#10B981`)
- **Zero-Allocation GDI+ Optimization**: Pre-allocated static `SolidBrush` and `Pen` instances (`BrushCanvas`, `BrushSurface`, `PenBorder`, `PenAccent`, etc.) so custom paint routines never allocate temporary objects or trigger GC pauses during rendering.
- **System Typography Tokens**: Pre-allocated `FontHeading` (13px bold), `FontBodyPrimary` (12px), `FontBodyBold` (12px bold), `FontBodySecondary` (11px), `FontBadge` (10px bold), and `FontMono` (11px `Consolas`).

### 1.3. UI Component Architecture & Modern Card Layout
- **Reusable Card Containers (`CreateSectionPanel`)**: Replaced default Windows `GroupBox` borders with flat, 1px custom-painted panels featuring title typography and metadata badges (e.g. `"Active-Low PWM 4-Bit"`, `"PWM Proportion Scaling"`, `"Instant A704 Auto-Apply"`).
- **Unified Action Control Bar**: Consolidated "Apply (RAM)", "Save Profile", and "Reset Profile" into a cohesive 36px action row (`pnlActions`) with clear visual hierarchy:
  - Primary filled Emerald button for `Save Profile`.
  - Elevated surface for `Apply (RAM)`.
  - Subtle secondary surface for `Reset Profile`.
- **Preset Color Selection Feedback**: Implemented `OnPresetButtonClick` and button tracking (`presetButtons[]`) to visually highlight the selected quick-preset button with an Emerald border.
- **Modernized TrackBar & Value Badge**: Configured `TickStyle = TickStyle.None` with a matching surface background and a dedicated right-aligned percentage readout label (`lblBrightnessVal`).
- **Modern Windows Explorer Theme for Controls**: Applied `SetWindowTheme(cmbDpiStages.Handle, "DarkMode_Explorer", null)` for dark dropdown controls.
- **Accessibility Enhancements**: Assigned explicit `TabIndex` ordering and human-readable `AccessibleName` labels across all interactive controls.

### 1.4. Asynchronous Hardware Mutex & Thread-Safe Experimental DPI Dispatch
- Converted `BtnTestDpi_Click` into `async void`.
- Offloaded the hardware synchronization Mutex (`TaskSchedulerHelper.HARDWARE_MUTEX_NAME`) and USB packet transmission to a background thread via `Task.Run`.
- Prevents UI thread locking and GUI stutter during DPI stage testing while ensuring cross-process hardware exclusivity.

```csharp
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
```

### 1.5. Non-Destructive HID Device Path Discovery
- In `FindDevicePath`, altered the `CreateFile` query access parameter from `GENERIC_READ | GENERIC_WRITE` to `0` (query access only).
- Allows verifying device VID/PID and capabilities without requesting read/write handles, preventing access-denied sharing conflicts during background enumeration.

### 1.6. Dynamic Device Disconnection & Stale Handle Invalidation
- When an operation fails with Win32 errors `1167` (`ERROR_DEVICE_NOT_CONNECTED`), `2` (`ERROR_FILE_NOT_FOUND`), or `6` (`ERROR_INVALID_HANDLE`):
  - Immediately invalidates `cachedDevicePath = null`.
  - Dynamically updates the header banner to `"○ Device Not Found. Is USB Connected?"` with red danger coloring.
  - Prevents subsequent operations from repeatedly attempting to communicate over a dead handle.

### 1.7. USB Hotplug Re-Entrancy Guard
- Introduced `isHotplugHandling` boolean flag in `HandleUsbHotplugDebounced`.
- Ensures rapid physical cable replugging or multiple concurrent `WM_DEVICECHANGE` messages cannot trigger overlapping background recovery pipelines.

### 1.8. Resilient Atomic Profile Serialization
- Overhauled `AppProfile.Save(out string error)`:
  - Writes serialized JSON settings to a temporary file (`.tmp`) first with UTF-8 encoding.
  - Uses atomic `File.Replace` (with `File.Move` fallback) to commit the profile file.
  - Completely eliminates file truncation or profile loss if the computer loses power or shuts down mid-write.

### 1.9. Hex Input Sanitization & Synchronization
- Added `TrySyncHexColor(out string error)` helper.
- Validates the hex string against `^#([0-9A-Fa-f]{6})$` and synchronizes the active `currentColor` state before allowing `Apply` or `Save` operations to proceed.
- Prevents malformed color strings from causing invalid PWM calculations or runtime errors.

### 1.10. Global Crash Logging & Diagnostic Trapping
- Registered application-wide exception handlers:
  - `Application.ThreadException`
  - `AppDomain.CurrentDomain.UnhandledException`
- Implemented `LogUnhandledException` which writes timestamps, fatal flags, exception messages, and complete stack traces to `%LocalAppData%\RedgearA20\crash.log`.
- Presents user-friendly diagnostic alert dialogs instead of silent process terminations.

### 1.11. Task Scheduler XML Deprecation & Cleanup
- Marked `TaskSchedulerHelper.InstallTask` with `[Obsolete]`.
- Updated `--install-task` CLI handling to print a deprecation notice and automatically configure the superior, lighter Stealth Watcher in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

### 1.12. Deterministic Form Resource Disposal
- Implemented `Dispose(bool disposing)` override in `MainForm` to explicitly clean up `trayMenu`, `trayIcon`, and `usbDebounceTimer`.

---

## 2. Build Pipeline & Windows Application Manifest

### 2.1. Native Application Manifest (`src/app.manifest`)
- Created `src/app.manifest` providing:
  - **OS Compatibility Declarations**: Explicit GUIDs for Windows 7, Windows 8, Windows 8.1, Windows 10, and Windows 11.
  - **High-DPI Awareness**: Declared `<dpiAware>true/pm</dpiAware>` and `<dpiAwareness>PerMonitorV2, PerMonitor</dpiAwareness>`.
  - **Modern Windows Common Controls**: Linked `Microsoft.Windows.Common-Controls` v6.0.0.0.
  - **Privilege Declaration**: Standard user level (`requestedExecutionLevel level="asInvoker"`), avoiding unnecessary UAC prompts.

### 2.2. Build Script Hardening (`build.bat`)
- Added dynamic auto-detection supporting both 64-bit (`Framework64`) and 32-bit (`Framework`) .NET 4.0/4.8 `csc.exe` compiler paths.
- Embedded `src/app.manifest` directly into the binary via `/win32manifest:"src\app.manifest"`.
- Added strict process return codes (`exit /b 0` on success, `exit /b 1` on failure) to support automated scripting and CI/CD pipelines.

---

## 3. Architectural Documentation & Long-Term Strategy

### 3.1. Architecture Decisions & Developer Guardrails Bible (`docs/architecture-decisions-and-guardrails.md`)
*(Added in Commit `9135296`)*  
A 414-line foundational document establishing:
- **Physical Hardware Reality**: Documented that the Instant Microelectronics A704 / A704F microcontroller lacks non-volatile EEPROM for custom RGB; custom lighting is stored in volatile SRAM registers wiped whenever the 5V rail loses power.
- **USB HID Protocol Specifications**: Full register breakdowns for Vendor Collection `UsagePage 0xFF01`, Report ID `0x07`, Active-Low PWM registers (Command `0x14`), and Solid Static mode `0x86` (Command `0x13`).
- **The 7 Strict Developer Guardrails**:
  1. Mandatory $\ge 50\text{ms}$ inter-packet spacing to prevent MCU buffer overrun.
  2. Ephemeral HID handles (`CreateFile` $\to$ write $\to$ `CloseHandle`).
  3. Zero slider spamming (sliders update UI preview only; zero USB traffic during drag).
  4. Decoupled Safe Action Architecture (RAM preview vs permanent profile save).
  5. Immutable atomic state snapshotting (`SaveParameters`).
  6. UI safety interlocking (`body.Enabled = false`) and window closing lock.
  7. Graceful hotplug handling & debounce logic (`WM_DEVICECHANGE`).

### 3.2. Feature Masterlist & Prioritization Matrix (`docs/features-masterlist.md`)
An 89-line strategic roadmap categorizing 70+ potential peripheral driver features across 4 distinct tiers:
- **Category 1: Must-Have Features (Table Stakes)**: Button remapping, DPI stage tuning, polling rate switching, debounce delay, sleep wake persistence, factory reset.
- **Category 2: Good and Useful (Zero-Bloat Sweet Spot)**: Sub-2 MB Stealth Watcher, instant USB hotplug persistence, macros, sniper clutch, LOD, angle snapping.
- **Category 3: Enthusiast / Power-User**: Multi-vendor OEM chipset support, real-time HID packet inspector, headless CLI, surface tuning.
- **Category 4: Banned Bloat (Excluded)**: Screen ambient sync, audio visualizer, local web servers, Discord RPC, telemetry, cursor heatmaps, and background daemons.

### 3.3. Formal Design System Tokens (`DESIGN.md`)
- Defines the **Industrial Precision Utility** visual language.
- Establishes full GDI+ color tokens, contrast ratios (WCAG AAA/AA compliance), typography hierarchy, and spacing scales.

### 3.4. Formal Product Specification (`PRODUCT.md`)
- Outlines target audience, core purpose, positioning against heavy OEM suites (Razer Synapse, Corsair iCUE), operating context, capabilities, and constraints.

### 3.5. Interactive HTML Preview (`redesign-preview.html`)
- Standalone HTML/CSS reference prototype matching the exact 580px desktop window dimensions, typography, and color tokens of the redesigned interface.

### 3.6. Documentation & Visual Media Updates (`README.md` & `assets/`)
- Added real GUI screenshot `assets/control-center-ui.png` to the repository.
- Added visual comparison `assets/Before vs After.png`.
- Updated `README.md` with links to the Architecture Decisions bible and Feature Masterlist.

---

## 4. Git Commits Since `v0.1.1`

```text
* 88e7ed2 - Updated README (Added UI screenshot and refined documentation)
* 39dcabb - feat: remove Google site verification file
* 5e23213 - feat: add Google site verification file
* 9135296 - docs: add architecture decisions, hardware realities, and developer guardrails bible
* cb5915b - (TAG: v0.1.1) docs: document loud exit confirmation dialog and [X] button persistence behavior
```

---

## Summary of Outcomes

| Area | Before (v0.1.1 Release) | Now (Post-v0.1.1 Development) |
|---|---|---|
| **Window Frame** | Standard white OS title bar on Windows 10/11 | Native immersive dark title bar via `dwmapi.dll` |
| **Visual Aesthetic** | Generic WinForms GroupBoxes & inconsistent colors | Industrial dark utility with Emerald accent and hairline cards |
| **GDI Performance** | Dynamic brush allocations on paint | Pre-allocated static brushes, pens, and fonts (0 allocation) |
| **DPI Testing Safety** | Synchronous mutex lock on UI thread | Asynchronous background worker dispatch via `Task.Run` |
| **Device Discovery** | Requested Read/Write access during path search | Non-destructive query access (`0`) eliminating sharing conflicts |
| **Cable Disconnect** | Continued trying to write to invalid path | Instant handle invalidation and UI red alert status |
| **Hotplug Handling** | Potential concurrent hotplug overlap | Re-entrancy guarded via `isHotplugHandling` |
| **Profile Storage** | Direct overwrite (risk of file truncation) | Atomic temp-file commit with `File.Replace` / `File.Move` |
| **Input Validation** | Unchecked text input before save | Format validation via `TrySyncHexColor` with user feedback |
| **Crash Handling** | Unhandled WinForms crash dialog | Trapped globally and written to `%LocalAppData%\RedgearA20\crash.log` |
| **DPI & Manifest** | Default OS bitmap scaling | Embedded `src/app.manifest` with Per-Monitor V2 DPI scaling |
| **Compiler Support** | Hardcoded 64-bit csc.exe path | Automatic 64-bit and 32-bit `csc.exe` detection with return codes |
| **Documentation** | Basic README and protocol notes | Complete Architecture Bible, Design Tokens, and 70+ Feature Masterlist |
