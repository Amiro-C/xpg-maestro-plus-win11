# ADATA XPG 3-Segment USB Display Controller for Windows 11

[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-blue.svg)](https://microsoft.com/windows)
[![Device](https://img.shields.io/badge/USB%20HID-VID%200x125F%20%3A%20PID%200x7601-green.svg)](docs/xpg_display_communication.md)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

A standalone **Windows 11** utility and driver for the **ADATA XPG 3-Segment USB Hardware Display** (VID `0x125F`, PID `0x7601`). 

Reverse-engineered via **Wireshark / USBPcap**, this tool sends real-time system telemetry to all 3 segments of the display:
1. **CPU TEMP** (°C) - Real-time CPU Package Temperature with calibration & inertia physics.
2. **RAM USAGE** (%) - Real-time System Memory load percentage.
3. **CPU USAGE** (%) - Real-time CPU Load percentage.

---

## 🌟 Features

* **3-Segment Support**: Updates CPU Temp (°C), RAM Load (%), and CPU Load (%) simultaneously at 1 Hz.
* **System Tray Integration**: Minimize to tray, close-to-tray button, and notify icon context menu.
* **Windows Autostart**: One-click autostart configuration via Windows Registry (`HKCU\...\Run`), running silently in the system tray on boot (`--minimized`).
* **Temperature Calibration**: Built-in temperature offset slider (`+/- 20°C`) to match exact AIO / CPU sensor hardware.
* **Zero Dependencies**: Native C# application compiled with Win32 HID API (`hid.dll`) and WinForms.
* **Python Frontend**: Includes an optional Python 3 Tkinter GUI and CLI script (`src/xpg_controller.py`).

---

## 📂 Repository Structure

```
AdataXPG_Windows/
├── .gitignore                      # Git ignore rules
├── LICENSE                         # MIT License
├── README.md                       # Main documentation
├── bin/
│   └── XpgDisplayController.exe    # Pre-compiled standalone Windows 11 executable
├── src/
│   ├── XpgDisplayController.cs     # C# Source code (Driver + GUI + System Tray + Autostart)
│   ├── XpgDisplayController.csproj # Visual Studio / .NET project file
│   └── xpg_controller.py           # Python 3 frontend wrapper
└── docs/                           # Reverse engineering captures & protocol specifications
    ├── xpg_display_communication.md# Detailed USB HID protocol & byte map
    ├── xpg_display_protocol_summary.json
    ├── xpg_display_payloads.json
    ├── xpg_display_packets.json
    └── xpg_display_capture.pcapng  # Binary Wireshark USBPcap capture file
```

---

## ⚡ Quick Start

### 1. Download & Run
Directly launch the pre-compiled executable in `bin/XpgDisplayController.exe`:
```powershell
.\bin\XpgDisplayController.exe
```

### 2. Command Line Arguments
* **Automatic Monitoring Mode (1 Hz update)**:
  ```powershell
  .\bin\XpgDisplayController.exe --monitor
  ```
* **Silent Autostart Mode (Runs in System Tray)**:
  ```powershell
  .\bin\XpgDisplayController.exe --minimized
  ```
* **Manual Set Values (TEMP, RAM%, CPU%)**:
  ```powershell
  .\bin\XpgDisplayController.exe --set 45 35 15
  ```

---

## 🛠️ Building from Source

### Option A: Using Microsoft C# Compiler (`csc.exe`)
No Visual Studio required! Compiles natively on Windows:
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:"bin\XpgDisplayController.exe" "src\XpgDisplayController.cs" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll
```

### Option B: Using .NET CLI / Visual Studio
```powershell
cd src
dotnet build -c Release
```

---

## 🔬 Protocol Specification (Wireshark Analysis)

* **Vendor ID (`idVendor`)**: `0x125F` (ADATA Technology Co., Ltd.)
* **Product ID (`idProduct`)**: `0x7601` (XPG Segment Display)
* **USB Class**: `USB HID (0x03)`
* **Transfer Type**: `USB HID SET_REPORT Control Transfer` (`bmRequestType = 0x21`, `bRequest = 0x09`)
* **Report ID**: `7` (`wValue = 0x0207`)
* **Payload Length**: `64 Bytes`

### HID Report Payload Map (64 Bytes)

| Offset | Hex Value | Parameter |
| :--- | :--- | :--- |
| `byte[0]` | `0x07` | **Report ID** (Always 7) |
| `byte[1]` | `0xA1` | **Command Magic Byte** |
| `byte[2]` | `0x01` | **Version / Mode Flag** |
| `byte[3..5]` | `0x00` | **Padding** |
| `byte[6]` | `0x0A` | **Marker 1** |
| `byte[7]` | `0x28`..`0x5F` | **Segment 1: CPU TEMP (°C)** |
| `byte[8]` | `0x0A` | **Marker 2** |
| `byte[9]` | `0x00`..`0x64` | **Segment 2: RAM USAGE (%)** |
| `byte[10]` | `0x00`..`0x64` | **Segment 3: CPU USAGE (%)** |
| `byte[11..13]` | `0x07` | **Status / Control Flags** |
| `byte[14..63]` | `0x00` | **Zero Padding** |

For full Wireshark capture logs and JSON dumps, see [`docs/xpg_display_communication.md`](docs/xpg_display_communication.md).

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
