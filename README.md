# Redgear A20 Control Center

[![Download Executable](https://img.shields.io/badge/Download%20.exe-v0.2.1-success)](https://github.com/shivamvcx/A20-Driver/releases)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Platform: Windows x64](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(64--bit)-0078D6.svg)](https://microsoft.com)
[![Architecture: C# WinForms](https://img.shields.io/badge/Architecture-C%23%20WinForms-239120.svg)](src/Program.cs)
[![Executable Size](https://img.shields.io/badge/Binary%20Size-~230%20KB-orange.svg)](#build-from-source)
[![Zero Dependencies](https://img.shields.io/badge/Dependencies-Zero%20(Pure%20Win32)-success.svg)](#disclaimer)

**Open-source Redgear A20 software for Windows** — a lightweight replacement for the official Redgear A20 gaming mouse driver. Control **RGB lighting, brightness, DPI, button remapping and keyboard shortcuts** from a single lightweight `.exe`: no background services, no telemetry. Run it portable, or use the optional `Setup.exe` to install it per-user — both are the exact same program with identical features.

It talks directly to the **Redgear A20 RGB Gaming Mouse** (`VID: 0x18F8 & PID: 0x0FC0`) and its Instant A704 microcontroller over USB HID Feature Reports.

> **Latest version: [v0.3.0](https://github.com/shivamvcx/A20-Driver/releases/latest)** — adds Button Remapping and Keyboard Shortcuts.
>
> **Next: v0.4.0** — complete UI redesign, multiple saved profiles, light/dark themes and a Setup installer ([details](#whats-new-in-v040)).

<p align="center">
  <img src="assets/control-center-ui.png" alt="Redgear A20 Control Center UI" width="460">
</p>

## Key Features

- **True 12-bit RGB control** — 4,096 colors via reverse-engineered active-low PWM registers, with a visual picker, `#RRGGBB` hex input, and quick presets.
- **Solid static mode** — hardware mode `0x86` permanently replaces the stock, unchangeable rainbow breathing cycle.
- **Proportional brightness dimming** — 5–100% scaling that preserves the exact color hue.
- **Experimental DPI stage control** — select among 600–3200 DPI stages (opt-in and RAM-tested before saving).
- **Button remapping** — reassign Middle / Wheel, Forward, Back, DPI + and DPI − to clicks, DPI controls, Double Click, Rapid Fire, Show Desktop or Disabled. Test on the mouse before saving; Left / Right Click stay locked so the mouse can never become unusable.
- **Keyboard shortcuts on mouse buttons** — bind any remappable button to a key combo (e.g. `Ctrl + C`, `Alt + Tab`, `F5`). The mouse types it as its own USB keyboard — no global keyboard hooks. Ctrl + Alt + Del is blocked and risky combos ask for confirmation.
- **Stealth persistence** — a hidden background watcher (~1.8 MB RAM, 0% CPU, no tray clutter) detects USB hotplugs via native `WM_DEVICECHANGE` and restores your settings in ~300 ms, across reboots, sleep cycles, and any USB port. Three modes: Stealth Watcher, Tray Agent, or RAM-only.
- **Safety-first engineering** — RAM-only application, mandatory 50 ms inter-packet pacing, atomic profile writes, global crash logging, and automatic disconnect recovery.
- **Live diagnostic console** — every 8-byte feature report with its Win32 return status, copyable to clipboard.
- **Multiple profiles** — save named profiles (rename, duplicate, delete); clicking one applies it instantly and makes it the profile restored on replug and reboot.
- **Native modern UI** — Dark, Light or System theme (follows Windows), Per-Monitor V2 DPI awareness, and crisp 4K rendering in a ~230 KB binary.

## Quick Start

1. Download from [Releases](https://github.com/shivamvcx/A20-Driver/releases) either the portable `RedgearA20-win-x64.exe` or the installer `RedgearA20-win-x64-setup.exe` (per-user, no admin rights; adds a Start menu entry and an uninstaller).
2. Plug in your Redgear A20 and run the portable exe — or run Setup and launch it from the Start menu.
3. Confirm the left panel shows **● Connected**.
4. Pick a colour and brightness on the **Lighting** tab (and a DPI stage on **DPI**), then click **Save Profile**. Use **+** in the Profiles column to add more profiles.
5. Open **Settings** (gear icon) and choose **Stealth watcher** as the background mode.
6. Close the window. Your active profile now persists silently across reboots and USB replugs.
7. *(Optional)* On the **Buttons** tab, open the Button Mapper to remap buttons or assign keyboard shortcuts. Shortcuts need the app running in the background, so keep **Stealth watcher** or **Tray agent** enabled.

## Updating

The app checks GitHub Releases for a newer **stable** version (drafts and pre-releases are never offered) — at launch, then once a day while running in the background. When one is found, an **Update available** notice appears in the app; in Stealth or Tray mode a Windows notification is shown instead. Click **Update now** and the new build is downloaded, verified against its published SHA-256 checksum, installed in place, and the app restarts with your profile intact. You can also check manually with **Check for Updates** (in Settings or the tray menu). Installed (Setup) and portable copies update the same way. If an update can't be installed — for example, the exe lives in a protected folder — your current version is left untouched and you're offered the download page instead.

## How Persistence Works

The A704 chip has no non-volatile storage for user settings — custom registers live in volatile SRAM and reset to factory defaults the moment USB power drops. The original vendor software worked around this with a bloated background service; this project solves it with a featherweight hidden watcher that reapplies your profile the instant the mouse reappears on the bus.

## USB Protocol

All communication uses 8-byte HID Feature Reports on the vendor endpoint `UsagePage 0xFF01` (Report ID `0x07`):

| Command | Purpose | Status |
| :---: | :--- | :---: |
| `0x14` | Per-stage RGB color (4-bit active-low PWM) | Verified |
| `0x13` | LED mode — `0x86` static, `0x87` off, breathing, spectrum | Verified |
| `0x20` | Hardware register sync latch | Verified |
| `0x09` | DPI stage & enabled-stage bitmask | Experimental |
| `0x10` | Button remapping (per-button action code) | Verified |
| `0x15` | Keyboard key injection (shortcut playback) | Verified |
| `0x16` | Extended button matrix | Research |

## What's New in v0.4.0

- **Complete UI redesign** — three columns: a mouse preview that glows in your LED colour, a list of saved profiles, and the settings (Lighting · DPI · Buttons tabs). Settings, the update check and the USB diagnostic log live behind the gear icon.
- **Multiple named profiles** — click to apply; Preview tries changes on the mouse without saving, and unsaved edits are never lost silently.
- **Themes** — Dark, Light, or System (follows the Windows app theme).
- **Setup installer** — an optional per-user `Setup.exe` alongside the portable exe. Same binary, same features; it just adds a Start menu entry and a clean uninstaller (your profiles are kept).

Existing profiles carry over automatically.

## Roadmap

| Version | Scope | Status |
| :---: | :--- | :---: |
| v0.1.0 – v0.2.1 | RGB engine, stealth persistence, industrial UI overhaul, stability hardening | Released |
| v0.3.0 | Hardware button remapping + keyboard shortcuts on mouse buttons | Released |
| v0.4.0 | Complete UI redesign, multiple profiles, themes, Setup installer | Ready |
| Future | Extended RGB effects, sensor & polling rate, per-stage DPI customization, sniper mode | Planned |
| v1.0.0 | Stable release + multi-device OEM support | Long-term |

## Build from Source

No Visual Studio, SDK, or toolchain installation required — the project compiles with Windows' built-in C# compiler:

```cmd
git clone https://github.com/shivamvcx/A20-Driver
cd A20-Driver
build.bat
```

Output lands in `release\`: `RedgearA20-win-x64.exe` plus its SHA-256 checksum. If [Inno Setup 6](https://jrsoftware.org/isinfo.php) is installed, `build.bat` also builds `RedgearA20-win-x64-setup.exe` from `installer\RedgearA20.iss`.

## Supporting Other Mice

Community contributions for other OEM mice (Instant Microelectronics, SinoWealth, or Holtek chipsets) are welcome. Open an issue with your device's `VID`/`PID`, its USB HID report descriptor, and packet captures recorded against the original vendor software.

## Disclaimer

> **This is an independent, unofficial community project.** It is not affiliated with, endorsed by, sponsored by, or associated with Redgear or Imagine Marketing Limited. All trademarks belong to their respective owners.
>
> This software communicates directly with mouse hardware using reverse-engineered USB HID feature reports. While it is built with strict safety guardrails (RAM-only application, mandatory inter-packet delays, decoupled save operations), **use this software at your own risk**.

## Author

**Shivam** ([@shivamvcx](https://github.com/shivamvcx)) — reverse-engineered the USB protocol, validated every packet and guardrail on physical hardware, and used AI coding agents for implementation acceleration and documentation. Everything was reviewed, debugged, and verified against the real device.

## License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.
