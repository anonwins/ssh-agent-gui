# Repository hardening (manual settings)

This file lists GitHub settings the workflows cannot apply by themselves.

## Branch protection (`master`)

In Settings → Branches (or Rulesets):

- Require a pull request before merging
- Do not allow force pushes
- Do not allow deletions
- Require status checks to pass: **CI** and **CodeQL**
- Optionally require conversation resolution

## Private vulnerability reporting

Settings → Code security → Enable **Private vulnerability reporting** so [SECURITY.md](../SECURITY.md) can use GitHub Advisories.

## Code scanning

Enable Code scanning so CodeQL and Scorecard SARIF uploads appear. Private repositories need GitHub Advanced Security for that UI.

## Why Actions are pinned to commit SHAs

Third-party Actions are referenced as `owner/repo@<40-char SHA> # vX.Y.Z`, not `@v4` or `@main`. A tag can be moved; a commit cannot. Dependabot should open PRs that bump the SHA and the version comment together — keep the pin, do not switch back to a floating tag.

Scorecard must use the **peeled** commit of the release tag (`refs/tags/vX.Y.Z^{}`), not the annotated tag object.

## Workflow permissions

- CI: `contents: read` only. No secrets.
- CodeQL: `security-events: write` to upload results.
- Scorecard: job-level `security-events: write` and `id-token: write` so public repos can publish results. No workflow-level write permissions and no top-level `env:`.
- Release: `contents: write` on **tag pushes only**. Pull requests never see this workflow.

There is no `pull_request_target` workflow.

## Scorecard

A low score is expected until branch protection, a LICENSE (legal choice — not added here), and a signed release exist. Do not add CODEOWNERS or a badge just to raise the number. Link the workflow until a run exists.
