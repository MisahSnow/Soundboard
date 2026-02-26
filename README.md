# SoundboardNet

Desktop WinForms soundboard (`.NET 8`) with dual-output playback, goofy button tiles, and persistent board settings.

## Features

- Auto-load and reload sounds from a selected folder
- Dynamic tile grid with per-sound volume slider
- Route playback to up to 2 output devices at once
- Drag-and-drop audio import into the active folder
- Right-click tile actions:
  - Rename file
  - Set hotkey / clear hotkey
  - Change tile color / reset color
- Middle-click tile to stop currently playing instances of that sound
- Drag-and-drop tile reordering (swap by dropping on another tile)
- Persistent settings:
  - Last folder
  - Output 1 / Output 2 device selection
  - Per-sound volume
  - Per-sound hotkey
  - Per-sound tile color
  - Tile order

## Supported Audio Formats

- `.wav`
- `.mp3`
- `.flac`
- `.ogg`
- `.aiff`
- `.aif`

## Dependencies / Prerequisites

- Windows x64
- .NET 8 SDK (for building)
- NuGet access during restore (for `NAudio`)

## Quick Start

```powershell
dotnet restore
dotnet run
```

## Run

```powershell
dotnet restore
dotnet run
```

## Build

```powershell
dotnet restore
dotnet build -c Release
```

## Publish (Windows x64 EXE)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\Build
```

## Release Packaging

- Example release archive name: `Soundboard-1.0.0.zip`
- Recommended: zip the full publish output folder so required native files stay beside `SoundboardNet.exe`

## Notes

This project uses `NAudio` for playback and output device selection.
