# 🔌 Redgear A20 USB HID Protocol Specification

This document details the reverse-engineered USB Human Interface Device (HID) communication protocol for the **Redgear A20 RGB Gaming Mouse** (`VID: 0x18F8 & PID: 0x0FC0`).

---

## 1. Physical Device & Interface Architecture

* **Vendor ID (VID)**: `0x18F8` (Instant Microelectronics OEM)
* **Product ID (PID)**: `0x0FC0`
* **Target Endpoint**: Vendor-defined Collection **Col04** (`UsagePage 0xFF01`, `Usage 0x0001`)
* **Fallback Endpoint**: Vendor-defined Collection **Col03** (`UsagePage 0xFF00`, `Usage 0x0001`)
* **Communication Mechanism**: USB HID Feature Reports via `HidD_SetFeature`
* **Report ID**: Always `0x07` (Byte 0)
* **Packet Size**: Exactly 8 Bytes

```
Standard 8-Byte Feature Report Layout:
[ Byte 0: Report ID (0x07) ]
[ Byte 1: Command Opcode   ]
[ Byte 2: Parameter 1      ]
[ Byte 3: Parameter 2      ]
[ Byte 4: Parameter 3      ]
[ Byte 5: Parameter 4      ]
[ Byte 6: Parameter 5      ]
[ Byte 7: Parameter 6      ]
```

---

## 2. Verified Protocol Commands

### Command `0x14` — DPI Stage RGB Color Control
Programs the 12-bit RGB color for one of the mouse's 6 internal DPI stages.

```
[0x07, 0x14, Byte2, Byte3, 0x00, 0x00, 0x00, 0x00]
```

* **Byte 2**: `(StageIndex << 5) | (Green_Inv & 0x0F)`
  - `StageIndex`: DPI Stage index ($0$ to $5$).
  - `Green_Inv`: 4-bit Active-Low Green channel ($0$ to $15$).
* **Byte 3**: `((Red_Inv & 0x0F) << 4) | (Blue_Inv & 0x0F)`
  - `Red_Inv`: 4-bit Active-Low Red channel ($0$ to $15$).
  - `Blue_Inv`: 4-bit Active-Low Blue channel ($0$ to $15$).
* **Bytes 4–7**: `0x00, 0x00, 0x00, 0x00` (Padding).

#### Active-Low PWM Architecture:
The Redgear A20 microcontroller uses an active-low PWM drive for its RGB channels:
* `0x0` ($0$) = **$100\%$ Full Brightness (Full ON)**
* `0xF` ($15$) = **$0\%$ Brightness (Full OFF)**

$$\text{Active-Low Level} = 15 - \text{round}\left(\frac{\text{Percent}}{100} \times \frac{\text{Brightness}}{100} \times 15\right)$$

*Example (Pure Purple at 100% Brightness)*:
- Red: $100\%$ ON $\rightarrow$ `Red_Inv = 0x0`
- Green: $0\%$ OFF $\rightarrow$ `Green_Inv = 0xF`
- Blue: $100\%$ ON $\rightarrow$ `Blue_Inv = 0x0`
- Result: `Byte 2 = (Stage << 5) | 0x0F`, `Byte 3 = 0x00`

---

### Command `0x13` — LED Lighting Mode
Controls the lighting animation mode and spectrum cycle permissions.

```
[0x07, 0x13, Byte2, Byte3, 0x00, 0x00, 0x00, 0x00]
```

* **Byte 2 (`0x7F`)**: 7-bit mask of allowed cycle colors (`0x7F` = all 7 colors enabled).
* **Byte 3 (`ModeByte`)**:
  - `0x86`: **Solid Static Mode** (Locks LED to steady light with zero breathing/fading).
  - `0x80 | Speed`: **Breathing Mode** (Fades in and out using the active DPI stage color).
  - `0x90 | Speed`: **Cyclic Spectrum Mode** (Rainbow cycle through enabled colors).
  - `0x87`: **LED Off** (Completely turns off LED).
* **Bytes 4–7**: `0x00, 0x00, 0x00, 0x00`.

---

### Command `0x20` — Commit to Onboard Memory (EEPROM)
Instructs the microcontroller to write active RAM settings into its non-volatile memory block.

```
[0x07, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]
```

* *Note*: On certain hardware revisions of the A20, this command executes cleanly but requires further protocol analysis for cold-reboot persistence.

---

## 3. Experimental / Unverified Protocol Commands

### Command `0x09` — Sensor DPI Mode & Stage Bitmask
Configures the active DPI stage and the mask of enabled stages.

```
[0x07, 0x09, Byte2, Byte3, Byte4, 0x00, 0x00, 0x00]
```

* **Byte 2 (`0x40 | StageIndex`)**: Active DPI level ($0x40$ for Stage 0 up to $0x45$ for Stage 5).
* **Byte 3 (`0x08`)**: Sensor configuration flag.
* **Byte 4 (`EnabledStagesBitmask`)**: 6-bit mask of enabled stages (e.g. `0x3F` for all 6 stages).
* **Bytes 5–7**: `0x00, 0x00, 0x00`.

---

## 4. Hardware Safety Guidelines

1. **Mandatory Delay**: Enforce a minimum **$50\text{ms}$ delay** between consecutive Feature Reports to prevent 8-bit MCU buffer saturation.
2. **Handle Management**: Open device handles only when transmitting, call `HidD_FlushQueue`, and close immediately.
3. **Decoupled Flash Writes**: Never execute Command `0x20` in automated loops.
