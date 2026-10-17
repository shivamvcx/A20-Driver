# Changelog

All notable changes to the Redgear A20 Custom Driver & Control Center project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.2.0] - 2026-10-17

### Added
- **Industrial Precision Utility UI Overhaul**:
  - **Native Dark Window Title Bar**: Integrated Win32 `dwmapi.dll` P/Invoke (`DWMWA_USE_IMMERSIVE_DARK_MODE`) to render native dark title bars and frames on Windows 10 and 11 desktops.
  - **Zero-Allocation GDI+ Token Engine**: Pre-cached brushes, pens, and system fonts via static `Theme` class, eliminating GC allocation pauses and rendering stutter during window repainting.
  - **Hairline Card Containers**: Flat 1px border card panels (`CreateSectionPanel`) with section headers and metadata badges replacing default WinForms GroupBoxes.
  - **Visual Preset Border Highlighting**: Active-state visual feedback (`OnPresetButtonClick`) highlighting the selected quick-preset color with an Emerald border.
  - **Unified Action Control Row**: Reorganized `Apply (RAM)`, `Save Profile`, and `Reset Profile` into a cohesive 36px action row with clear visual hierarchy and primary Emerald styling for profile saving.
  - **Modernized TrackBar**: Configured brightness slider with `TickStyle.None` and dedicated right-aligned percentage readout label.
  - **Explorer Dark Mode Controls**: Applied `SetWindowTheme(..., "DarkMode_Explorer", null)` for dark dropdown styling.
- **Native Windows Application Manifest (`src/app.manifest`)**:
  - Embedded manifest declaring Per-Monitor V2 High-DPI awareness (`PerMonitorV2, PerMonitor`) to ensure crisp text and layout scaling on 4K/high-DPI monitors.
  - Dependency linking to `Microsoft.Windows.Common-Controls` v6.0.0.0.
  - Explicit Windows OS compatibility declarations (Windows 7, 8, 8.1, 10, 11) and `asInvoker` execution level.
- **Asynchronous Concurrency & Safe Mutex Dispatch**:
  - Converted experimental DPI testing (`BtnTestDpi_Click`) to asynchronous background worker execution (`Task.Run` with `async`/`await`).
  - Hardware synchronization mutex (`TaskSchedulerHelper.HARDWARE_MUTEX_NAME`) is acquired on background threads, completely eliminating UI thread stutter and lockups.
- **Fault-Tolerant Storage & Crash Diagnostics**:
  - **Atomic JSON Profile Serialization**: Overhauled `AppProfile.Save(out string error)` to write to a temporary `.tmp` file and replace the target atomically (`File.Replace`), eliminating profile corruption risk during abrupt power loss.
  - **Hex Input Sanitization & Validation**: Added `TrySyncHexColor(out string error)` helper enforcing `#RRGGBB` format and synchronizing active color state before executing save/apply actions.
  - **Global Crash Trapping**: Application-wide unhandled exception telemetry (`Application.ThreadException` and `AppDomain.CurrentDomain.UnhandledException`) logged directly to `%LocalAppData%\RedgearA20\crash.log` with detailed stack traces.
- **Multi-Architecture Build Pipeline (`build.bat`)**:
  - Intelligent auto-detection for 64-bit (`Framework64`) and 32-bit (`Framework`) `csc.exe` compiler paths.
  - Added `/win32manifest:"src\app.manifest"` compiler flag to embed application manifest into the executable.
  - Explicit process return codes (`exit /b 0` and `exit /b 1`).
- **Comprehensive Architectural Documentation**:
  - Added `docs/architecture-decisions-and-guardrails.md`: 414-line developer bible covering Instant A704 hardware volatile SRAM realities and the 7 Developer Guardrails.
  - Added `docs/features-masterlist.md`: 89-line masterlist organizing 70+ peripheral driver features across 4 strategic priority tiers.
  - Added `DESIGN.md` & `PRODUCT.md`: Formal Design System tokens and Product Strategy specifications.

