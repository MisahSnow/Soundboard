# SoundboardNet

WinForms `.NET 8` soundboard recreation with:

- Folder auto-load / reload
- Dynamic sound grid (buttons + per-sound volume slider)
- Two separate output dropdowns (route to up to 2 outputs)
- Drag-and-drop import into selected folder
- Right-click rename file
- Right-click tile color change/reset
- Saved folder, outputs, volumes, and tile colors

## Dependencies / Prerequisites

- Windows x64
- .NET 8 SDK (for building)
- NuGet access during restore (for `NAudio`)

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

## Note

This project uses `NAudio` for playback and output device selection.
