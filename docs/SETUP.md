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
