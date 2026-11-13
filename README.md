# WinDebloat

> **A modern, dynamic Windows debloater and memory optimization suite for Windows 10 and 11.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![Framework: .NET 10](https://img.shields.io/badge/Framework-.NET%2010.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Releases](https://img.shields.io/github/v/release/foofoodog/WinDebloat?color=success&label=Release)](https://github.com/foofoodog/WinDebloat/releases)

---

> [!WARNING]
> **USE AT YOUR OWN RISK — SYSTEM INTEGRITY WARNING**
> Modifying core Windows components, disabling system services, or removing preinstalled packages can cause system instability, break functionality, or brick your machine. Always create a **System Restore Point** using the built-in one-click tool before applying optimizations.

---

## Download & Running (No Installer Required)

**WinDebloat requires no installer.** It is built as a 100% standalone, portable single-file executable that can run directly from your Downloads folder, Desktop, or a USB drive without touching your filesystem or registry with an installer.

### Download Options from [GitHub Releases](https://github.com/foofoodog/WinDebloat/releases):
- **[WinDebloat.exe (Standalone)](https://github.com/foofoodog/WinDebloat/releases)**:
  - **Zero dependencies** — includes the entire self-contained runtime.
  - Double-click and run immediately on any Windows 10 or Windows 11 machine.
- **[WinDebloat-Portable.zip](https://github.com/foofoodog/WinDebloat/releases)**:
  - Lightweight framework-dependent archive for systems with .NET 10 installed.

---

## Key Features

### Real-Time Memory Monitor
- Live physical RAM consumption, free memory, and utilization percentage gauge.
- Instant feedback on memory reclaimed after applying debloat actions.

### Bloatware Catalog (OEM & Consumer Bloat)
- **OEM Preloads**: Automatically detects companion and telemetry software from **HP, Dell, Lenovo, and ASUS** (e.g. *HP Smart, HP Print Companion, Dell Command Update, Dell SupportAssist, Lenovo Vantage, MyASUS*).
- **Consumer & Ad Preloads**: Identifies preinstalled sponsored app stubs (*TikTok, Instagram, Disney+, Spotify, Clipchamp, casual card games*).
- **Telemetry & Assistant Apps**: Disables or removes *Connected User Experiences (`DiagTrack`), WAP Push Routing (`dmwappushservice`), Feedback Hub, Tips, Cortana, and Copilot*.
- **Entertainment Hubs**: Identifies unused MSN feeds, maps managers, and legacy media players.

### Dynamic Scan Component for User-Added Bloat
- Scans both 64-bit and 32-bit machine (`HKLM`) and user (`HKCU`) application databases for installed third-party desktop software.
- Filters out essential Microsoft Visual C++ runtimes and Windows security updates.
- Inspects publisher, version, install location, and provides direct uninstaller execution.

### Reversible Core System Tweaks
- **Start Menu Web Search**: Prevents `SearchHost.exe` from loading the Edge WebView2 browser engine and sending queries to Bing, speeding up local file/app search.
- **Windows Widgets & News Feed**: Hides taskbar widget buttons and terminates background feed caching (`MicrosoftStartFeedProvider`).
- **Browser Background Mode**: Enforces policies stopping Google Chrome and Microsoft Edge from pre-launching stealth background processes or lingering in memory after closure.

### Windows Services Optimization
- **Active Running Services Only**: Strictly filters candidate services to those currently running in RAM, eliminating clutter from idle or stopped services.
- **Safe Disabling**: Configures telemetry, Superfetch (`SysMain`), and unused background services to `Disabled` in the Windows Service Control Manager and registry.
- Stops the active instance immediately and moves the service to the Undo center.

### Persistent JSON Undo & Revert Engine
- **Persistent Disk Storage**: Every change, disabled service, and removed startup entry is serialized to a persistent JSON history database (`%LOCALAPPDATA%\WinDebloat\undo_history.json`).
- **Survives Reboots & App Restarts**: Historical modifications reload automatically into the **Undo / Revert** tab whenever you open the app.
- **Dedicated Tab**: Keeps main debloat candidate views clean and focused, preventing clutter.
- **Card-Level Revert**: Click `Revert Change` directly on any item in the Undo tab to restore it.
- **Batch Revert**: Select multiple items in the Undo tab and click `Revert Selected` to restore them in one go.
- **Clear History**: Includes a dedicated `Clear History` button to purge saved history records from disk when you're satisfied with your optimizations.

### Smart Elevation, Pre-Removal Snapshot & Modern UI
- **Pre-Removal Restore Point Prompt**: Before any changes are committed, WinDebloat prompts you with an optional System Restore Point snapshot modal.
- **Elevation When Needed**: Runs in standard mode without forcing UAC upfront; automatically prompts for elevation only when applying actions that require Administrator rights.
- **Modern Dark Dialogs**: Sleek, borderless dark-slate modal dialogs with soft drop shadows and contextual icons instead of dated Win32 message boxes.
- **Reboot Indicators**: Identifies tweaks requiring a system restart or Windows Explorer reload with a purple `REBOOT NEEDED` badge, and offers a one-click restart prompt upon completion.

---

## System Requirements

- **Operating System**: Windows 10 (1809+) or Windows 11 (64-bit)
- **Runtime**: None required for the standalone executable. [.NET 10.0 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) for portable/source builds.
- **Privileges**: Standard user for viewing and user tweaks; Administrator privileges for services, system policies, and restore point creation.

---

## Getting Started

### Running the Pre-Built Binary
1. Download **`WinDebloat.exe`** from [Releases](https://github.com/foofoodog/WinDebloat/releases).
2. Right-click and choose **Run as administrator** (or launch normally; the app prompts for elevation when applying admin-level actions).
3. *(Recommended)* Click **Create Restore Point** in the top warning banner.
4. Select the optimizations you want to apply, or click **Select Recommended**.
5. Click **Apply Selected Actions**.

---

## Building from Source

Ensure you have the .NET 10 SDK installed:

```powershell
# Clone the repository
git clone https://github.com/foofoodog/WinDebloat.git
cd WinDebloat

# Restore dependencies and build
dotnet build -c Release

# Publish standalone single-file executable (no runtime required)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./publish-standalone
```

---

## Project Architecture

```
WinDebloat/
├── .github/workflows/
│   └── release.yml            # Automated CI/CD release workflow for standalone binary
├── Models/
│   ├── DebloatItem.cs         # Core debloat item model with ApplyAction and UndoAction
│   └── UndoRecord.cs          # Persistent JSON history record model
├── Services/
│   ├── HistoryService.cs      # Persistent JSON undo database (%LOCALAPPDATA%\WinDebloat\undo_history.json)
│   ├── ScannerService.cs      # Scanners for startup, bloatware, tweaks, services, and user apps
│   └── SystemHelper.cs        # Registry, process execution, elevation, restore point, and memory helpers
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

## Disclaimer

WinDebloat is provided as-is without warranty of any kind. Modifying operating system settings, terminating services, or removing preinstalled software carries inherent risk. Always ensure your important files are backed up and a System Restore Point is created prior to debloating.
