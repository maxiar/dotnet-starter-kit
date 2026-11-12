# Keeping the fork in sync with upstream

This repository (`maxiar/dotnet-starter-kit`) is a fork of
[`fullstackhero/dotnet-starter-kit`](https://github.com/fullstackhero/dotnet-starter-kit). This guide
is the runbook for bringing upstream changes in without losing the fork's own features. Follow it
for every sync.

**Contents** — [Branches](#branches) · [Are we up to date?](#are-we-up-to-date) ·
[What the fork adds](#what-the-fork-adds) · [Sync runbook](#sync-runbook) ·
[Conflict playbook](#conflict-playbook) · [Follow-ups](#fork-follow-ups-after-the-merge) ·
[Verify](#verify) · [After the merge](#after-the-merge) · [Contributing back](#contributing-back-upstream) ·
[Pitfalls](#pitfalls) · [Sync log](#sync-log)

---

## Branches

| Branch | What it is | Who writes to it |
|---|---|---|
| `main` | **An exact mirror of `upstream/main`.** Never commit to it. Its only job is to show, at a glance, which upstream commit the fork has seen. | Fast-forward pushes from `upstream/main` only |
| `develop` | The Falconsoft working branch: upstream + the fork's features. Default branch; every project is scaffolded from it. | PRs only |
| `sync/upstream-YYYY-MM-DD` | One branch per sync: `develop` + the merge of `main` + the fork follow-ups. Becomes a PR into `develop`. | The person running the sync |
| `feat/...`, `fix/...` | Fork work, PR into `develop`. | Anyone |
| `*-upstream` | A change proposed to upstream, branched from `upstream/main`. Becomes a PR to `fullstackhero/dotnet-starter-kit`. | See [Contributing back](#contributing-back-upstream) |

Remotes:

```bash
git remote -v
# origin    git@maxiar:maxiar/dotnet-starter-kit.git              (the fork)
# upstream  https://github.com/fullstackhero/dotnet-starter-kit.git
git remote add upstream https://github.com/fullstackhero/dotnet-starter-kit.git   # if missing
```

## Are we up to date?

```bash
git fetch upstream origin
git rev-list --count origin/main..upstream/main     # commits upstream has that the mirror lacks
git rev-list --count origin/develop..origin/main    # mirrored commits not merged into develop yet
```

Both `0` means the fork is current. On GitHub, the `main` branch page shows the same thing, since
it must always read "This branch is up to date with fullstackhero/dotnet-starter-kit:main".

## What the fork adds

These are the features a sync must never lose. Most conflicts happen where one of them meets an
upstream change.

| Feature | Where it lives | Notes |
|---|---|---|
| **SQL Server provider** | `src/Host/FSH.Starter.Migrations.MSSQL`, provider switches in `BuildingBlocks/Persistence`, `IsSqlServer()` branches in module DbContexts | Every upstream PostgreSQL migration needs a SQL Server twin. Requires SQL Server 2025 / Azure SQL. |
| **`TestDatabase` seam** | `src/Tests/Integration*.Tests/Infrastructure/TestDatabase.cs` | Integration tests run on PostgreSQL or SQL Server (`FSH_TEST_DB_PROVIDER=mssql`). Upstream's factories hard-code a `PostgreSqlBuilder`; keep ours. |
| **`fsh` CLI extensions** | `src/Tools/CLI` (`framework pack/list/swap/clean-cache`, `self`, `upgrade`, `new --framework-packages/--agents/--template-path/--db-provider`) | Documented in `README-CLI.md`. |
| **Framework packages** | `src/Directory.Build.targets` reference rewrite, `FSH.Framework.*` pins in `Directory.Packages.props`, `[KernelSourceOnlyFact]` | Lets projects consume BuildingBlocks from a NuGet feed. |
| **Per-project Data Protection** | `DataProtection:ApplicationName` / `DataProtection:Store` (Redis or database key ring) | Upstream hard-codes `FSH.Starter`; closed upstream PR #1376. |
| **Hidable UI modules** | `disabledModules` in both React apps (`src/lib/modules.ts`, `env.ts`, `config.json`, nav, routes, cards), `FSH_DISABLED_MODULES` in Docker/Terraform | Upstream does not have it. Re-apply the `module`/`anyModule` tags on every nav item, route and card upstream rewrites. |
| **Redis key prefix** | `CachingOptions:KeyPrefix` | Proposed upstream as PR #1450; drop the fork copy once it lands. |
| **`db-migrate.ps1`** | `deploy/scripts/db-migrate.ps1` | Runs the DbMigrator against deployed environments. |
| **Docs** | `README-CLI.md`, `README-fork.md`, MSSQL notes in `AGENTS.md` and `.agents/` | |

## Sync runbook

Prerequisites: a clean working tree, Docker running (integration tests), and the `gh` CLI.

```bash
# 1. Refresh the mirror. Fast-forward only: if this is rejected, someone committed to main — investigate.
git fetch upstream origin
git push origin upstream/main:refs/heads/main

# 2. Branch the sync from develop and merge the mirror.
git switch -c sync/upstream-$(date +%F) origin/develop
git merge --no-ff --no-commit origin/main
git diff --name-only --diff-filter=U          # the conflict list
```

3. Resolve the conflicts with the [playbook](#conflict-playbook) and commit the merge. In the commit
   message, list every conflicted file and how it was resolved; the next sync will need that context.
4. Apply the [follow-ups](#fork-follow-ups-after-the-merge), **each as its own commit** after the
   merge, so reviewers can tell "upstream's change" from "our adaptation".
5. [Verify](#verify).
6. Open a PR `sync/upstream-YYYY-MM-DD` → `develop`. Include a table of the commits (marking which
   ones could go upstream) and the verification matrix, as PR #9 does.
7. Merge it, then do [After the merge](#after-the-merge).

## Conflict playbook

| File(s) | Rule |
|---|---|
| `src/Directory.Packages.props` | Take upstream's versions. Re-add the fork-only packages (`Aspire.Hosting.SqlServer`, `Hangfire.SqlServer`, `Microsoft.Data.SqlClient`, `OpenTelemetry.Instrumentation.SqlClient`, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, `Testcontainers.MsSql`) on the **same** version line as their siblings. Check transitive floors: `Aspire.Hosting.SqlServer` 13.6 requires `Microsoft.Data.SqlClient >= 7.0.2`, so the pin must be raised with it or restore fails with NU1605. |
| `Integration*.Tests/Infrastructure/*WebApplicationFactory.cs` | Keep `TestDatabase.Create(...)`. Port upstream's container changes (image tag, options) into `TestDatabase.cs`. |
| `clients/*/src/env.ts`, `public/config.json`, `docker/config.json.template`, `docker/docker-entrypoint.sh`, `deploy/docker/docker-compose.yml`, `deploy/terraform/**` | Union: keep upstream's new settings **and** `disabledModules` / `FSH_DISABLED_MODULES`. The entrypoint must still default and `export` `FSH_DISABLED_MODULES`; `envsubst` only substitutes exported variables. |
| `clients/**` pages, nav, settings tabs, cards | Take upstream's markup (for example its i18n `t()` / `labelKey` text) and re-apply the fork's `module` / `anyModule` tags and `isModuleEnabled(...)` gates. Watch for merged objects that end up with the same key twice (TS1117), such as two `enabled:` on one `useQuery`; combine them with `&&`. |
| `AGENTS.md`, `.agents/**`, `deploy/docker/README.md` | Upstream's text, plus the fork's MSSQL and hidable-module notes. |

After resolving: `git grep -n '^<<<<<<<\|^>>>>>>>'` must return nothing.

## Fork follow-ups after the merge

Go through this checklist every time. Each item has caught something in a past sync.

- [ ] **SQL Server twins for new PostgreSQL migrations.** List them with
  `git diff --name-only --diff-filter=A <merge-base> origin/main -- src/Host/FSH.Starter.Migrations.PostgreSQL`.
  Scaffold each one into `FSH.Starter.Migrations.MSSQL` with the `create-migration` skill (step 2b). No
  live SQL Server is needed to scaffold. Then hand-port any raw `migrationBuilder.Sql(...)` to T-SQL.
  - **Index on Identity claim columns:** `ClaimType` / `ClaimValue` are `nvarchar(max)` on SQL Server, which cannot be index keys. Bound them in an `IsSqlServer()` branch so the whole key stays ≤ 1700 bytes.
  - **Filtered indexes** cannot use `LIKE` on SQL Server (see `FilesDbContext`).
- [ ] **Provider-specific exception handling.** Upstream matches `PostgresException`, for example on
  unique violations. Add the SQL Server equivalent (`SqlException` 2601/2627) wherever the fork must
  behave the same. `git grep -n PostgresException origin/main -- src/Modules` finds them.
- [ ] **Fork-only code against upstream's library upgrades.** Upstream adapts its own code; the fork's
  CLI commands, test attributes and helpers are ours to fix. Examples: Spectre.Console.Cli made
  `ExecuteAsync` public (CS0507); xUnit v3 requires caller-info constructors on custom `FactAttribute`s
  (xUnit3003) and dropped `Xunit.Abstractions`.
- [ ] **New security advisories.** Advisories published after upstream's last bump fail restore under
  `TreatWarningsAsErrors` (NU1902/NU1903). Bump the whole patch line (every `10.0.x` pin, Aspire
  packages **and** the `Aspire.AppHost.Sdk` version in the AppHost project) and note the advisory IDs
  in the commit. If the advisory also covers the ASP.NET Core runtime, the servers need the matching
  Hosting Bundle.
- [ ] **Hidable modules on new UI.** A new page, nav item or card from upstream that belongs to a
  hidable module needs its `module` tag.
- [ ] **Docs.** `README-CLI.md` (new CLI behaviour) and this file's [sync log](#sync-log).

## Verify

```bash
dotnet build src/FSH.Starter.slnx -warnaserror
dotnet test  src/FSH.Starter.slnx                                   # unit + architecture + integration on PostgreSQL
FSH_TEST_DB_PROVIDER=mssql dotnet test src/Tests/Integration.Tests          # integration on SQL Server 2025
FSH_TEST_DB_PROVIDER=mssql dotnet test src/Tests/Integration.Middleware.Tests
cd clients/admin     && npm install && npx tsc -b && npm run lint && CI=1 npx playwright test
cd clients/dashboard && npm install && npx tsc -b && npm run lint && CI=1 npx playwright test
```

`MigrationDriftTests` (in Architecture.Tests) fails if either provider is missing a migration.

## After the merge

```bash
fsh self install                                  # only if src/Tools/CLI changed
fsh framework pack --push --clear-cache --feed ~/dev/nuget-local
```

Then propagate to every project listed in
[README-CLI.md → Projects built from this template](README-CLI.md#projects-built-from-this-template):
`fsh upgrade` for template changes and a `FshFrameworkVersion` bump for the kernel, following the
[runbook](README-CLI.md#runbook-upgrading-a-project-end-to-end). Update the Framework column of that table.

## Contributing back upstream

A fix that is not Falconsoft-specific belongs upstream too. That way the fork carries less of its own
code, and future syncs get easier.

```bash
git switch -c fix/<topic>-upstream upstream/main     # branch from upstream, not develop
# ...commit, push to origin...
GH_TOKEN=$(gh auth token -u maxiar) gh pr create -R fullstackhero/dotnet-starter-kit \
  --base main --head maxiar:fix/<topic>-upstream --title "..." --body-file pr.md
```

- PRs to upstream are opened from the **`maxiar`** GitHub account. `gh`'s active account on this
  machine may be a different one; `GH_TOKEN=$(gh auth token -u maxiar)` uses the right account for one
  command without switching.
- Bring the same change into `develop` through its own PR. When upstream merges it, the next sync
  resolves it as "already applied".
- Upstream's `main` may itself fail restore on a fresh advisory. Say so in the PR and build with
  `-p:NuGetAudit=false` to verify only the change.

## Pitfalls

| Symptom | Cause | Fix |
|---|---|---|
| `dotnet ef migrations remove --no-build` deleted an older, committed migration | `--no-build` reads the stale migrations assembly, whose "last migration" is the previous one | Never combine `remove` with `--no-build`; restore with `git checkout -- <migrations folder>` |
| A scaffolded migration ignores a model change you just made | `--no-build` used the startup project's stale copy of the module DLL | Let `dotnet ef` build, or build the startup project first |
| `error : La información de control del código fuente no está disponible` / "source link is empty" on the CLI project | The remotes use SSH host aliases (`git@maxiar:`), which SourceLink cannot map to GitHub | Environmental and harmless; it affects only that project's SourceLink |
| Hundreds of integration tests fail in under a second | Docker is not running; Testcontainers cannot start the database | Start Docker Desktop and re-run |
| A Playwright login spec fails for no reason | Another project's dev server holds port 5173/5174 and Playwright reuses it | Stop it, or run with `CI=1` on a free port |
| `gh pr create` → "must be a collaborator" | Wrong `gh` account active | See [Contributing back](#contributing-back-upstream) |

## Sync log

Append a row per sync.

| Date | Upstream commit | Sync PR | Notes |
|---|---|---|---|
| 2026-10-15 | `a9675375` | #9 | 34 commits, 20 conflicts. MSSQL migrations for `ProductImageFileAssetIdIndex`, `FileAssetLegacyKeyIndex`, `AddUserLocale`. |
| 2026-11-12 | `8f211c24` | (this sync) | 24 commits, 23 conflicts (React i18n vs `disabledModules`). .NET 10.0.15 / Aspire 13.6.5 for CVE-2026-114147 and CVE-2026-120006. MSSQL `AddRoleClaimsUniqueIndex`. Fork code adapted to xUnit v3 and Spectre.Console. `main` mirror branch introduced. |
