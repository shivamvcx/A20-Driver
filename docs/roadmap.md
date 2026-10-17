# 🗺️ Project Roadmap: Redgear A20 Control Center

This document outlines the development milestones, planned features, and architectural direction for the **Redgear A20 Control Center** project.

---

## Project Vision & Guiding Principles

The official Redgear software is abandoned, bloated, and unstable. Our goal is to provide a clean, high-performance hardware utility inspired by tools like **Logitech Onboard Memory Manager (OMM)** and **OpenRGB**:

1. **Zero-Bloat & Portable**: Single standalone executable ($<30\text{ KB}$), zero background services, zero electron wrappers, zero network telemetry.
2. **Hardware-First**: Directly communicate with mouse microcontroller registers via native USB HID Feature Reports.
3. **Safety First**: Never spam device buffers, decouple volatile RAM testing from flash writes, and safeguard core input switches against lockups.

---

## Release Milestones

```mermaid
flowchart LR
    v1["v0.1.0\nFoundation & Color\n(RELEASED)"] --> v11["v0.1.1\nStealth Persistence & DPI\n(RELEASED)"]
    v11 --> v2["v0.2.0\nUI Overhaul & Hardening\n(RELEASED)"]
    v2 --> v3["v0.3.0\nExtended RGB Effects\n(PLANNED)"]
    v3 --> v4["v0.4.0\nSensor & Polling Rate\n(PLANNED)"]
    v4 --> v5["v0.5.0\nButton Remapping\n(RESEARCH)"]
    v5 --> v6["v1.0.0\nUniversal OEM Support\n(LONG TERM)"]
```

---

### Milestone 1: v0.1.0 — Core Lighting & Safety Architecture (Released)
- [x] Full reverse engineering of `Gaming Mouse 2.0.exe` and endpoint collection detection (`UsagePage 0xFF01`).
- [x] **12-bit Active-Low PWM Color Engine**: 4,096 discrete hardware colors ($16 \times 16 \times 16$).
- [x] **Proportional Brightness Dimming**: Continuous scaling ($5\text{–}100\%$) while preserving exact color hue.
- [x] **Solid Static Mode (`0x86`)**: Eliminated unchangeable vendor rainbow breathing.
- [x] **Hardened Safety Guardrails**: RAM-only live preview, isolated save button, mandatory 50ms hardware delays, and zero-spam slider mechanics.
- [x] **Live Hex Packet Inspector**: Real-time diagnostic console displaying all 8-byte Feature Reports.
- [x] **Zero-Dependency Native WinForms GUI**: Standalone 64-bit executable (`RedgearA20-win-x64.exe`) and 1-click build script.

---

### Milestone 1.5: v0.1.1 — Stealth Driver Watcher & Instant Persistence (Released)
- [x] **Stealth Driver Watcher**:
  - Intercepts Win32 `WM_DEVICECHANGE` (`0x0219`) for instant (<50ms) detection of USB insertion on ANY port or hub.
  - Automatically restores custom color and DPI in ~300ms.
  - P/Invoke `SetProcessWorkingSetSize` compresses idle RAM footprint to **~1.8 MB** (0.00% CPU).
  - Invisible background loop (`trayIcon.Visible = false`, zero taskbar clutter, zero notification icons).
  - Autostart registration via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- [x] **Single-Instance IPC with UIPI Traversal**:
  - `ChangeWindowMessageFilter` ensures Windows message passing across integrity levels.
  - Launching the `.exe` again smoothly restores the existing GUI; closing via `[X]` hides to stealth mode.
- [x] **3-Mode Persistence Engine**:
  - Stealth Driver Watcher (~1.8 MB RAM, invisible)
  - Tray Agent Mode (~2.7 MB RAM, quick access tray icon)
  - Disabled (RAM Only)
- [x] **Experimental DPI Stage Controls**:
  - Opt-in Command `0x09` dispatch for active DPI levels (600, 1000, 1200, 1600, 2400, 3200 DPI) with live RAM testing.
- [x] **Self-Contained Local Deployment**:
  - Automatic deployment to `%LocalAppData%\RedgearA20\RedgearA20.exe` using atomic move-away rename.

---

### Milestone 2: v0.2.0 — UI Overhaul, Modern Windows Integration & Safety Hardening (Current Release)
- [x] **Industrial Precision Utility UI Overhaul**:
  - Engineered dark-mode aesthetic with 1px hairline framing and card containers (`CreateSectionPanel`).
  - Pre-cached static GDI+ token system (`Theme` class) guaranteeing zero memory allocations or garbage collection stutter during window repainting.
  - Emerald (`#10B981`) primary functional accent, unified 36px action row (`Apply`, `Save`, `Reset`), and active-state preset border highlighting.
  - TrackBar modernization with `TickStyle.None` and dynamic percentage readout.
- [x] **Native Windows 10 & 11 Dark Title Bar**:
  - Intercepted window handle creation to apply `DWMWA_USE_IMMERSIVE_DARK_MODE` via `dwmapi.dll` P/Invoke.
