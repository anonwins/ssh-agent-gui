# Changelog

## Unreleased

## 0.1.0 — 2026-08-25

First public release. Framework-dependent `win-x64` zip; binaries are not Authenticode signed.

- Create, load, unload, and copy keys; passphrase prompts in the app; tray; auto-unload; Pageant Allow/Deny
- Askpass named pipe writes only to the spawned OpenSSH process tree
- Leftover passphrase-pipe environment no longer hijacks the GUI; `--start-ssh-agent` is never treated as askpass
- Pageant forwards list and confirmed sign only
- Metadata files are replaced atomically
- CI, CodeQL, Dependabot, Scorecard, and a tag-triggered release with SHA-256 checksums