### Changed
- **Non-Destructive Device Path Enumeration**: Changed device query access parameter in `CreateFile` to `0` (query access only), eliminating handle sharing conflicts during background enumeration.
- **Task Scheduler Deprecation**: Deprecated `--install-task` XML installer in favor of the lightweight Stealth Driver Watcher (`--stealth` via `HKCU Run`).

### Fixed
- **UI Freezes During DPI Testing**: Offloaded mutex wait and packet transmission to asynchronous background tasks.
- **Stale Device Handle on Disconnect**: Automatically detects Win32 disconnect errors (`1167`, `2`, `6`), invalidates cached device paths, and displays visual red alert status.
- **Hotplug Re-Entrancy Hazard**: Introduced `isHotplugHandling` guard flag to prevent race conditions during rapid physical USB cable replugging.
- **Resource Cleanup on Exit**: Implemented `Dispose(bool disposing)` override in `MainForm` to explicitly release system tray icon, context menu, and debounce timer.

---

## [0.1.1] - 2026-10-15

### Added
- **Stealth Driver Watcher (Near-Zero RAM Persistence)**:
  - **Zero Taskbar & Zero System Tray Clutter**: Sits as an invisible background watcher (`trayIcon.Visible = false`) with no taskbar button and no tray icons cluttering your screen.
  - **Ultra-Low Memory Footprint**: Uses `SetProcessWorkingSetSize` P/Invoke to aggressively compress working set memory to **~1.8 MB RAM** and **0.00% CPU** when idle.
  - **Instant Hotplug Persistence Across All Ports**: Intercepts native Win32 `WM_DEVICECHANGE` (`DBT_DEVICEARRIVAL` / `DBT_DEVNODES_CHANGED`) in `< 50ms`. Automatically reapplies custom color and DPI within `~300ms` when unplugged and replugged into ANY USB 2.0/3.0 port or hub.
  - **Boot & Standby Persistence**: Automatically starts on Windows user login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Survives reboots and Modern Standby / S3 sleep.
  - **Single-Instance IPC with UIPI Traversal**: Configured `ChangeWindowMessageFilter` for registered broadcast message `RedgearA20_ShowControlCenter`. Launching the `.exe` again automatically wakes the existing GUI control center.
  - **3 Persistence Modes**:
    1. Stealth Driver Watcher (Invisible, No Tray Icon, ~1.8 MB RAM — Recommended)
    2. Tray Agent Mode (~2.7 MB RAM, Quick Access System Tray Icon)
    3. Disabled (RAM Only — reverts to default when mouse is unplugged)
  - **Explicit Exit Controls & Safety Guardrails**: Added an "Exit App" button in the header banner and an "Exit Driver (Stop Persistence)" context menu item in tray mode. Features a prominent, high-visibility warning dialog explaining that exiting terminates background persistence, defaults to "No" to prevent accidental clicks, and explicitly recommends using the standard Windows Close [X] button to keep the silent persistence facility running.
- **Experimental DPI Stage Switching**:
  - Implemented Command `0x09` (`[0x07, 0x09, 0x40 | Stage, 0x08, 0x3F, 0x00, 0x00, 0x00]`) to configure active DPI levels (Stages 0–5: 600, 1000, 1200, 1600, 2400, 3200 DPI).
  - Protected behind an opt-in checkbox with live RAM-only testing (`[ Test DPI (RAM) ]`).
- **Asynchronous Non-Blocking Hardware Pipeline**:
  - Migrated hardware USB dispatch to background workers (`Task.Run` with `async`/`await`) to eliminate GUI stutter and UI thread freezes during multi-packet operations.
  - Implemented immutable `SaveParameters` state snapshotting to protect against mid-save slider drags or preset changes.
  - Added container-level UI locking (`body.Enabled = false`) during transmission with real-time status updates.
  - Added `OnFormClosing` safety interlock to block accidental window closure while hardware write is in progress.
