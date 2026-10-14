# Changelog

All notable changes to the Redgear A20 Custom Driver & Control Center project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
