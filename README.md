# SSH Agent

A small Windows tray app for the OpenSSH Authentication Agent. List, create, load, enable/disable, and unload keys without using a terminal.

## Requirements

- Windows 10/11 with the OpenSSH client (`C:\Windows\System32\OpenSSH\`)
- The **OpenSSH Authentication Agent** (`ssh-agent`) Windows service
- .NET 9 (to build)

## Build

```bat
dotnet build SshAgentGui.sln -c Release
```

Run `src\SshAgentGui\bin\Release\net9.0-windows\SshAgentGui.exe`.

## Behavior

- Minimize or close (X) hides the window to the system tray. Exit from the window menu or the tray icon.
- On exit you can clear keys from the agent, leave them loaded, or cancel.
- **Disable** unloads a key but keeps the row so you can **Enable** it later. **Unload** removes it from the agent and from the list.
- Uses Windows `ssh-add` / `ssh-keygen` only (not Git’s copies).
