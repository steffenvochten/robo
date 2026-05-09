# robo

A Spectre.Console terminal UI wrapper for Robocopy on Windows. Instead of typing out long `robocopy` commands by hand, `robo` walks you through the options interactively and runs the command for you.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4) ![Windows](https://img.shields.io/badge/platform-Windows-0078D4)

## Features

- Source and destination folder prompts with validation
- Auto-appends the source folder name to the destination path, with an editable pre-filled input so you can trim it before running
- Toggle move vs copy (`/MOVE`)
- Toggle recursive copy (`/E`)
- Multithreading with configurable thread count (`/MT:N`, default 128)
- Optional retry configuration (`/R:N /W:N`)
- Displays the full `robocopy` command before executing — confirm or skip
- Robocopy exit code displayed with a plain-English interpretation
- Loops back after each run so you can queue another operation without restarting

## Requirements

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (to build)

## Build & publish

```powershell
.\publish.ps1
```

This produces a single self-contained `robo.exe` and copies it to `C:\APrograms\robo.exe`. Add `C:\APrograms` to your PATH and run `robo` from anywhere.

## Usage

```
robo
```

The app is fully interactive — no command-line arguments needed.
