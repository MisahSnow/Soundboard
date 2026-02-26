# SoundboardNet

WinForms `.NET 8` soundboard recreation with:

- Folder auto-load / reload
- Dynamic sound grid (buttons + per-sound volume slider)
- Two separate output dropdowns (route to up to 2 outputs)
- Drag-and-drop import into selected folder
- Right-click rename file
- Right-click tile color change/reset
- Saved folder, outputs, volumes, and tile colors

## Run

```powershell
dotnet restore
dotnet run
```

## Note

This project uses `NAudio` for playback and output device selection.
