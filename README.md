# SSH Agent GUI

Windows GUI for the OpenSSH Authentication Agent. Create keys, load them, copy the public half, unload them.

It talks to the Windows OpenSSH tools under `%SystemRoot%\System32\OpenSSH\` (then `%ProgramFiles%\OpenSSH`) — not the ones Git puts on `PATH`. Those directories are an allowlist only. This app does not verify binary signatures or authenticity. An attacker who can replace `ssh-add.exe` there wins.

Minimize sends the window to the tray. Close and tray Exit quit. If anything is still loaded you’ll get a choice: leave the keys, unload them, or cancel. A passphrase key opens a dialog, not a console.

One instance. A second launch just brings the existing window forward.

## Auto-unload is a GUI-enforced policy, not a Windows ssh-agent security guarantee

The native Windows `ssh-agent` ignores `ssh-add -t`. While this app is running it may run `ssh-add -d` when the chosen Auto-unload time is due. That is session policy, not an agent-enforced lifetime.

It does **not** unload keys if the GUI is fully quit (the tray is enough to keep policy alive). Crashes, sleep, a killed process, a failed `ssh-add -d`, or another tool re-adding the same key can leave the agent holding the key.

## Passphrases

Passphrases are not written to disk. They exist only briefly in this process and are given to OpenSSH over an ephemeral named pipe (`CurrentUserOnly`). That pipe is intended to restrict access to the current Windows user. It is a same-user bearer capability (a random GUID name) and does **not** protect against a malicious process running as the same user.

## Requirements

- Windows 10/11, OpenSSH Client installed
- The `ssh-agent` service present (Manual is fine). If it is stopped, the app can start it; Windows may prompt for elevation. A Disabled service has to be enabled in Services first.
- .NET 9 to build

## Build

```bat
dotnet test SshAgentGui.sln
dotnet build SshAgentGui.sln -c Release
```

Then run `src\SshAgentGui\bin\Release\net9.0-windows\SshAgentGui.exe`. Use the built exe (not `dotnet run` as the askpass helper) so OpenSSH can invoke this program to unlock a key.

Settings live in `%AppData%\SshAgentGui\` (`keys.json` for key metadata, `ui.json` for window size/position and the last folders you picked). That directory is given a user-only DACL; it is not DPAPI. `keys.json` stores fingerprint, path, comment, type, bits, and optional `expiresAtUtc`. Paths are metadata, not credentials, but they may be privacy-sensitive.
