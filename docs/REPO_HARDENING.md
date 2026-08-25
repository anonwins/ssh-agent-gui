# Repository hardening (manual settings)

This file lists GitHub settings the workflows cannot apply by themselves.

## Branch protection (`master`)

Applied as ruleset **[Protect master](https://github.com/anonwins/ssh-agent-gui/rules/21353954)** (active, default branch):

- Require a pull request before merging
- Require one approving review (dismiss stale reviews)
- Do not allow force pushes
- Do not allow deletions
- Require status checks to pass and the branch to be up to date: **`build-test`** (CI) and **`analyze`** (CodeQL)
- Do **not** require **`analysis`** (Scorecard) or **Dependabot** as merge gates
- Repository admin is on the ruleset **bypass list** so a solo maintainer can still push `master`. Without that bypass, you cannot approve your own PRs and you cannot push `master`.

Do not add CODEOWNERS just to raise a Scorecard number.

## Private vulnerability reporting

Enabled. [SECURITY.md](../SECURITY.md) uses GitHub Advisories.

## Code scanning

[Code scanning](https://github.com/anonwins/ssh-agent-gui/security/code-scanning) is **CodeQL only**. Scorecard policy scores (License, Fuzzing, Code-Review, and so on) are not code defects and are not uploaded there. A private fork would need GitHub Advanced Security for that UI.

## Why Actions are pinned to commit SHAs

Third-party Actions are referenced as `owner/repo@<40-char SHA> # vX.Y.Z`, not `@v4` or `@main`. A tag can be moved; a commit cannot. Dependabot should open PRs that bump the SHA and the version comment together — keep the pin, do not switch back to a floating tag.

Scorecard must use the **peeled** commit of the release tag (`refs/tags/vX.Y.Z^{}`), not the annotated tag object.

## Workflow permissions

Write scopes belong on the **job**, not the workflow:

- CI: workflow `contents: read` only. No secrets.
- CodeQL: workflow `contents: read` + `actions: read`; job `security-events: write` to upload results.
- Scorecard: workflow `permissions: read-all`; job `security-events: write` (to clear leftover policy rows), `id-token: write`, `contents: read`, and `actions: read`. No SARIF upload, no workflow-level write, and no top-level `env:`.
- Release: workflow `contents: read`; job `contents: write` on **tag pushes only**. Pull requests never see this workflow.

There is no `pull_request_target` workflow.

## Scorecard

Scorecard still runs and publishes to [api.scorecard.dev](https://api.scorecard.dev/projects/github.com/anonwins/ssh-agent-gui). A low number is expected without a LICENSE, a second reviewer, fuzzing, or a signed release. That is not a Code Scanning finding and does not belong on the Security tab.

The **Protect master** ruleset is the real branch-protection control. Scorecard often cannot read rulesets on a public repo without a `SCORECARD_TOKEN` PAT; do not add a PAT just to raise the number. Do not add CODEOWNERS or a Best Practices badge just to raise the number.
