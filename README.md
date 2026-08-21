# SSH Agent GUI

A small Windows tray app for the OpenSSH Authentication Agent. List, create, load, and unload keys without using a terminal.

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

- Minimize hides the window to the system tray. Close (X) and tray **Exit** quit the app.
- If keys are loaded, you can **Exit only**, **Clear keys and exit**, or **Cancel**. With no keys loaded, exit is immediate.
- **Unload** removes a key from the agent and from the list.
- Uses Windows `ssh-add` / `ssh-keygen` only (not Git’s copies).
