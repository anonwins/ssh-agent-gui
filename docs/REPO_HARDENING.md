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

This public repository already receives CodeQL and Scorecard SARIF on `master`. Confirm results at [Code scanning](https://github.com/anonwins/ssh-agent-gui/security/code-scanning). A private fork would need GitHub Advanced Security for that UI.

## Why Actions are pinned to commit SHAs

Third-party Actions are referenced as `owner/repo@<40-char SHA> # vX.Y.Z`, not `@v4` or `@main`. A tag can be moved; a commit cannot. Dependabot should open PRs that bump the SHA and the version comment together — keep the pin, do not switch back to a floating tag.

Scorecard must use the **peeled** commit of the release tag (`refs/tags/vX.Y.Z^{}`), not the annotated tag object.

## Workflow permissions

Write scopes belong on the **job**, not the workflow:

- CI: workflow `contents: read` only. No secrets.
- CodeQL: workflow `contents: read` + `actions: read`; job `security-events: write` to upload results.
- Scorecard: workflow `permissions: read-all`; job `security-events: write`, `id-token: write`, `contents: read`, and `actions: read`. No workflow-level write and no top-level `env:`.
- Release: workflow `contents: read`; job `contents: write` on **tag pushes only**. Pull requests never see this workflow.

There is no `pull_request_target` workflow.

## Scorecard

Token-Permissions is already a pass (job-level writes). A low overall score is still expected until a LICENSE exists (legal choice — not added here) and a signed release exists. Do not add CODEOWNERS or a badge just to raise the number.

Accepted leftovers (not defects in this app):

- **License** — add a LICENSE only after choosing one
- **Fuzzing** — out of scope
- **Code-Review** — solo maintainer; requiring PRs helps, a second human would be needed to pass honestly
- **CII-Best-Practices** — optional OpenSSF Best Practices badge
- **Maintained** — Scorecard treats repos younger than 90 days as unmaintained
- **Branch-Protection** (Scorecard alert) — the **Protect master** ruleset is the real control. On a public repo Scorecard often cannot *read* rulesets/classic protection with `GITHUB_TOKEN` (it still says protection is off). An optional `SCORECARD_TOKEN` PAT (`repo_token` on the Scorecard job) would let the check see them; do not add a PAT unless you want that alert gone.

Do not dismiss Scorecard alerts just to clear the Security tab.
