# Changelog

All notable changes to the Redgear A20 Custom Driver & Control Center project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.1.1] - 2026-10-15

### Added
- **🚀 Stealth Driver Watcher (Near-Zero RAM Persistence)**:
  - **Zero Taskbar & Zero System Tray Clutter**: Sits as an invisible background watcher (`trayIcon.Visible = false`) with no taskbar button and no tray icons cluttering your screen.
  - **Ultra-Low Memory Footprint**: Uses `SetProcessWorkingSetSize` P/Invoke to aggressively compress working set memory to **~1.8 MB RAM** and **0.00% CPU** when idle.
  - **Instant Hotplug Persistence Across All Ports**: Intercepts native Win32 `WM_DEVICECHANGE` (`DBT_DEVICEARRIVAL` / `DBT_DEVNODES_CHANGED`) in `< 50ms`. Automatically reapplies custom color and DPI within `~300ms` when unplugged and replugged into ANY USB 2.0/3.0 port or hub.
  - **Boot & Standby Persistence**: Automatically starts on Windows user login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Survives reboots and Modern Standby / S3 sleep.
  - **Single-Instance IPC with UIPI Traversal**: Configured `ChangeWindowMessageFilter` for registered broadcast message `RedgearA20_ShowControlCenter`. Launching the `.exe` again automatically wakes the existing GUI control center.
  - **3 Persistence Modes**:
    1. `🚀 Stealth Driver Watcher` (Invisible, No Tray Icon, ~1.8 MB RAM — Recommended)
    2. `📌 Tray Agent Mode` (~2.7 MB RAM, Quick Access System Tray Icon)
    3. `○ Disabled` (RAM Only — reverts to default when mouse is unplugged)
  - **Explicit Exit Controls**: Added an "Exit App" button in the header banner and an "Exit Driver" context menu item in tray mode.
- **🎯 ⚠️ Experimental DPI Stage Switching**:
  - Implemented Command `0x09` (`[0x07, 0x09, 0x40 | Stage, 0x08, 0x3F, 0x00, 0x00, 0x00]`) to configure active DPI levels (Stages 0–5: 600, 1000, 1200, 1600, 2400, 3200 DPI).
  - Protected behind an opt-in checkbox with live RAM-only testing (`[ Test DPI (RAM) ]`).
- **Asynchronous Non-Blocking Hardware Pipeline**:
  - Migrated hardware USB dispatch to background workers (`Task.Run` with `async`/`await`) to eliminate GUI stutter and UI thread freezes during multi-packet operations.
  - Implemented immutable `SaveParameters` state snapshotting to protect against mid-save slider drags or preset changes.
  - Added container-level UI locking (`body.Enabled = false`) during transmission with real-time status updates.
  - Added `OnFormClosing` safety interlock to block accidental window closure while hardware write is in progress.
- **Factory Profile Reset (`↺ Reset Profile`)**:
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
