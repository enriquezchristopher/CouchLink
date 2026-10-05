# Contributing to CouchLink

Thanks for helping! This guide covers how to build, test, and send changes.

## Build and test

Requires Windows 10/11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build      # warnings are treated as errors
dotnet test
```

To try virtual controllers locally, install the
[ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases) and run
`dotnet run --project src/CouchLink.PadTest -- check 9`.

## How we work

- **Design first.** Larger features start as a spec in
  `docs/superpowers/specs/` and a step-by-step plan in `docs/superpowers/plans/`.
- **Test first.** Write a failing test, watch it fail, then make it pass. Logic
  belongs in `CouchLink.Core` (pure and unit-tested); Windows/driver glue stays
  thin in `CouchLink.App` / `CouchLink.Pads`.
- **Small, focused files**, each with one clear job.

## Commit messages

We use [Conventional Commits](https://www.conventionalcommits.org/). The type
decides the next version number and the changelog:

| Type | Use for | Version effect (before 1.0) |
|---|---|---|
| `feat:` | new user-facing feature | minor (0.1.0 -> 0.2.0) |
| `fix:` | bug fix | patch (0.1.0 -> 0.1.1) |
| `perf:` | performance improvement | patch |
| `docs:`, `test:`, `build:`, `ci:`, `chore:`, `refactor:` | everything else | none |

Add a scope when it helps, e.g. `feat(core): ...`, `fix(app): ...`.

## Pull requests

- Every PR must pass the **CI** check (build + tests + trial package).
- We use **stacked PRs** for multi-step work: each PR targets the branch of
  the PR below it, so each one shows only its own changes. When the lower PR
  merges, GitHub retargets the next one to `main`.
- Describe what changed and how you tested it (the PR template asks for this).

## Releases

Releases are automated with
[release-please](https://github.com/googleapis/release-please). After changes
land on `main`, a bot keeps a "release" PR open with the changelog. Merging it
tags the version, publishes a GitHub Release, and attaches
`CouchLink-vX.Y.Z-win-x64.zip`.