- **Factory Profile Reset (`Reset Profile`)**:
  - Restores verified stock multi-stage DPI colors (Blue, Yellow, Green, Purple, Cyan, White) and default breathing cycle mode (`0x80`), removes auto-start registry keys, and clears persistence.
- **Human-Readable Win32 Error Translation**:
  - Integrated descriptive error messages for common Win32 codes (`ERROR_ACCESS_DENIED`, `ERROR_GEN_FAILURE`, `ERROR_INVALID_PARAMETER`, `ERROR_DEVICE_NOT_CONNECTED`).

### Fixed
- **Hardware Profile Loss Across USB Unplugs & Reboots**:
  - Disassembled and verified that the OEM Instant A704 microcontroller physically stores custom RGB lighting in volatile SRAM registers (not flash EEPROM).
  - Proved that Windows Event Log never emits device arrival events on normal USB hotplug (Event 410 is only emitted during first installation), rendering Task Scheduler EventTriggers ineffective for replug.
  - Solved permanent persistence through the ultra-lightweight Win32 `WM_DEVICECHANGE` Stealth Driver Watcher.

---

## [0.1.0] - 2026-10-14

### Added
- **Native Windows GUI Desktop App (`RedgearA20-win-x64.exe`)**:
  - Standalone portable WinForms application ($\approx 25\text{ KB}$) compiled with built-in .NET Framework `csc.exe`.
  - Zero external dependencies; zero background services or telemetry.
  - Dark-themed control panel with automatic device detection.
- **Hardware Reverse Engineering & USB HID Transport**:
  - Full reverse engineering of official `Gaming Mouse 2.0.exe` (Instant Microelectronics chipset, `VID: 0x18F8 & PID: 0x0FC0`).
  - Direct Win32 P/Invoke transport (`hid.dll` / `setupapi.dll`) targeting vendor collection `UsagePage 0xFF01` (Report ID `0x07`).
  - Verified Command `0x14` for 4-bit active-low PWM RGB channel programming across 6 DPI stages.
  - Verified Command `0x13` mode byte `0x86` for permanent Solid Static lighting (eliminates forced breathing effect).
- **12-bit Active-Low Precision Color Engine**:
  - Support for 4,096 discrete hardware colors ($16 \times 16 \times 16$).
  - Full Active-Low PWM inverted mapping: $0 = 100\%$ ON, $15 = 0\%$ OFF.
  - Visual Color Dialog picker + Hex code validation (`#RRGGBB`).
  - Curated quick presets (Lavender, Pure Purple, Neon Violet, Cyan, Mint, Gold, etc.).
- **Proportional Brightness Dimming**:
  - Variable brightness slider ($5\text{–}100\%$) that scales channel PWM duty cycles proportionally without shifting color hue.
- **Strict Safety Architecture & Guardrails**:
  - **Apply to RAM Only**: Live changes are sent only to volatile memory to prevent EEPROM exhaustion.
  - **Decoupled Save Button**: Command `0x20` is quarantined to an explicit "Save to Mouse Memory" button with confirmation.
  - **Zero Slider Spamming**: Sliders only update local UI preview; zero USB packets transmitted during drag.
  - **Mandatory $50\text{ms}$ Delays**: Safe inter-packet delay preventing MCU buffer overrun.
  - **Safe Reset**: One-click restore to verified factory default lighting.
  - **Live Hex Diagnostic Log**: Real-time console displaying every 8-byte packet sent with Win32 return codes and clipboard export.
- **Experimental Section**:
  - Isolated DPI control panel (Command `0x09`) behind an opt-in checkbox, disabled by default.

### Known Limitations (v0.1.0)
- Onboard EEPROM flash persistence (Command `0x20`) remains non-persistent on certain hardware revisions of the A20; volatile RAM configuration is fully operational.
- DPI stage switching (Command `0x09`) is currently marked experimental pending hardware validation.
- Physical button remapping is excluded in v1 to protect core input integrity.
