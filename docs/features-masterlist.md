# Feature Masterlist & Prioritization Matrix

> **Core Philosophy**: Zero-bloat, standalone, hardware-first utility. Native performance without telemetry, web wrappers, or bloated background services.

---

## Category 1: Must-Have Features (Industry Baseline / Table Stakes)
*The non-negotiable baseline that every modern peripheral software must provide.*

* **Full Button Remapping**
* **Custom DPI Stages & Stage Pruning**
* **USB Polling Rate Switching (125 Hz – 1000 Hz)**
* **Basic RGB Lighting Control (Static, Breathing, Off) & Color Picker / Hex Input**
* **Media, Volume & System Function Key Binding**
* **Debounce Delay Tuning (Anti-Double-Click)**
* **Left/Right Click Lockout Fail-Safe**
* **Battery Health & Low-Power Warning (for wireless models)**
* **Windows Cold-Boot & S3/Modern Standby Wake Persistence**
* **One-Click Factory Default Reset**

---

## Category 2: High-Utility Features (Zero-Bloat Sweet Spot)
*High-value features that elevate the driver into an exceptional tool without introducing memory or CPU bloat.*

* **Macros & Multi-Key Hotkey Chaining**
* **Sniper Button / DPI Clutch**
* **Per-Application & Per-Game Auto-Profile Switching**
* **Lift-Off Distance (LOD) Calibration**
* **Independent X/Y Axis Sensitivity**
* **Layer Shifting (Fn-Shift / Modifier Layers)**
* **Sub-2 MB Stealth Driver Watcher (Zero GUI, Zero Tray Clutter)**
* **Instant Win32 USB Hotplug Persistence (`<50 ms`)**
* **Unlimited Named JSON Profiles (Import / Export / Sharing)**
* **Volatile RAM-Only Sandbox Testing**
* **Onboard Hardware Memory Flashing**
* **Idle Screen-Off Auto-Dimming**
* **Angle Snapping & Motion Smoothing Toggle**
* **Lightweight On-Screen Display (OSD) for DPI & Profile Swapping**
* **Hardware Double-Click / Switch Chatter Diagnostic Tester**
* **Single Standalone Portable Executable (Zero Telemetry, Zero Accounts)**

---

## Category 3: Optional Features (Enthusiast / Power-User)
*Valuable additions for power-users, niche workflows, and multi-device setups, but not required for everyday usage.*

* **Multi-Vendor OEM Chipset Support (Instant, SinoWealth, Holtek, PixArt)**
* **Multi-Device Management (Mice, Keyboards, Keypads)**
* **Real-Time USB HID Feature Report Packet Inspector**
* **Headless CLI & Local Automation API**
* **Cross-Platform Portable Executables (Windows, Linux, macOS)**
* **Surface / Mousepad Auto-Calibration**
* **Custom Mouse Acceleration Curve Editor (Raw Accel Alternative)**
* **OpenRGB & SignalRGB Bridge**
* **Anti-Cheat Tournament Mode (Hard-Locks Software Macros for LAN Safety)**
* **Precision Scroll Wheel Tuning (Custom Pixel/Line Stepping per Notch)**
* **Global Ambidextrous / Left-Handed One-Click Inversion**
* **Rapid Fire / Turbo Action Toggles**
* **Auto-Clicker with Customizable Intervals**
* **Live Clicks-Per-Second (CPS) Meter**
* **Physical Microswitch Click Counter & Lifetime Wear Tracker**
* **Kiosk / Tournament Read-Only Lockdown (Password Protection)**
* **Community Device Descriptor & VID/PID Mapper**

---

## Category 4: Excluded Features (Heaviness Exceeds Usefulness)
*Features that bloat software into 500 MB background monsters by requiring continuous screen scraping, audio capture, background web servers, kernel drivers, or heavy global hooks.*

* **Screen Ambient Color Sync** *(requires constant GPU/DXGI frame grabbing and high CPU/GPU load)*
* **System Audio Visualizer Reactive Lighting** *(requires continuous audio loopback & FFT processing)*
* **Localhost Web Remote / Mobile Phone Control** *(requires running a background web/socket server)*
* **Discord Rich Presence Integration** *(requires persistent IPC hooks and Discord SDK bloat)*
* **Windows Game Bar (Xbox Game Bar) Widget** *(requires UWP/MSIX packaging & WinRT overhead)*
* **Stream Deck / Loupedeck Plugin Integration** *(requires persistent background local daemon)*
* **Home Assistant / MQTT Smart Home Room Lighting Sync** *(requires network stack & Wi-Fi polling)*
* **Cursor Heatmap & Continuous Click Tracker** *(requires continuous global input hooks & disk logging)*
* **Lifetime Movement Odometer** *(unnecessary background tracking and disk writes)*
* **Sensor Jitter & Polling Rate Stability Grapher** *(complex benchmarking UI better suited for standalone tools)*
* **Real-Time Input Latency Benchmark Tool** *(laboratory-grade scope; out of place in a driver)*
* **Mouse Gestures & Directional Stroke Actions** *(complex global mouse hooks that interfere with game aim)*
* **Radial / Pie Action Menu on Mouse Hold** *(requires rendering in-game desktop overlays)*
* **Multi-Monitor Cursor Teleporter & Edge Wrap-Around** *(interferes with OS window managers)*
* **Microcontroller Firmware Dumper & Flasher** *(extreme device bricking risk for normal users)*
* **Portable Virtual HID Driver Emulation Mode** *(requires kernel-level driver signing)*
* **Humanized Macro Jitter** *(crosses into gray-hat/bannable automation)*
* **Profile Cryptographic Integrity Checksums** *(enterprise-level overkill for local mouse configs)*
