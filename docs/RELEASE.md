# Releases

There is no published GitHub Release until a maintainer pushes a version tag.

## Cut a release

1. Update [CHANGELOG.md](../CHANGELOG.md) and the `<Version>` in `src/SshAgentGui/SshAgentGui.csproj` if needed.
2. Commit on `master`.
3. Tag and push (example):

```bat
git tag v0.1.0
git push origin v0.1.0
```

The [release workflow](../.github/workflows/release.yml) runs only on tags matching `v*.*.*`. It tests, publishes a framework-dependent `win-x64` build, zips it, writes `SHA256SUMS.txt`, and creates a GitHub Release. The tag is the version SSOT (`/p:Version=` from the tag).

Users need the [.NET 9 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).

## Verify a release

1. Confirm the release page lists the source commit.
2. Download the zip and `SHA256SUMS.txt`.
3. Check the hash:

```powershell
Get-FileHash .\SshAgentGui-0.1.0-win-x64.zip -Algorithm SHA256
```

or:

```bat
certutil -hashfile SshAgentGui-0.1.0-win-x64.zip SHA256
```

The lowercase hex should match the line in `SHA256SUMS.txt` (GNU two-space form).

Builds are **not** claimed to be bit-for-bit reproducible. Verification is: tag → commit → public workflow → SHA-256.

## Authenticode

Release binaries are **not** Authenticode signed. There is no certificate in CI.

To add signing later:

- Use a release-only secret or GitHub OIDC to Azure Trusted Signing / `signtool`.
- Never grant that credential to `pull_request` workflows.
- Timestamp signatures.
- Document the certificate owner in this file when it exists.
