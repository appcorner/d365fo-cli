# Setup

Everything you need to install, configure, and verify `d365fo-cli`. For command examples jump to [EXAMPLES.md](EXAMPLES.md).

---

## Prerequisites

**Any platform (Windows, macOS, Linux):**

- .NET SDK 10 — pinned by `global.json`.
- `git` — required for `review diff`.

**Windows D365FO developer VM (optional, for `build` / `sync` / `test` / `bp`):**

- Visual Studio 2026 (or 2022) with the **Dynamics 365 Finance and Operations** developer tools installed.
- A local `PackagesLocalDirectory` (typically `K:\AosService\PackagesLocalDirectory`).
- Microsoft tooling on `PATH`: `MSBuild.exe`, `SyncEngine.exe`, `SysTestRunner.exe`, `xppbp.exe`.

**Optional:** Python 3.8+ or PowerShell 7 — only needed if you want to regenerate Skills with `scripts/emit-skills.*`.

Off-Windows, the `build` / `sync` / `test` / `bp` commands return a structured `UNSUPPORTED_PLATFORM` error; everything else (indexing, searching, scaffolding, agent integration) works against any copy of `PackagesLocalDirectory`.

---

## Install

Pick one of two scenarios.

### Scenario A — Development (fastest for your own machine)

Use this when you are iterating on `d365fo-cli` itself, or you just want the quickest path from `git clone` to a working `d365fo` command. No publish step, no runtime bundling — every invocation goes through `dotnet run`, which rebuilds on source changes automatically.

```sh
git clone https://github.com/dynamics365ninja/d365fo-cli.git
cd d365fo-cli
dotnet build d365fo-cli.slnx -c Release
```

Expose the CLI as a shell alias that forwards to `dotnet run`:

```sh
# bash / zsh  — add to ~/.zshrc or ~/.bashrc
alias d365fo='dotnet run --project /absolute/path/to/d365fo-cli/src/D365FO.Cli --'
```

```powershell
# PowerShell  — add to $PROFILE
function d365fo { dotnet run --project C:\path\to\d365fo-cli\src\D365FO.Cli -- @args }
```

Pros: zero deployment, always runs current source. Cons: ~1 s .NET startup per call, requires the .NET SDK.

### Scenario B — Distribution (ship a standalone binary)

Use this when you want to share `d365fo` with teammates, drop it on a D365FO VM, bake it into a CI image, or run it on a machine that has no .NET SDK. `--self-contained` bundles the .NET 10 runtime next to the binary, so the target machine needs nothing installed.

```sh
# macOS / Linux
dotnet publish src/D365FO.Cli -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true

# Windows
dotnet publish src/D365FO.Cli -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishTrimmed=true
```

