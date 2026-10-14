# Changelog

All notable changes to the Redgear A20 Custom Driver & Control Center project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.1.1] - 2026-10-14

### Fixed
- **EEPROM Flash Persistence Bug**:
  - Resolved issue where saving settings (e.g. Lavender `#BE59FF`) reverted to factory defaults upon USB unplug/replug or cold restart.
  - Implemented full hardware write sequence: dispatches Command `0x14` across all 6 DPI stages, locks permanent solid mode with Command `0x13`, and transmits Command `0x20` with the verified flash commit unlock key `[0x07, 0x20, 0x5A, 0xA5, 0x00, 0x00, 0x00, 0x00]`.
  - Added user disclosure warning informing that saving custom color to mouse memory synchronizes and overwrites stock per-stage DPI indicator colors.

### Added
- **Asynchronous Non-Blocking Hardware Pipeline**:
  - Migrated hardware USB dispatch to background workers (`Task.Run` with `async`/`await`) to eliminate GUI stutter and UI thread freezes during multi-packet operations.
  - Implemented immutable `SaveParameters` state snapshotting to protect against mid-save slider drags or preset changes.
  - Added container-level UI locking (`body.Enabled = false`) during flash writes with real-time status banner updates (`⏳ Writing to EEPROM flash... Do not disconnect mouse.`).
  - Added `OnFormClosing` safety interlock to block accidental window closure while flash write is in progress.
- **Post-Write MCU Transport Health Verification**:
  - Enforced mandatory `300ms` EEPROM programming delay (`EEPROM_WRITE_DELAY_MS`) following Command `0x20`.
  - Interfaced `HidD_GetFeature` (Report ID `0x07`, 32 bytes) to verify microcontroller bus responsiveness and health after flash programming.
- **Factory Profile Reset (`↺ Factory Reset`)**:
  - Added dedicated one-click factory reset feature restoring verified stock multi-stage DPI colors (Blue, Yellow, Green, Purple, Cyan, White) and default breathing cycle mode (`0x80`), followed by EEPROM commit.
- **Human-Readable Win32 Error Translation**:
  - Integrated symbolic and descriptive error messages for common Win32 codes (`ERROR_ACCESS_DENIED`, `ERROR_GEN_FAILURE`, `ERROR_INVALID_PARAMETER`, `ERROR_DEVICE_NOT_CONNECTED`).

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
