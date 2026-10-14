# 🛠️ Engineering DevLog: Redgear A20 Open Driver Project

---

## 📌 Project Overview
* **Target Device**: Redgear A20 RGB Gaming Mouse
* **Chipset**: Instant Microelectronics OEM USB Gaming Mouse Controller
* **Hardware Identifiers**: `VID: 0x18F8`, `PID: 0x0FC0`
* **Communication Interface**: USB HID Feature Reports, Report ID `0x07` (8 Bytes), Endpoint `UsagePage 0xFF01` (Col04)
* **Status**: v0.1.0 (Verified Solid Color & Brightness in RAM; EEPROM Save in Progress)

---

## 📅 Chronological Development Log

### Phase 1: Problem Definition & Hardware Identification
* **The Problem**: The official Redgear A20 driver software is abandoned, unstable, and fails to configure the mouse's RGB lighting to a fixed color (e.g. Pure Purple). The mouse defaults to an unchangeable slow rainbow spectrum cycle.
* **USB Enumeration**:
  - Scanning the Windows HID tree initially revealed composite interfaces.
  - The correct mouse hardware was identified as `VID: 0x18F8 & PID: 0x0FC0` ("USB GAMING MOUSE" by manufacturer "INSTANT").
  - The mouse exposes 6 HID collections. Probing showed that standard collections (`UsagePage 0x0001`) are locked by Windows `mouhid.sys` for exclusive mouse input, while vendor collection **Col04 (`UsagePage 0xFF01`, Usage `0x0001`)** accepts 8-byte Feature Reports.

---

### Phase 2: Reverse Engineering `Gaming Mouse 2.0.exe`
* Extracted the 32-bit PE binary from the vendor's InstallShield installer (`RedGear A20 RGB Gaming Mouse.[20181212].exe`).
* Disassembled the main hardware dispatch routine starting at virtual address **`VA 0x00408700 – 0x00408A50`**:
  - Buffer allocation: Global 8-byte buffer at address `0x005C1900`.
  - Report ID: Constant byte `0x07`.
  - Dispatch mechanism: Calls `HidD_SetFeature(hDevice, buffer, 8)`.

#### Discovered Command Table:
| Opcode | Routine VA | Functional Role |
| :---: | :---: | :--- |
| `0x10` | `0x00408780` | Button remapping matrix (Button ID $\rightarrow$ Action Code) |
| `0x11` | `0x00408840` | USB Polling rate ($125\text{Hz}$, $250\text{Hz}$, $500\text{Hz}$, $1000\text{Hz}$) |
| `0x13` | `0x004088B0` | LED Lighting Mode & Cycle Speed |
| `0x09` | `0x00408938` | Sensor DPI Mode & Enabled Stages Bitmask |
| `0x14` | `0x004089CE` | DPI Stage Colors (looped 6 times for stages 0–5) |
| `0x12` | `0x004089FB` | Sensitivity, Scroll lines per notch, Pointer speed |
| `0x20` | `0x004063A2` | Non-volatile Flash / EEPROM Commit |

---

### Phase 3: The Active-Low PWM Breakthrough (The "Green" Mystery)
* **Initial Test**: When testing Command `0x14` with standard RGB values for Purple ($\text{Red}=15, \text{Green}=0, \text{Blue}=15$), the mouse unexpectedly turned **100% solid bright Green**!
* **Disassembly Inspection (`VA 0x00408D07`)**:
  Examining the driver's color-parsing function revealed the hardware architecture:
  ```x86
  8B C7          MOV EAX, EDI         ; EDI = 255 (0xFF)
  0F B6 CB       MOVZX ECX, BL        ; BL = Raw Color Component
  2B C1          SUB EAX, ECX         ; EAX = 255 - Component (INVERTED!)
  C1 F8 04       SAR EAX, 4           ; (255 - Component) / 16 -> 4-bit Active-Low!
  ```
* **Discovery**: The microcontroller's internal LED drivers use **Active-Low PWM**:
  - `0x0` = $100\%$ Full Brightness (Full ON)
  - `0xF` ($15$) = $0\%$ Brightness (Full OFF)
