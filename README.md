# WinDebloat 🚀

> **A modern, dynamic Windows debloater and memory optimization suite for Windows 10 and 11.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![Framework: .NET 10](https://img.shields.io/badge/Framework-.NET%2010.0-512BD4.svg)](https://dotnet.microsoft.com/)

---

> [!WARNING]
> **USE AT YOUR OWN RISK — SYSTEM INTEGRITY WARNING**
> Modifying core Windows components, disabling system services, or removing preinstalled packages can cause system instability, break functionality, or brick your machine. Always create a **System Restore Point** using the built-in one-click tool before applying optimizations.

---

## 🌟 Key Features

### 🧠 Real-Time Memory Monitor
- Live physical RAM consumption, free memory, and utilization percentage gauge.
- Instant feedback on memory reclaimed after applying debloat actions.

### 🗑️ Bloatware Catalog (OEM & Consumer Bloat)
- **OEM Preloads**: Automatically detects companion and telemetry software from **HP, Dell, Lenovo, and ASUS** (e.g. *HP Smart, HP Print Companion, Dell Command Update, Dell SupportAssist, Lenovo Vantage, MyASUS*).
- **Consumer & Ad Preloads**: Identifies preinstalled sponsored app stubs (*TikTok, Instagram, Disney+, Spotify, Clipchamp, casual card games*).
- **Telemetry & Assistant Apps**: Disables or removes *Connected User Experiences (`DiagTrack`), WAP Push Routing (`dmwappushservice`), Feedback Hub, Tips, Cortana, and Copilot*.
- **Entertainment Hubs**: Identifies unused MSN feeds, maps managers, and legacy media players.

### 📦 Dynamic Scan Component for User-Added Bloat
- Scans both 64-bit and 32-bit machine (`HKLM`) and user (`HKCU`) application databases for installed third-party desktop software.
- Filters out essential Microsoft Visual C++ runtimes and Windows security updates.
- Inspects publisher, version, install location, and provides direct uninstaller execution.

### ⚡ Reversible Core System Tweaks
- **Start Menu Web Search**: Prevents `SearchHost.exe` from loading the Edge WebView2 browser engine and sending queries to Bing, speeding up local file/app search.
- **Windows Widgets & News Feed**: Hides taskbar widget buttons and terminates background feed caching (`MicrosoftStartFeedProvider`).
- **Browser Background Mode**: Enforces policies stopping Google Chrome and Microsoft Edge from pre-launching stealth background processes or lingering in memory after closure.

### ⚙️ Windows Services Optimization
- Safely configures telemetry, Superfetch (`SysMain`), and unused network/consumer services to `Disabled` in the Windows Service Control Manager and registry.
- Detects dependent services to prevent system lockups.

### ↩️ Persistent JSON Undo & Revert Engine
- **Persistent Disk Storage**: Every change, disabled service, and removed startup entry is serialized to a persistent JSON history database (`%LOCALAPPDATA%\WinDebloat\undo_history.json`).
- **Survives Reboots & App Restarts**: Historical modifications reload automatically into the **↩️ Undo / Revert** tab whenever you open the app.
- **Dedicated Tab**: Keeps main debloat candidate views clean and focused, preventing clutter.
- **Card-Level Revert**: Click `↩️ Revert Change` directly on any item in the Undo tab to restore it.
- **Batch Revert**: Select multiple items in the Undo tab and click `↩️ Revert Selected` to restore them in one go.
- **Clear History**: Includes a dedicated `🗑️ Clear History` button to purge saved history records from disk when you're satisfied with your optimizations.

### 🛡️ Smart Elevation & Modern UI
- **Elevation When Needed**: Runs in standard mode without forcing UAC upfront; automatically prompts for elevation only when applying actions that require Administrator rights.
- **Modern Dark Dialogs**: Sleek, borderless dark-slate modal dialogs with soft drop shadows and contextual icons instead of dated Win32 message boxes.
- **Reboot Indicators**: Identifies tweaks requiring a system restart or Windows Explorer reload with a purple `REBOOT NEEDED` badge, and offers a one-click restart prompt upon completion.

---

## 🖥️ System Requirements

- **Operating System**: Windows 10 (1809+) or Windows 11 (64-bit recommended)
- **Runtime**: [.NET 10.0 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Privileges**: Standard user for viewing and user tweaks; Administrator privileges for services, policies, and restore point creation.

---

## 🚀 Getting Started

### Running the Pre-Built Binary
1. Clone or download the repository.
2. Launch `run-debloat.cmd` or run `publish\WinDebloat.exe`.
3. *(Recommended)* Click **🛡️ Create Restore Point** in the top warning banner.
4. Select the optimizations you want to apply, or click **⭐ Select Recommended**.
5. Click **⚡ Apply Selected Actions**.

---

## 🛠️ Building from Source

Ensure you have the .NET 10 SDK installed:

```powershell
# Clone the repository
git clone https://github.com/your-username/WinDebloat.git
cd WinDebloat

# Restore dependencies and build
dotnet build -c Release

# Publish self-contained or framework-dependent executable
dotnet publish -c Release -o ./publish
```

---

## 📂 Project Architecture

```
WinDebloat/
├── Models/
│   └── DebloatItem.cs         # Core debloat item model with ApplyAction and UndoAction
├── Services/
│   ├── ScannerService.cs      # Scanners for startup, bloatware, tweaks, services, and user apps
│   └── SystemHelper.cs        # Registry, process execution, elevation, and memory helpers
├── ViewModels/
│   ├── MainViewModel.cs       # Primary MVVM controller with filtering, selection, and undo
│   └── RelayCommand.cs        # Parameterized ICommand implementation
├── Views / Windows/
│   ├── MainWindow.xaml        # Main WPF UI layout with tabs, monitor gauge, and controls
│   └── ModernDialog.xaml      # Custom dark-themed modern dialog replacement for Win32 modals
├── App.xaml                   # Application entry point
├── WinDebloat.csproj          # .NET 10 WPF project definition
└── README.md
```

---

## ⚖️ Disclaimer

WinDebloat is provided as-is without warranty of any kind. Modifying operating system settings, terminating services, or removing preinstalled software carries inherent risk. Always ensure your important files are backed up and a System Restore Point is created prior to debloating.
