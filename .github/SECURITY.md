# 🛡️ Security Policy

The **Redgear A20 Control Center** project takes hardware safety, memory safety, and user security seriously. Because this software communicates directly with peripheral microcontroller hardware via raw USB HID Feature Reports, we maintain strict safety guardrails.

---

## 📦 Supported Versions

Security and stability updates are provided for the following versions:

| Version | Supported | Notes |
| :---: | :---: | :--- |
| `0.4.x` | ✅ Yes | Current release line |
| `0.3.x` | ⚠️ Critical fixes only | Please update; the in-app updater installs the latest stable build |
| `< 0.3.0` | ❌ No | Older releases and exploratory reverse-engineering builds |

---

## 🔍 Scope of Security Considerations

We welcome reports regarding:
1. **Hardware Communication Safety**: USB Feature Reports or parameters that could induce microcontroller buffer overflows, MCU deadlocks, or GPIO scan loop starvation.
2. **Win32 P/Invoke Memory Safety**: Unhandled pointer dereferences, buffer overflows, or handle leaks in `hid.dll` or `setupapi.dll` calls.
3. **Privilege & Execution Safety**: Any vector allowing unauthorized privilege escalation, persistence, or malicious payload execution.
4. **Update & Installer Integrity**: Ways to make the in-app updater install a binary that doesn't match its published SHA-256 checksum, or to tamper with the per-user Setup installer or uninstaller.
5. **Keyboard Shortcut Safety**: Ways for a profile to make the mouse send blocked combinations (such as Ctrl + Alt + Del) or keys outside the allow-list.

*(Note: Third-party physical hardware damage resulting from modifying source code to bypass safety delays or send arbitrary unknown opcodes to the device is outside project liability; please see our [DISCLAIMER](README.md#disclaimer).)*

---

## 🚨 Reporting a Vulnerability

If you discover a security vulnerability or a critical hardware bug, please report it privately:

### Option 1: GitHub Private Vulnerability Reporting (Preferred)
1. Navigate to the [Security Advisories](https://github.com/shivamvcx/A20-Driver/security/advisories) tab of this repository.
2. Click **Report a vulnerability** to open a confidential report directly with the project maintainer.

### Option 2: Direct Email
Send an encrypted or plain email to:
* **Maintainer**: Shivam
* **Email**: [shivamsinhav1@proton.me](mailto:shivamsinhav1@proton.me)
* **Subject Line**: `[SECURITY VULNERABILITY] Redgear A20 Control Center`

Please include:
* Description of the vulnerability or hardware lockup condition.
* Exact reproduction steps or packet payload.
* Device hardware revision, firmware details, and Windows OS version.

---

## ⏱️ Response & Disclosure Timeline

* **Acknowledgment**: We aim to acknowledge receipt within **48 hours**.
* **Assessment & Fix**: A patch or mitigation will be developed and validated in a private branch before public disclosure.
* **Public Release**: A patched release (`v0.4.x`) and security advisory will be published simultaneously with credit to the reporter.
