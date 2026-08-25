# Security Policy

## Supported versions

Security fixes are developed on the default branch (`master`). There are **no published GitHub Releases yet**; build from source at the commit you trust.

After the first tagged release (`v*.*.*`), supported versions will be **the latest tag** and, at maintainer discretion, the previous tag for a limited time. Older commits and fork builds are unsupported.

## Reporting a vulnerability

Report security issues through [GitHub Security Advisories](https://github.com/anonwins/ssh-agent-gui/security/advisories/new) (private vulnerability reporting).

There is no security contact email in this repository. Private vulnerability reporting is enabled — do not file exploit details in a public issue.

Please allow **7 days** for an initial response and **30 days** for a fix or a documented decision on `master`. Do not expect coordinated disclosure dates until tagged releases exist.

## In scope

- Passphrase handling (askpass named pipe and GUI prompt)
- Pageant compatibility bridge (list + confirmed sign)
- `%AppData%\SshAgentGui` directory protection
- Windows OpenSSH executable resolution
- GUI auto-unload timer
- Isolated `--start-ssh-agent` elevation helper

## Out of scope

- Bugs in Microsoft OpenSSH, PuTTY, WinSCP, or Git for Windows
- Keys that remain loaded after you quit, crash, or sleep
- Same-user malware with code execution
- A fully compromised Windows account, kernel, or SYSTEM
- Unsigned operating-system binaries

## Known limitations

- Pageant caller labels are advisory, not identity proof.
- This app does not verify Authenticode signatures of OpenSSH or of itself.
- Passphrases are .NET `string` values and are not securely erased from memory.
- Auto-unload is best-effort while the GUI is running.
- OpenSSH `ssh` and Git talk to Windows `ssh-agent` directly and skip Allow/Deny.

See [docs/THREAT_MODEL.md](docs/THREAT_MODEL.md) and [docs/SECURITY_ARCHITECTURE.md](docs/SECURITY_ARCHITECTURE.md).