- [x] **Per-Monitor V2 High-DPI Scaling & Modern Common Controls**:
  - Embedded native application manifest (`src/app.manifest`) declaring Per-Monitor V2 DPI awareness and `Microsoft.Windows.Common-Controls` v6.
- [x] **Multi-Threaded Hardware Concurrency & Safe Mutex Dispatch**:
  - Offloaded experimental DPI stage testing and hardware mutex synchronization (`TaskSchedulerHelper.HARDWARE_MUTEX_NAME`) to background worker tasks (`Task.Run` with `async`/`await`), eliminating UI thread freezes.
- [x] **Fault-Tolerant Storage & Crash Diagnostics**:
  - Atomic profile serialization via temporary file replacement (`File.Replace`), eliminating risk of corrupt JSON files during abrupt system shutdowns.
  - Global unhandled exception trapping logged to `%LocalAppData%\RedgearA20\crash.log`.
  - Non-destructive query access (`0`) in HID device path discovery.
  - Automatic stale device handle invalidation on USB disconnect.
  - Re-entrancy protection for rapid USB hotplugging (`isHotplugHandling`).
  - Strict hex input sanitization and validation (`TrySyncHexColor`).
- [x] **Dual-Architecture Build Pipeline**:
  - Standardized `build.bat` with automated 64-bit and 32-bit `csc.exe` compiler detection, manifest embedding, and explicit exit codes.
- [x] **Foundational Strategic Documentation**:
  - Authored the comprehensive Developer Guardrails bible, 70+ Feature Masterlist, formal Design System tokens, and product specification docs.

---

### Milestone 3: v0.3.0 — Extended RGB Effects & Lighting Engine
*Target: Q1 2027*

- [ ] **Configurable Breathing Mode**:
  - Expose hardware modes `0x80` through `0x85` with selectable cycle periods (slow pulse to fast pulse).
- [ ] **Cyclic Spectrum Mode**:
  - Expose hardware modes `0x90` through `0x95` with customizable rotation speeds.
- [ ] **Instant LED Off / Stealth Mode**:
  - Hardware mode `0x87` to completely disable mouse lighting with one click.
- [ ] **Named Color Profile System**:
  - Save, load, and share custom lighting presets via lightweight JSON files (`Valorant.json`, `NightMode.json`, `Work.json`).
- [ ] **Auto-Sleep / Idle Dimming (Optional)**:
  - Detect system idle time and gently dim lighting after $N$ minutes of inactivity, instantly restoring upon movement.

---

### Milestone 4: v0.4.0 — Sensor, Polling Rate & Per-Stage Customization
*Target: Q2 2027*

- [ ] **DPI Stage Pruning (Command `0x09`)**:
  - Enable players to reduce the default 6 DPI stages down to 2 or 3 active stages using the hardware bitmask, eliminating redundant DPI cycling during competitive matches.
- [ ] **Independent DPI Stage Color Tagging**:
  - Assign distinct colors per DPI stage (e.g. Stage 1 = Lavender, Stage 2 = Cyan, Stage 3 = Crimson Red).
- [ ] **Hardware USB Polling Rate Selector (Command `0x11`)**:
  - Safe UI control for $125\text{Hz}$, $250\text{Hz}$, $500\text{Hz}$, and $1000\text{Hz}$ switching.

---

### Milestone 5: v0.5.0 — Hardware Button Remapping (Experimental)
*Target: Mid 2027*

- [ ] **Side Button Customization (Command `0x15` / `0x16`)**:
  - Safely rebind Button 4 (Forward) and Button 5 (Back) to custom actions (Middle Click, Media Play/Pause, Mute, Volume Control).
- [ ] **Hardware Fail-Safe Interlock**:
  - Ensure Left Click and Right Click bindings are strictly locked against accidental unbinding or invalid register values to prevent device input lockouts.
- [ ] **Sniper / DPI Clutch Mode**:
  - Temporary sensitivity drop while holding a designated side button.

---

### Milestone 6: v1.0.0 — Multi-Device & Community Profiles
*Target: Late 2027*

- [ ] **Device Profile Abstraction**:
  - Modular device definition layer allowing support for other budget gaming mice powered by Instant Microelectronics, SinoWealth, or Holtek chipsets.
- [ ] **Community Device Contribution Guide**:
  - Step-by-step documentation for users to submit USB HID descriptors and packet captures for new devices.
- [ ] **Portable MSIX / Single-File Release Pipeline**:
  - Automated GitHub Actions build and release packaging.

---

## Out of Scope (Explicit Non-Goals)

To preserve the utility's lightweight nature and protect mouse hardware, the following are explicitly out of scope:

* **No Background Telemetry or Mandatory Accounts**: We will never require logins, cloud syncing, or background analytic daemons.
* **No Multi-Zone Addressable ARGB**: The Redgear A20 uses a single diffused hardware LED zone; multi-zone wave animations do not physically exist on this PCB.
* **No Firmware Overclocking beyond $1000\text{Hz}$**: The OEM microcontroller operates at Full-Speed USB 2.0 ($1000\text{Hz}$ max); pushing higher packet frequencies risks input desynchronization.
