# DJ Win Optimizer (Portable)

Portable Windows optimization tool for Streamers, Gamers and DJs. Built with C# WinForms (.NET 8).

## Run (Development)
- Install .NET 8 SDK.
- From `WinOptimizerApp/` run:

```
dotnet run -c Debug
```

## Publish (Portable)
Single-folder portable publish (no installer):

```
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

This produces a `publish/` folder containing all binaries and data files. Settings, profiles and logs are stored next to the EXE.

## Folders
- `Profiles/` saved profiles (JSON)
- `Logs/` logs per day
- `Core/`, `Services/`, `UI/`, `Utils/` code

## Notes
- Some system tweaks (Defender, Windows Update, Game DVR, OneDrive) often require admin privileges, policies or registry edits. The prototype logs placeholders and avoids destructive changes.
- Power plan switching uses `powercfg`.
- Auto-switch checks running processes every 3 seconds to select a matching profile by `Targets` list.
