# 🖱️ Redgear A20 Control Center

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows x64](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(64--bit)-0078D6.svg)](https://microsoft.com)
[![Architecture: C# WinForms](https://img.shields.io/badge/Architecture-C%23%20WinForms-239120.svg)](src/Program.cs)
[![Executable Size](https://img.shields.io/badge/Binary%20Size-~25%20KB-orange.svg)](#build-from-source)
[![Zero Dependencies](https://img.shields.io/badge/Dependencies-Zero%20(Pure%20Win32)-success.svg)](#architecture)

A standalone, zero-bloat, open-source Windows driver and hardware control application for the **Redgear A20 RGB Gaming Mouse** (`VID: 0x18F8 & PID: 0x0FC0`).

Designed to replace the abandoned official driver software with a clean, high-performance desktop application that unlocks true 12-bit RGB lighting control, eliminates unwanted rainbow breathing, and communicates directly with mouse hardware without background services or telemetry.

---

## ⚠️ Disclaimer

> **This is an independent, unofficial community project.** It is not affiliated with, endorsed by, sponsored by, or associated with **Redgear** or **Imagine Marketing Limited**. All trademarks and brand names belong to their respective owners.
> 
> This software interacts directly with mouse hardware using reverse-engineered USB HID Feature Reports. While it has been designed with strict safety guardrails (RAM-only application, mandatory hardware delays, decoupled flash writes), **use this software at your own risk**.

---

## 🖥️ System & Hardware Compatibility

* **Supported Mouse**: **Redgear A20 RGB Gaming Mouse** (Instant Microelectronics OEM chipset, `VID: 0x18F8`, `PID: 0x0FC0`).
  *(Other mice are not supported in this release; see [Adding Support for Other Mice](#-adding-support-for-other-mice)).*
* **Supported Operating System**: **Windows 10 / Windows 11 (64-bit)**.
* **Dependencies**: **None**. Built with standard .NET Framework 4.8 Windows Forms; runs on any standard Windows installation out-of-the-box.

---

## ✨ Features (v0.1.0 Scope)

* **🎨 4,096-Color True Precision Engine**:
  - Full 12-bit RGB color control ($16 \times 16 \times 16$ levels per channel).
  - Uses verified **Active-Low PWM** register mapping ($0 = 100\%$ ON, $15 = 0\%$ OFF).
  - Visual Color Dialog picker + custom Hex code input (`#RRGGBB`).
  - Quick presets: *Lavender, Pure Purple, Cyan, Ice Blue, Mint, Green, Gold, Pink, White*.
* **💡 Proportional PWM Brightness Dimming**:
  - Variable brightness slider ($5\text{–}100\%$) that scales channel duty cycles proportionally while preserving the exact color hue.
* **🔒 Solid Static Mode (No Breathing)**:
  - Hardware mode `0x86` forces the microcontroller to display permanent, non-fading solid light, eliminating the default unchangeable rainbow breathing cycle.
  - *Note: Additional customizable RGB effects (custom breathing speeds, cyclic spectrum, and dynamic lighting) will be added in future updates — see [docs/roadmap.md](docs/roadmap.md).*
* **🛡️ Hardened Safety Architecture**:
  - **Apply to RAM Only**: Live changes are applied to volatile memory first; unsaved settings revert cleanly upon unplugging the mouse.
  - **Decoupled EEPROM Save**: Flashing onboard memory (`0x20`) is isolated behind an explicit confirmation button.
  - **Zero Slider Spamming**: Sliders only update local UI preview; zero USB packets are transmitted while dragging.
  - **Mandatory 50ms Delays**: Safe inter-packet delay prevents 8-bit MCU buffer overruns.
* **🔍 Live Monospace Diagnostic Log**:
  - Embedded console displaying every 8-byte Feature Report sent and its Win32 return status, with one-click clipboard export.
* **⚡ Ultra-Light Portable Executable**:
  - Standalone $\approx 25\text{ KB}$ 64-bit executable (`RedgearA20-win-x64.exe`). No installer, no background services, no electron bloat.

---

## 🔬 Verified vs. Unverified Protocol Commands

All hardware communication is performed via 8-byte USB HID Feature Reports sent to endpoint **UsagePage `0xFF01` (Report ID `0x07`)**:

```
Packet Structure: [0x07, Command, Param1, Param2, Param3, Param4, Param5, Param6]
```

| Command | Target | Payload Format | Description | Status |
| :---: | :---: | :--- | :--- | :---: |
| **`0x14`** | DPI Stage RGB | `[07 14 (Stage<<5)\|Green_Inv (Red_Inv<<4)\|Blue_Inv 00 00 00 00]` | 4-bit Active-Low PWM color per stage | ✅ **VERIFIED** |
| **`0x13`** | LED Mode | `[07 13 7F ModeByte 00 00 00 00]` | `0x86` = Solid Static, `0x80` = Breathing, `0x87` = Off | ✅ **VERIFIED** |
| **`0x20`** | Flash Memory | `[07 20 00 00 00 00 00 00]` | Commits active settings to onboard EEPROM | ⚠️ In Progress |
| **`0x09`** | Sensor DPI | `[07 09 (0x40\|Stage) 08 EnabledMask 00 00 00]` | Active stage & 6-bit enabled mask | ⚠️ Experimental |

> 📚 **Project Documentation & Guides**:
> - [Protocol Specification](docs/protocol-notes.md) — Complete byte-by-byte register and command reference.
> - [Feature Roadmap](docs/roadmap.md) — Upcoming milestones, planned RGB effects, and sensor tuning.
> - [Engineering DevLog](docs/devlog.md) — Technical story, reverse-engineering breakthrough, and safety architecture.
> - [Changelog](docs/changelog.md) — Version release notes and migration history.

---

## 🛠️ Build from Source

This project compiles with the built-in Microsoft C# compiler (`csc.exe`) included with Windows. **No Visual Studio, SDK, or toolchain installation is required.**

1. Clone the repository:
   ```cmd
   git clone https://github.com/shivamvcx/A20-Driver.git
   cd A20-Driver
   ```
2. Run the automated build script:
   ```cmd
   build.bat
   ```
3. The standalone 64-bit executable and its SHA256 checksum will be generated in the `release/` directory:
   - `release\RedgearA20-win-x64.exe`
   - `release\RedgearA20-win-x64.sha256`

---

## 🎮 How to Use

1. Run `release\RedgearA20-win-x64.exe`.
2. Check the top status bar: it will confirm `● Connected: Redgear A20 (VID:18F8 PID:0FC0 UsagePage:0xFF01)`.
3. Select your desired color using the **Color Picker**, presets, or by typing a custom `#RRGGBB` hex code.
4. Adjust the **Brightness** slider.
5. Click **`[ ▶ Apply to Mouse (RAM Only) ]`**. The mouse LED will immediately switch to your color with a steady, non-breathing glow.
6. *(Optional)* If you wish to test saving permanently to onboard memory, click **`[ 💾 Save to Mouse Memory (0x20) ]`**. Otherwise, your settings will revert cleanly when the mouse is unplugged.

---

## 🌐 Adding Support for Other Mice

If you have a different budget or OEM gaming mouse (e.g., mice powered by Instant Microelectronics, SinoWealth, or Holtek chips) that suffers from broken vendor software, community contributions are welcome!

To request support or contribute a profile, please open an Issue with:
1. **Device Identification**:
   - Vendor ID (`VID`) and Product ID (`PID`) (found in Windows Device Manager $\rightarrow$ Mice $\rightarrow$ Hardware IDs).
2. **USB HID Report Descriptor**:
   - Exported using tools like `USBlyzer`, `Wireshark` with `USBPcap`, or `node-hid`.
3. **Packet Captures**:
   - USB packet capture (`.pcapng`) recorded while clicking buttons or applying colors in the original vendor software.

---

## 👨‍💻 Author & Project

Developed and maintained by **[Shivam](https://github.com/shivamvcx)** as part of **[Silicon Labs](https://siliconlabs.vercel.app/)**.

---

## 📄 License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.