* **The Fix**:
  - Setting $\text{Green} = 0$ had turned Green to **maximum full brightness** while turning Red and Blue off.
  - Inverting the registers ($\text{Red}=0, \text{Green}=15, \text{Blue}=0$) instantly produced **Pure Solid Purple** on the mouse!

---

### Phase 4: Static Solid vs. Breathing Mode Discovery
* During initial hardware testing, we observed that with Command `0x13` mode byte `0x80`, the LED breathed (faded in and out every 6 seconds).
* Inspecting address `VA 0x00408870 – 0x00408899`:
  - When `LED_TYPE == 0` (Fixed/DPI): Mode byte is `0x80 | (CycleTime & 0x07)` (Breathing mode).
  - When `LED_TYPE == 2` (Static):
    ```x86
    BA 06 00 00 00   MOV EDX, 6
    ...
    ADD CL, DL       ; CL = 0x80 | 6 = 0x86 !
    ```
* **Result**: Mode byte **`0x86`** forces the MCU into **Static Solid mode**, completely disabling the breathing/fading cycle.

---

### Phase 5: Input Incident Post-Mortem & Safety Architecture
* **Symptom**: During rapid testing of lighting loops, the mouse cursor moved and the scroll wheel scrolled, but Left & Right clicks stopped registering. A laptop reboot completely resolved the issue.
* **Forensic Diagnosis**:
  1. *Hardware Separation*: The optical sensor (SPI interrupt) and scroll wheel (timer/encoder pins) operate on hardware interrupts. Physical button switches are scanned in software via a GPIO debounce loop in the MCU's main firmware thread.
  2. *Buffer Saturation*: Rapidly sending back-to-back Feature Reports without inter-packet delays saturated the MCU's 8-bit endpoint buffer, starving the GPIO debounce scan loop.
  3. *Handle Stalling*: Abrupt process termination left orphaned HID endpoint buffers in Windows' `hidclass.sys`.
* **Permanent Safety Guardrails Established**:
  - **No Live Slider Spamming**: Sliders only update on-screen visual preview; zero packets sent during dragging.
  - **Single Explicit Apply**: Changes only transmit to the device on user click.
  - **Mandatory $50\text{ms}$ Delays**: Enforced between individual Feature Reports.
  - **Decoupled RAM vs EEPROM**: "Apply" only writes to volatile RAM; "Save" (`0x20`) is isolated behind a separate confirmation button.
  - **Immediate Handle Disposal**: Handle is opened, written, flushed via `HidD_FlushQueue`, and closed immediately.

---

### Phase 6: v0.1.0 Desktop Application Architecture
* Implemented a zero-dependency C# Windows Forms application (`src\Program.cs`).
* Integrated a native P/Invoke Win32 HID transport (`hid.dll` / `setupapi.dll`).
* Features implemented:
  - 12-bit Active-Low Precision Color Engine (Color dialog + `#RRGGBB` input + quick presets).
  - Proportional Brightness Dimming ($5\text{–}100\%$).
  - Separate "Apply to Mouse (RAM)" and "Save to Mouse Memory (0x20)" buttons.
  - Safe Reset button (restores verified default Cyan lighting).
  - Isolated "⚠️ EXPERIMENTAL - DPI" section (Command `0x09`) behind an opt-in checkbox.
  - Real-time Diagnostic Hex Log panel with clipboard copy.
* Automated 1-click build script (`build.bat`) utilizing the built-in Windows .NET Framework `csc.exe` compiler. Output binary size: **$\approx 25\text{ KB}$**.

---

### Phase 7: Current Findings & Open Research Items
* **LED Control**: 100% verified and functional.
* **Brightness Control**: 100% verified and functional.
* **Solid vs. Breathing**: 100% verified and functional.
* **Onboard Flash Memory Persistence (`0x20`)**:
  - *Observation*: While `0x20` is accepted with Win32 success and verified in `Gaming Mouse 2.0.exe` at `VA 0x004063A2`, certain hardware revisions of the A20 do not persist custom color registers across cold power reboots, reverting to default on unplug.
  - *Investigation Plan*: Analyze whether a secondary commit sentinel or unlock byte (such as the `0x5A 0xA5` markers observed in Report 0x07 bytes 17–18) is required prior to `0x20`.
