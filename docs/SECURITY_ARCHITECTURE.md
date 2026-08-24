# Security architecture

## Private keys

This GUI does not implement SSH cryptography. Private keys stay in files the user chooses. Loading and signing are delegated to Windows OpenSSH (`ssh-add`, `ssh-keygen`) and Windows `ssh-agent`. The app stores fingerprints, comments, types, paths, and optional expiry in `%AppData%\SshAgentGui\keys.json` — never key material.

If keygen reports success after a passphrase was requested, `EnsureCreatedKeyEncrypted` inspects the file and deletes an unencrypted leftover. OpenSSH can treat a failed askpass as an empty passphrase.

## Passphrases

Unlock uses a `PasswordBox`, then a short-lived `CurrentUserOnly` named pipe (`ssh-agent-gui-{guid}`). The askpass child reads the pipe and writes the secret to stdout for OpenSSH. The secret is not written to disk, logs, the clipboard, command lines, or environment values.

The pipe server writes only if `GetNamedPipeClientProcessId` shows the client is the spawned OpenSSH process or a descendant (typical tree: GUI → `ssh-add` → askpass instance of this exe). Lookup failure is fail-closed. A reject-only attacker can stall the 30s unlock timeout, not steal the secret.

Passphrases exist as .NET `string` values and UTF-8 pipe buffers. The runtime does not guarantee immediate erasure. We do not use `SecureString` or explicit zeroization. A same-user process with memory access during unlock can scrape the secret.

Reads from the askpass pipe are capped at 16 KiB. Oversized input is rejected.

## Executable resolution

`ssh-add.exe` and `ssh-keygen.exe` are taken from `%SystemRoot%\System32\OpenSSH` then `%ProgramFiles%\OpenSSH`. `PATH` is not searched. Names with `\` or `/` are rejected. The candidate must stay in that root. Reparse points on the root, the file, or directories below the root are skipped. This is not Authenticode verification.

The askpass helper must be a fully qualified `SshAgentGui.exe`, not `dotnet.exe`. Use the built executable, not `dotnet run`.

OpenSSH children have Git `SSH_AUTH_SOCK` / `SSH_AGENT_PID` stripped, plus leftover askpass/pipe/file env. `DISPLAY=1` is a Windows OpenSSH hang workaround, not a Unix display.

## Pageant

While the GUI runs it may host the classic `Pageant` window and a `CurrentUserOnly` pipe named `pageant.{user}.{sha256}`. Clients (PuTTY, WinSCP Pageant mode, Plink, TortoiseGit) can list identities without a prompt. **Sign** requests show Allow/Deny (Deny default, 60s auto-deny). All other SSH-2 types, including add/remove/lock/unlock/extension, fail locally and are not forwarded.

Caller text comes from a window handle, a PuTTY mapping name, or the pipe client PID. That is not cryptographic proof of the executable. Same-user malware can still talk to Pageant. OpenSSH `ssh` and Git do not use this bridge and will not prompt.

Do not run the GUI as Administrator: UIPI blocks unelevated clients from an elevated Pageant window. The manifest is `asInvoker`. The only elevation path is a short-lived `--start-ssh-agent` child with `runas`.

Pageant pipe **reads** time out after 30s. Writes after Allow/Deny are not put on that timer (the dialog can last 60s).

## Auto-unload

Windows `ssh-agent` does not honor `ssh-add -t`. This app unloads on a timer **while it is running**. If you quit, the app crashes, the machine sleeps through the timer, or another tool reloads a key, identities can remain in the agent. That is not an agent-level guarantee.

## Data at rest

`%AppData%\SshAgentGui` is created with a protected DACL: current user Modify, SYSTEM and Administrators FullControl, inheritance stripped, Everyone/Users/Authenticated Users Allow rejected. Startup aborts if protection fails.

`keys.json` and `ui.json` are written via a same-directory temp file and `File.Replace` (or `File.Move` on first create). Crash safety is for metadata integrity, not confidentiality.

Temp `%TEMP%\ssh-agent-gui-*.pub` files hold public key lines for fingerprint or unload helpers and are deleted in `finally`.
