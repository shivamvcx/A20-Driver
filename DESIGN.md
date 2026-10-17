# Design System Tokens: Redgear A20 Control Center

<!-- impeccable:design-schema 1 -->

## Art Direction
**Industrial Precision Utility**: An engineered dark-mode desktop instrument panel with disciplined 1px hairline framing, crisp Windows system typography, and a single functional emerald accent that prioritizes visual scanability and instant hardware telemetry over cosmetic decoration.

---

## Design Tokens (WinForms GDI+ Ready)

### 1. Color Palette

#### Base Neutrals
| Token | Hex | WinForms GDI+ Expression | Role / Application |
|---|---|---|---|
| `--color-canvas` | `#121316` | `Color.FromArgb(18, 19, 22)` | Form background canvas |
| `--color-surface` | `#1A1C22` | `Color.FromArgb(26, 28, 34)` | Group panel cards, header bar |
| `--color-surface-elevated` | `#242731` | `Color.FromArgb(36, 39, 49)` | Inputs, textboxes, dropdowns, secondary buttons |
| `--color-surface-hover` | `#2D313E` | `Color.FromArgb(45, 49, 62)` | Hover state for secondary buttons |
| `--color-border` | `#2B2F3A` | `Color.FromArgb(43, 47, 58)` | 1px hairline structural frames and dividers |
| `--color-border-subtle` | `#20232A` | `Color.FromArgb(32, 35, 42)` | Inner container dividers |

#### Text & Typography Neutrals
| Token | Hex | WinForms GDI+ Expression | Contrast Ratio (WCAG) | Role |
|---|---|---|---|---|
| `--color-text-primary` | `#F1F3F7` | `Color.FromArgb(241, 243, 247)` | 14.2:1 (AAA) | Headings, active values, crisp titles |
| `--color-text-secondary` | `#8D93A5` | `Color.FromArgb(141, 147, 165)` | 5.1:1 (AA) | Labels, field descriptions, metadata |
| `--color-text-muted` | `#5A6072` | `Color.FromArgb(90, 96, 114)` | 3.2:1 | Disabled labels, helper hints |

#### Single Primary Accent (Emerald / Hardware Mint)
| Token | Hex | WinForms GDI+ Expression | Role |
|---|---|---|---|
| `--color-accent` | `#10B981` | `Color.FromArgb(16, 185, 129)` | Primary button fill (`Save Profile`), active radio, status dot |
| `--color-accent-hover` | `#059669` | `Color.FromArgb(5, 150, 105)` | Primary button hover state |
| `--color-accent-text` | `#FFFFFF` | `Color.White` | Text on accent button (5.4:1 contrast) |
| `--color-accent-dim` | `#064E3B` | `Color.FromArgb(6, 78, 59)` | Selected radio background pill tint |

#### Functional Semantic Roles (Strict Hardware Feedback)
| Token | Hex | WinForms GDI+ Expression | Role |
|---|---|---|---|
| `--color-warning` | `#F59E0B` | `Color.FromArgb(245, 158, 11)` | Experimental section border, badge, test button text |
| `--color-warning-surface` | `#231C13` | `Color.FromArgb(35, 28, 19)` | Experimental section background tint |
| `--color-danger` | `#EF4444` | `Color.FromArgb(239, 68, 68)` | Exit App button text and border |
| `--color-danger-surface` | `#281416` | `Color.FromArgb(40, 20, 22)` | Exit App button background |
| `--color-terminal-text` | `#10B981` | `Color.FromArgb(16, 185, 129)` | Monospace diagnostic report stream text |

---

### 2. Typography Scale (Windows System Fonts Only)

Strictly relies on pre-installed Windows system fonts (`Segoe UI` and `Consolas`) with zero runtime memory allocation for custom font tables.

| Scale Token | Font Family | Size | Weight | Line Height | Usage in WinForms |
|---|---|---|---|---|---|
| `--font-heading` | `Segoe UI` | 13px (9.75pt) | Bold (600) | 18px | Section titles, header status (`new Font("Segoe UI", 9.75f, FontStyle.Bold)`) |
| `--font-body-1` | `Segoe UI` | 12px (9.0pt) | SemiBold (500) | 16px | Buttons, inputs, active value labels (`new Font("Segoe UI", 9.0f, FontStyle.Regular)`) |
| `--font-body-2` | `Segoe UI` | 11px (8.25pt) | Regular (400) | 15px | Field labels, radio descriptions, badges (`new Font("Segoe UI", 8.25f, FontStyle.Regular)`) |
| `--font-mono` | `Consolas` | 11px (8.25pt) | Regular (400) | 16px | Hex code input, diagnostic USB packet console (`new Font("Consolas", 8.25f, FontStyle.Regular)`) |

---

### 3. Spacing Scale

Strict 4px geometric scale:
- `--space-1`: `4px` (gap between icon/dot and text, chip padding)
- `--space-2`: `8px` (button padding, element gap in rows)
- `--space-3`: `12px` (input padding, section row spacing)
- `--space-4`: `16px` (card internal padding, card gap)
- `--space-5`: `20px` (window outer margins)

---

### 4. Shape & Geometry

- `--radius-default`: `4px` (single uniform radius across buttons, cards, textboxes, and color chips)
- `--border-width`: `1px` (clean hairline borders using `Pen` width 1)

---

### 5. Layout Sizing Specifications

- **Total Form Size**: `580px` width $\times$ `835px` height (Fixed single border)
- **Client Area Width**: `564px` (accounting for Win32 non-client borders)
- **Body Panel Width**: `532px` centered (16px left/right margins)
- **Header Docked Height**: `44px`
- **Primary Action Buttons**: Height `38px`
- **Diagnostic Console**: Height `180px`
