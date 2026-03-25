# Mutiblox

A Windows desktop application for managing multiple Roblox accounts and running multiple game instances simultaneously.

> **Status:** Work in progress — core features functional, some areas still under development.

## Features

- **Multi-Account Management** — Store and switch between multiple Roblox accounts with encrypted credential storage (DPAPI)
- **Multi-Instance Support** — Run multiple Roblox game clients at the same time by handling Roblox's singleton mutex
- **Game Browser** — Browse and launch games directly from the app
- **Instance Dashboard** — Monitor running instances with performance stats
- **Graphics Presets** — Apply custom ClientSettings for performance optimization
- **Anti-AFK** — Keep instances active automatically
- **Cookie Keep-Alive** — Automatic session refresh for stored accounts
- **Watcher Service** — Background process that monitors Roblox instances

## Tech Stack

- **Language:** C# / .NET 8.0
- **UI Framework:** WPF (Windows Presentation Foundation)
- **Architecture:** MVVM (CommunityToolkit.Mvvm)
- **DI:** Microsoft.Extensions.DependencyInjection
- **Browser:** WebView2 (for game browsing)
- **Encryption:** DPAPI (Windows Data Protection API)

## Building

### Prerequisites
- .NET 8.0 SDK
- Windows 10/11

### Build & Run
```bash
dotnet restore
dotnet build
dotnet run --project src/Mutiblox
```

### Publish
```bash
dotnet publish src/Mutiblox -c Release -r win-x64 --self-contained false -o publish
dotnet publish src/Mutiblox.Watcher -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Project Structure

```
Mutiblox/
├── src/
│   ├── Mutiblox/              # Main WPF application
│   │   ├── Core/              # Constants, native methods
│   │   ├── Models/            # Data models
│   │   ├── Services/          # Business logic (interfaces + implementations)
│   │   ├── ViewModels/        # MVVM view models
│   │   └── Views/             # XAML pages and windows
│   └── Mutiblox.Watcher/     # Background watcher service
└── tests/
    └── Mutiblox.Tests/        # Unit tests
```

## Disclaimer

This project is not affiliated with or endorsed by Roblox Corporation. Use at your own risk and in accordance with [Roblox's Terms of Use](https://en.help.roblox.com/hc/en-us/articles/115004647846-Roblox-Terms-of-Use).

## License

MIT
