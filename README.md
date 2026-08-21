# SSH Agent GUI

Windows GUI for the OpenSSH Authentication Agent. Create keys, load them, copy the public half, unload them. It talks to the Windows OpenSSH tools under `C:\Windows\System32\OpenSSH\` — not the ones Git puts on your PATH.

Minimize sends the window to the tray. Close and tray Exit quit. If anything is still loaded you’ll get a choice: leave the keys, unload them, or cancel. A passphrase key opens a dialog, not a console.

One instance. A second launch just brings the existing window forward.

## Requirements

- Windows 10/11, OpenSSH Client installed
- The `ssh-agent` service running
- .NET 9 to build

## Build

```bat
dotnet build SshAgentGui.sln -c Release
```

Then run `src\SshAgentGui\bin\Release\net9.0-windows\SshAgentGui.exe`.

Settings live in `%AppData%\SshAgentGui\` (`keys.json` for key paths, `ui.json` for window size/position and the last folders you picked).
