# Orbital

A macOS Dynamic Island–style floating desktop widget for Windows, built with WPF and .NET 10.

![Windows](https://img.shields.io/badge/platform-Windows-blue)
![.NET 10](https://img.shields.io/badge/.NET-10-purple)

## Features

- **Dynamic Island UI** — Expands/collapses with smooth animations, always on top
- **Clock & Date** — Always visible in the collapsed bar
- **Timer & Stopwatch** — Quick-access countdown timer and stopwatch
- **Audio Spectrum Visualizer** — Real-time waveform from system audio
- **System Controls** — Volume, brightness, lock, hibernate, shutdown, restart
- **System Info** — Live battery, CPU, and RAM usage
- **Workspaces** — Named workspaces that launch apps and websites together
- **Temporary Shelf** — Drag-and-drop files for quick access (cleared on restart)
- **Permanent Shortcuts** — Persistent file/folder/app shortcuts
- **Reminders** — Timed reminders with inline toast notifications
- **Day-Based Schedule** — Multi-week rotating schedule with countdown
- **Light & Dark Mode** — Full theme support
- **Auto-Hide** — Slides to screen edge when idle
- **Startup Launch** — Optional Windows startup registration

## Screenshots

<!-- Add screenshots of your app here, for example: -->
<!-- ![Collapsed](screenshots/collapsed.png) -->
<!-- ![Expanded](screenshots/expanded.png) -->

## Requirements

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or use the self-contained release)

## Installation

### Download Release
1. Go to [Releases](https://github.com/Flzsh/Orbital/releases)
2. Download the latest `.zip`
3. Extract and run the `.exe`

### Build from Source
```bash
git clone https://github.com/Flzsh/Orbital.git
cd Orbital
dotnet run --project "Study Island/Study Island.csproj"
```

## Configuration

Settings are stored in `%LocalAppData%\Orbital\settings.json` and include theme, schedule, workspaces, shortcuts, auto-hide timing, and startup preference.

## Tech Stack

- **WPF** (.NET 10, C# 14)
- **NAudio** — System audio capture for spectrum visualizer
- **WebView2** — Embedded browser in workspaces
- **P/Invoke** — Battery, CPU, RAM, brightness, volume control

## License

MIT