Supported RIDs: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`. Output lands in `src/D365FO.Cli/bin/Release/net10.0/<rid>/publish/`.

Rename `D365FO.Cli` (or `D365FO.Cli.exe`) to `d365fo` and drop it somewhere on `PATH`:

- **macOS / Linux:** `/usr/local/bin`, `~/.local/bin`, or `~/bin` (add to `PATH` in `~/.zshrc` if needed).
- **Windows:** `C:\Users\<you>\AppData\Local\Microsoft\WindowsApps`, or a custom folder added via *Settings → System → About → Advanced system settings → Environment Variables → Path*.

Verify with `which d365fo` (macOS/Linux) or `where.exe d365fo` (Windows), then `d365fo version`.

> **Framework-dependent alternative.** Drop `--self-contained` if the target machine already has .NET 10 installed — the publish output shrinks from ~70 MB to a few MB, at the cost of requiring a matching runtime.

---

## Configure

All configuration is environment-variable driven so it plays well with `.env` files, CI secrets, and VS Code launch profiles.

| Variable | Required | Purpose |
|---|---|---|
| `D365FO_PACKAGES_PATH` | for `index extract` | Root of the D365 F&O `PackagesLocalDirectory`. The extractor walks `<root>/<Package>/<Model>/AxTable/*.xml` etc. |
| `D365FO_INDEX_DB` | optional | Path to the SQLite index. Defaults to `$LocalAppData/d365fo-cli/d365fo-index.sqlite` (macOS/Linux: `~/.local/share/d365fo-cli/…`). |
| `D365FO_WORKSPACE_PATH` | optional | Root of your X++ workspace (used by `review diff` defaults). |
| `D365FO_CUSTOM_MODELS` | optional | CSV of model-name patterns to mark `IsCustom=true` in the index. Supports exact names, `*` / `?` wildcards, and `!` negation. Example: `ISV_*,!ISV_Sample`. |
| `D365FO_LABEL_LANGUAGES` | optional | CSV of language codes to keep during label extraction. Also used by `--resolve-labels` (first language wins). Default: `en-us`. |
| `D365FO_BRIDGE_ENABLED` | optional | `1` / `true` to route reads through the net48 Metadata Bridge (Windows + D365FO VM only). Silently falls back to the SQLite index otherwise. |
| `D365FO_BRIDGE_PATH` | optional | Path to `D365FO.Bridge.exe`. Defaults to `<cli-dir>/D365FO.Bridge.exe`. |
| `D365FO_BIN_PATH` | optional | `PackagesLocalDirectory\bin` — used by the bridge to resolve Microsoft metadata assemblies at runtime. |
| `D365FO_XREF_CONNECTIONSTRING` | optional | ADO.NET connection string for `DYNAMICSXREFDB`. Default: `Server=.;Database=DYNAMICSXREFDB;Integrated Security=true;Connection Timeout=5`. Used by `find refs --xref`. |

**Example — bash / zsh:**

```sh
export D365FO_PACKAGES_PATH=/mnt/d365fo/PackagesLocalDirectory
export D365FO_INDEX_DB=$HOME/.d365fo/index.sqlite
export D365FO_LABEL_LANGUAGES=en-us,cs
```

**Example — PowerShell:**

```powershell
$env:D365FO_PACKAGES_PATH = "K:\AosService\PackagesLocalDirectory"
$env:D365FO_INDEX_DB      = "$env:LOCALAPPDATA\d365fo-cli\index.sqlite"
$env:D365FO_LABEL_LANGUAGES = "en-us,cs"
```

Persist the exports in `~/.zshrc` / `~/.bashrc` / `$PROFILE` so every new shell picks them up automatically.

---

## First run

After Scenario A or B is in place and the environment variables are set:

```sh
# 1. Create / migrate the SQLite index
d365fo index build

# 2. Ingest metadata from PACKAGES_PATH (idempotent per model)
d365fo index extract
#   scoped:  d365fo index extract --model ApplicationSuite
#   explicit: d365fo index extract --packages /mnt/d365fo/PackagesLocalDirectory

# 3. Confirm
d365fo index status
```

`index extract` replaces rows per-model, so it is safe to re-run. A custom model takes seconds; a full ApplicationSuite extract takes minutes. XML parsing inside a model is parallelized across files; `*FormAdaptor` packages are skipped automatically.

---

## Quickstart script

Copy-paste the block below to go from a fresh clone to a populated index in one shot. It wires up env vars for the current shell and caches them in your rc file.

**bash / zsh:**

```sh
# Edit these two lines, then run the rest as-is.
REPO=$HOME/source/d365fo-cli
PKG=/mnt/d365fo/PackagesLocalDirectory

# 1. Build
cd "$REPO" && dotnet build d365fo-cli.slnx -c Release

# 2. Persist alias + env
{
  echo ""
  echo "# d365fo-cli"
  echo "alias d365fo='dotnet run --project $REPO/src/D365FO.Cli --'"
  echo "export D365FO_PACKAGES_PATH=\"$PKG\""
  echo "export D365FO_INDEX_DB=\"\$HOME/.d365fo/index.sqlite\""
} >> "$HOME/.zshrc"
source "$HOME/.zshrc"

# 3. Build the index
mkdir -p "$HOME/.d365fo"
d365fo index build
d365fo index extract
d365fo doctor
```

**PowerShell:**

```powershell
# Edit these two lines, then run the rest as-is.
$Repo = "C:\source\d365fo-cli"
$Pkg  = "K:\AosService\PackagesLocalDirectory"

# 1. Build
Push-Location $Repo
dotnet build d365fo-cli.slnx -c Release
Pop-Location

# 2. Persist function + env
Add-Content -Path $PROFILE -Value @"

# d365fo-cli
function d365fo { dotnet run --project $Repo\src\D365FO.Cli -- @args }
`$env:D365FO_PACKAGES_PATH = "$Pkg"
`$env:D365FO_INDEX_DB      = "`$env:LOCALAPPDATA\d365fo-cli\index.sqlite"
"@
. $PROFILE

# 3. Build the index
d365fo index build
d365fo index extract
d365fo doctor
```

> **Heads-up.** A first-class `d365fo init` command that performs all of this interactively is [tracked in the roadmap](ROADMAP.md). Until it lands, the script above is the canonical recipe.

---

## Visual Studio 2022 / 2026 integration

D365FO development happens in Visual Studio with the **Dynamics 365 Finance and Operations** developer tools. This section wires up `d365fo` as an External Tool and installs the GitHub Copilot Skills into your X++ project so Copilot has the X++ rule canon in scope at all times.

### 1. Prerequisites

- Visual Studio 2022 or 2026 with the **Dynamics 365 Finance and Operations** workload installed.
- [GitHub Copilot extension](https://marketplace.visualstudio.com/items?itemName=GitHub.copilotvs) installed in Visual Studio (supports VS 2022 17.10+ and VS 2026).
- `d365fo` reachable on `PATH` (Scenario A alias or Scenario B binary from the **Install** section above).

### 2. Register `d365fo` as an External Tool

External Tools let you run any CLI command from the **Tools** menu without leaving Visual Studio.

1. Open **Tools → External Tools…**
2. Click **Add** and fill in:

   | Field | Value |
   |---|---|
   | Title | `d365fo: index status` |
   | Command | `d365fo` |
   | Arguments | `index status --output json` |
   | Initial directory | `$(SolutionDir)` |
   | ☑ Use Output window | checked |

3. Repeat for any commands you want one-click access to (e.g. `index refresh --model $(ProjectName)`, `lint --output json`).
4. Click **OK**.

> **Tip.** Add a second entry with **Arguments** = `doctor --output json` to run a health check straight from the menu.

### 3. Copy Skills and Copilot instructions to your X++ project

GitHub Copilot in Visual Studio reads `.github/copilot-instructions.md` and `.github/instructions/*.instructions.md` from the root of your solution (repository). Deploying these files gives Copilot the full X++ rule canon — D365FO table/method names, CoC rules, BP rules, label rules — without burning context tokens.

**Automated script** — run once per X++ project (re-run after `d365fo-cli` updates to pick up new Skills):

```powershell
# Install-D365FoCopilotSkills.ps1
# Usage:
#   .\Install-D365FoCopilotSkills.ps1 -CliRepo C:\source\d365fo-cli -XppRepo K:\D365FO\MyProject
#
# Parameters:
#   -CliRepo   Path to your d365fo-cli clone (source of skills + copilot-instructions.md)
#   -XppRepo   Root of your X++ project / solution repository (target)

param(
    [Parameter(Mandatory)][string] $CliRepo,
    [Parameter(Mandatory)][string] $XppRepo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$src      = Join-Path $CliRepo 'skills\copilot'
$canon    = Join-Path $CliRepo '.github\copilot-instructions.md'
$dstRoot  = Join-Path $XppRepo '.github'
$dstInstr = Join-Path $dstRoot 'instructions'

# Create target directories if absent
New-Item -ItemType Directory -Force -Path $dstRoot  | Out-Null
New-Item -ItemType Directory -Force -Path $dstInstr | Out-Null

# Copy the main X++ rule canon
if (Test-Path $canon) {
    Copy-Item -Path $canon -Destination $dstRoot -Force
    Write-Host "[OK] copilot-instructions.md  →  $dstRoot"
} else {
    Write-Warning "copilot-instructions.md not found at: $canon"
}

# Copy all 15 Skills (.instructions.md)
$skills = Get-ChildItem -Path $src -Filter '*.instructions.md'
if ($skills.Count -eq 0) {
    Write-Warning "No *.instructions.md files found in: $src"
    Write-Warning "Run 'python scripts/emit-skills.py' in the d365fo-cli repo first."
} else {
    foreach ($f in $skills) {
        Copy-Item -Path $f.FullName -Destination $dstInstr -Force
        Write-Host "[OK] $($f.Name)  →  $dstInstr"
    }
}

Write-Host ""
Write-Host "Done. $($skills.Count) skill(s) + copilot-instructions.md deployed to $XppRepo"
Write-Host "Restart Visual Studio (or reload the solution) to apply."
```

**Example invocation:**

```powershell
.\Install-D365FoCopilotSkills.ps1 `
    -CliRepo  "C:\source\d365fo-cli" `
    -XppRepo  "K:\D365FO\MyProject"
```

After the script runs your X++ repository will have:

```
.github/
  copilot-instructions.md          ← full X++ / CoC / BP rule canon
  instructions/
    coc-extension-authoring.instructions.md
    data-entity-scaffolding.instructions.md
    event-handler-authoring.instructions.md
    form-pattern-scaffolding.instructions.md
    label-translation.instructions.md
    model-dependency-and-coupling.instructions.md
    object-extension-authoring.instructions.md
    review-and-checkpoint-workflow.instructions.md
    security-hierarchy-trace.instructions.md
    table-scaffolding.instructions.md
    x++-class-authoring.instructions.md
    xpp-best-practice-rules.instructions.md
    xpp-class-and-method-rules.instructions.md
    xpp-database-queries.instructions.md
    xpp-statement-and-type-rules.instructions.md
```

Commit these files so every developer on the project gets the same Copilot context automatically.

### 4. Keep Skills up to date

When you pull a new version of `d365fo-cli`, re-emit the Skills and re-run the install script:

```powershell
# In the d365fo-cli clone
cd C:\source\d365fo-cli
git pull
python scripts/emit-skills.py

# Re-deploy to your X++ project
.\Install-D365FoCopilotSkills.ps1 `
    -CliRepo "C:\source\d365fo-cli" `
    -XppRepo "K:\D365FO\MyProject"

# Then commit the updated files in your X++ project
cd K:\D365FO\MyProject
git add .github/
git commit -m "chore: update d365fo Copilot skills"
```

---

## Verify

```sh
d365fo version
d365fo doctor
```

`doctor` prints a checklist of the environment — SDK, env vars, index location, workspace. Fix any `ok=false` entries before continuing to [EXAMPLES.md](EXAMPLES.md).

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `PACKAGES_PATH_NOT_FOUND` | Set `D365FO_PACKAGES_PATH` or pass `--packages <PATH>`. |
| `UNSUPPORTED_PLATFORM` | `build` / `sync` / `test` / `bp` require Windows + a D365FO dev VM. Run them there. |
| Index file appears locked | Stop any running `d365fo daemon` or `d365fo-mcp` process. WAL sidecar files (`-wal`, `-shm`) are normal. |
| Extract missed a package | Confirm the `<root>/<Package>/<Model>/AxTable/…` layout and point `--packages` at the real `PackagesLocalDirectory`. |
| Label values contain junk | `search label` / `get label` strip control characters by default — pass `--raw-text` to see the unfiltered value. |
| Self-contained binary won't start on Linux | `chmod +x d365fo` after copying out of the publish folder. |

---

## Next steps

- [EXAMPLES.md](EXAMPLES.md) — one worked example per command.
- [ARCHITECTURE.md](ARCHITECTURE.md) — index schema, guardrails, bridge.
- [TOKEN_ECONOMICS.md](TOKEN_ECONOMICS.md) — why CLI + Skills beats MCP on token cost.
- [ROADMAP.md](ROADMAP.md) — planned and deferred items.
