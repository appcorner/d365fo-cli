# Roadmap

> **Audience:** contributors and users who want to know what's coming next.
> **Living document.** Only items that are **not yet implemented** live here. Everything already shipped is documented in [SETUP.md](SETUP.md) / [EXAMPLES.md](EXAMPLES.md) (user-visible surface) and [ARCHITECTURE.md](ARCHITECTURE.md) (internals). Git history preserves earlier design notes.

## Contents

1. [Refresh & observability](#1-refresh--observability)
2. [Runtime / live data](#2-runtime--live-data)
3. [More AOT types](#3-more-aot-types)
4. [Output & integration](#4-output--integration)
5. [Scaffolding extensions](#5-scaffolding-extensions)
6. [Code quality & Best Practices](#6-code-quality--best-practices)
7. [Tests](#7-tests)
8. [Small items / technical debt](#8-small-items--technical-debt)

---

## 1. Refresh & observability

### 1.1 `d365fo index diff <revision>`

Structural AOT diff vs. a git revision — e.g. "three new fields on `CustTable`, method `validate` signature changed". Requires a double extract or snapshotting. (The complementary fingerprint-based incremental refresh and the `ExtractionRuns` telemetry table shipped in schema v7; see EXAMPLES.md `index refresh` and `index history`.)

## 2. Runtime / live data

### 2.1 Live OData connector

`d365fo live entity <Name> --tenant … --env …` → calls `/data/$metadata` + `/data/<Collection>?$top=1`. Auth via `DefaultAzureCredential` or `D365FO_CLIENT_ID/SECRET`. Follow-on: `live call`, `live batch`.

### 2.2 Live metadata reconciliation

Compare offline `DataEntities` against live `$metadata` — surfaces entities inactive in an AOS or missing between Tier-1 / Tier-2.

### 2.3 Health / DMF (Windows VM)

`d365fo health entities`, `d365fo dmf push <Project>.zip`. Builds on existing `build` / `sync`.

## 3. More AOT types

Long-tail metadata not yet indexed:

- **3.1** AggregateDimension / Kpi / Perspective.
- **3.2** Tile / Workspace.
- **3.3** ReferenceGroup / Map / MapExtension.
- **3.4** ConfigurationKey / LicenseCode — cross-referenced to tables / fields / EDTs.
- **3.5** Feature (Feature Management).

## 4. Output & integration

### 4.2 Structured diff output

`--output patch` for `generate *` — apply as a text patch without touching the workspace.

### 4.3 Session cache

`.d365fo-session.json` next to the index; keeps the last active model / recent `get`s for prompt hints.

## 5. Scaffolding extensions

- Hand-rolled serialisers for selected Ax* kinds (e.g. `AxTableExtension`, `AxSecurityRole`) where `AxSerializer`'s generic walker elides detail the agent actually wants — the depth cap + cycle guard means we currently fall back to `Name` on deeply nested overlays.

## 6. Code quality & Best Practices

### 6.1 Additional lint categories

`d365fo lint` ships 6 categories and SARIF output (`--format sarif`). Still pending:

- "UI literal string without `@Label`" — requires parsing element captions on forms / menu items (deferred).

### 6.2 Richer coupling metrics

`d365fo models coupling` ships fan-in / fan-out / instability plus Tarjan SCC cycle detection over `ModelDependencies`. Remaining ideas:

- DYNAMICSXREFDB-backed object-level coupling (which class references which EDT / table) beyond the descriptor graph.
- HTML / DOT graph export (GraphViz `digraph`) so the output can feed CI dashboards.

## 7. Tests

- Performance smoke: `MeasureExtract(ApplicationSuite)` cap (runs only when `D365FO_PACKAGES_PATH` is set).
- Snapshot tests for `models deps` JSON output.
- Bridge: end-to-end harness that spins the net48 exe against a sample `PackagesLocalDirectory` fixture and asserts round-trip for read / create / update / delete.

## 8. Small items / technical debt

- Migrate remaining magic-string error codes to `D365FO.Core.D365FoErrorCodes` (canonical constants exist; call-sites are converting incrementally).
- Audit every `Render` call site for `StringSanitizer` coverage.

---

## See also

- [EXAMPLES.md](EXAMPLES.md) — what you can do today.
- [ARCHITECTURE.md](ARCHITECTURE.md) — where each item fits in the codebase.
