# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users
Budget gamers and PC enthusiasts owning the Redgear A20 RGB Gaming Mouse (Instant Microelectronics A704 / A704F chipset, VID: `0x18F8`, PID: `0x0FC0`). They want full control over their mouse lighting and DPI to match their setup aesthetic, but were abandoned by official vendor software (`Gaming Mouse 2.0.exe`) that crashed, consumed excessive RAM, leaked background processes, and failed to hold settings across system reboots.

## Product Purpose
Provide a standalone, zero-bloat, open-source Windows hardware utility that unlocks true 12-bit active-low PWM RGB lighting control, eliminates default rainbow breathing in favor of solid static illumination, and guarantees persistent hardware settings across cold boots, sleep cycles, and USB hotplugs without background bloat, telemetry, or system tray clutter. Success means users set their configuration once, close the window, and have their hardware settings automatically restored seamlessly with under 2 MB RAM usage and 0% CPU.

## Positioning
Unlike the official vendor software (`Gaming Mouse 2.0.exe`) and heavy gaming peripheral suites (e.g., Razer Synapse, Corsair iCUE) that require hundreds of megabytes of RAM, background web servers, telemetry, and user accounts, Redgear A20 Control Center is a single portable ~65 KB binary with zero external dependencies. It overcomes the A704 hardware's lack of onboard non-volatile EEPROM by implementing a featherweight native Win32 `WM_DEVICECHANGE` Stealth Driver Watcher that uses `SetProcessWorkingSetSize` memory compression to idle at ~1.8 MB RAM.

## Operating Context
Runs on 64-bit Windows 10 and Windows 11 desktop environments. Communicates directly with mouse hardware over USB HID Feature Reports (Report ID `0x07`, UsagePage `0xFF01`). Runs either as an interactive desktop GUI for configuration or silently in the background (`Stealth Driver Watcher`) launched via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Intercepts system USB arrival/removal notifications to restore volatile SRAM settings instantly upon hotplug.

## Capabilities and Constraints
- **Capabilities**:
  - Full 12-bit RGB color depth ($16 \times 16 \times 16$ levels) via Active-Low PWM register encoding.
  - Hardware mode `0x86` for permanent non-fading Solid Static LED lighting (overriding stock rainbow breathing).
  - Proportional PWM brightness dimming (5%–100%) maintaining exact hue balance.
  - Multi-stage DPI selection and tuning (Command `0x09` stages 0–5: 600, 1000, 1200, 1600, 2400, 3200 DPI).
  - 3 persistence modes: Stealth Driver Watcher (~1.8 MB RAM, zero tray icon), Tray Agent Mode (~2.7 MB RAM with tray icon), and Disabled (RAM only).
  - Embedded monospace real-time USB Feature Report diagnostic log with clipboard export.
  - Atomic auto-deployment to `%LocalAppData%\RedgearA20\RedgearA20.exe` for seamless background persistence.
- **Constraints**:
  - Hardware constraint: Instant Microelectronics A704 / A704F IC physically lacks onboard non-volatile EEPROM/flash memory for custom RGB; custom lighting resides in volatile SRAM and wipes whenever the 5V USB rail loses power.
  - Strict 50ms inter-packet delay required to prevent 8-bit MCU buffer overruns / GPIO starvation.
  - Ephemeral HID handle lifecycle (`CreateFile` -> send feature report -> `CloseHandle`).
  - Zero slider spamming: sliders only update local UI preview; zero USB packets transmitted while dragging.
  - Pure Win32 / standard .NET Framework 4.8 C# compilation via Windows built-in `csc.exe` (no external runtime, no NuGet, no Electron).

## Brand Commitments
- Independent, unofficial open-source community driver utility; explicitly disclaims endorsement by or affiliation with Redgear / Imagine Marketing Limited.
- Engineering identity: precision hardware control, transparent technical honesty, zero telemetry, zero accounts, zero bloat.
- Visual tone: purposeful hardware utility aesthetic inspired by Logitech Onboard Memory Manager (OMM) and OpenRGB.

## Evidence on Hand
- Functional C# codebase: `src/Program.cs` (2,389 lines, complete Win32 P/Invoke HID transport, UI, and stealth watcher).
- Build system: `build.bat` using built-in Windows `csc.exe`.
- Standalone executable artifact: `release/RedgearA20-win-x64.exe` and checksum `release/RedgearA20-win-x64.sha256`.
- Hardware architectural specifications and protocol reverse-engineering docs in `docs/` (`architecture-decisions-and-guardrails.md`, `protocol-notes.md`, `features-masterlist.md`, `devlog.md`, `changelog.md`).
- Existing UI screenshot asset: `assets/control-center-ui.png`.

## Product Principles
- **Hardware Safety First**: Never compromise hardware stability or drop mouse input packets; enforce inter-packet pacing and volatile RAM testing before saving.
- **Zero-Bloat Sovereignty**: Keep binary lightweight (~65 KB), dependency-free, and resource-minimal (~1.8 MB RAM, 0% CPU); strictly reject telemetry, login walls, and heavy background runtimes.
- **Transparent Control**: Expose direct hardware reality (register hex, feature reports, diagnostic log) rather than obscuring it behind artificial abstractions.
- **Silent Reliability**: Settings must persist across reboots, sleep/wake transitions, and USB port changes without demanding user attention or cluttering the system tray.

## Accessibility & Inclusion
- High-contrast text and clear numerical readouts for hex color codes and DPI values.
- Keyboard accessible navigation across all controls, with dedicated confirmation dialogs on destructive or exit actions to prevent accidental state loss.
