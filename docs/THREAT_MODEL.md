# Threat model

SSH Agent GUI is a local Windows helper. It does not implement SSH cryptography. Windows OpenSSH creates keys, stores identities in `ssh-agent`, and performs signing.

## In scope

- Malicious local process running as the same Windows user
- Malicious local process running as another Windows user
- Malicious filenames and paths
- Filesystem races and reparse points / junctions
- Malformed Pageant or SSH-agent protocol messages
- Malicious SSH key metadata in `%AppData%\SshAgentGui\keys.json`
- Compromised or modified release artifacts (once tags exist)
- Compromised GitHub Actions workflow or dependencies
- Accidental private-key or passphrase persistence by this app

## Out of scope / assumptions

The app cannot realistically protect against:

- A fully compromised Windows account
- Kernel-level malware or a compromised SYSTEM / Administrator
- A compromised Windows installation or Microsoft OpenSSH binaries
- Physical memory attacks or hardware compromise
- Direct use of `openssh-ssh-agent` by `ssh.exe` or Git (they never hit this GUI)

## Assets

| Asset | Where it exists | Persisted? | Crosses a process boundary? |
| --- | --- | --- | --- |
| Private key files | User-chosen paths | Yes, by the user / OpenSSH | Path only, via `ssh-add` / `ssh-keygen` arguments |
| Private key material | Windows `ssh-agent` | Agent process | Yes, after load |
| Passphrase | .NET strings and a short-lived pipe | No | Yes: GUI → named pipe → askpass child → OpenSSH stdin-equivalent (stdout) |
| Public keys / fingerprints | UI, clipboard, temp `.pub` files | Metadata only | Yes |
| Agent / Pageant frames | Named pipes, `WM_COPYDATA` | No | Yes |
| Process IDs / labels | Pageant confirm UI | No | Advisory only |

## Trust boundaries

```text
GUI (asInvoker)
 |
 +--> Windows OpenSSH binaries (fixed install roots, no PATH)
 |
 +--> Windows ssh-agent (SYSTEM service)
 |
 +--> Pageant clients (same user, same integrity)
 |
 +--> CurrentUserOnly named pipes
 |
 +--> %AppData%\SshAgentGui (protected DACL)
```

## Mitigations

- OpenSSH is resolved under `%SystemRoot%\System32\OpenSSH` or `%ProgramFiles%\OpenSSH`. Reparse points on the root, the file, or parents below the root are skipped. Signatures are **not** checked.
- OpenSSH is started with `ArgumentList` and no shell. The passphrase is never an argument or environment value.
- The askpass pipe is `CurrentUserOnly`. After connect, the client PID must be the spawned `ssh-add`/`ssh-keygen` or a descendant. Same-user malware that injects into that tree can still obtain the secret.
- Leftover `SSH_AGENT_GUI_PASSPHRASE_PIPE` alone does not steal the GUI process. `--start-ssh-agent` is never treated as askpass.
- Pageant forwards only list (11) and confirmed sign (13). SSH-1 and other types fail locally. Frames are capped at 256 KiB.
- AppData protection fails closed at startup. `keys.json` / `ui.json` hold paths and UI state, not secrets, and are replaced atomically.

## Residual risks

- Same-user code that can inject into the OpenSSH child we spawned
- `--askpass` plus a crafted prompt can still show this app’s dialog
- Pageant HWND / mapping-name caller labels are spoofable
- Temp `%TEMP%\ssh-agent-gui-*.pub` files contain public key text only
- PID reuse is a theoretical race in ancestry checks
