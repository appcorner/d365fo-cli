# d365fo — AI-native CLI for D365 Finance & Operations X++

> **Index your AOT. Search it in milliseconds. Scaffold objects that compile. Ground every AI answer in real metadata.**

[![MIT License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey.svg)]()
[![Successor to d365fo-mcp-server](https://img.shields.io/badge/successor%20to-d365fo--mcp--server-orange.svg)](https://github.com/dynamics365ninja/d365fo-mcp-server)

Built for D365FO developers and AI agents that need **real AOT metadata** — not training-data guesses.

---

## Why this exists

Every AI assistant hallucinates D365 field names, method signatures, and label IDs. The root cause is simple: your environment has hundreds of thousands of custom objects that no model has ever seen. The fix is equally simple: **give the agent a local index it can query before generating any code**.

| Without `d365fo` | With `d365fo` |
|---|---|
| Agent invents `CustTable.CustomerName` (doesn't exist) | `d365fo get table CustTable` returns the real field list in <100 ms |
| CoC wrapper copies default-param values → compile error | `d365fo find coc Class::method` returns exact signature — no guessing |
| Label IDs typed by hand → `BPErrorUnknownLabel` BP failures | `d365fo search label "customer account"` finds the right `@SYS…` token |
| 54 MCP tool definitions burn ~2,900 tokens every turn | 1 shell command + lazy-loaded Skills ≈ 100 tokens per turn |
| Scaffolded XML missing ActionPane / QuickFilter / PatternVersion | `d365fo generate form` uses validated pattern templates |
| `today()` in generated X++ → `BPUpgradeCodeToday` failure | Agent receives the X++ rule canon from `.github/copilot-instructions.md` |

---

## Solution Architecture

![Solution Architecture](docs/img/solution-architecture-diagram.png)

The CLI sits between your AI agent and the D365FO metadata layer. It exposes a single `d365fo` binary that queries a local SQLite index (or a live metadata bridge on Windows VMs) and returns stable JSON envelopes your agent can parse in one tool call.

---

## Key Capabilities

- **Instant AOT lookup** — tables, classes, EDTs, enums, forms, queries, views, reports, services, workflows, security roles, and labels; results in milliseconds without touching a D365 VM
- **Scaffold X++ objects** — pattern-validated XML for tables, classes, CoC extensions, and AxForms (all 9 D365FO patterns: `SimpleList`, `SimpleListDetails`, `DetailsMaster`, `DetailsTransaction`, `Dialog`, `TableOfContents`, `Lookup`, `ListPage`, `Workspace`)
- **Understand the code** — trace CoC targets, inbound/outbound relations, event handlers, label translations, and cross-references across your workspace
- **AI-ready JSON** — every command returns a stable `{ ok, data, warnings }` envelope; agents parse once, never re-adapt
- **15 lazy-loaded Skills** — full X++/CoC/BP rule canon (database queries, class rules, statement types, best practices) loaded only when relevant; ~90% fewer tokens per workflow than MCP
- **Daemon mode** — warm-cache named-pipe server with file-system watcher; auto-refreshes index on AOT XML changes (debounce configurable)
- **CI / pipeline ready** — runs in PowerShell, bash/zsh, GitHub Actions, Azure DevOps; no GUI required
- **Optional VM integration** — drives `MSBuild`, `SyncEngine`, `SysTestRunner`, and `xppbp` on Windows D365FO dev VMs
- **MCP adapter included** — `d365fo-mcp` speaks JSON-RPC 2.0 and shares the same index; wire into Claude Desktop, Cursor, Continue, or VS Code MCP

---

## Quick Start

### Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- Access to a D365 F&O `PackagesLocalDirectory` (local clone, Azure Files share, or Windows VM path)

### Install

```sh
git clone https://github.com/dynamics365ninja/d365fo-cli.git
cd d365fo-cli
dotnet build d365fo-cli.slnx -c Release
```

**PowerShell alias (fastest for dev):**

```powershell
function d365fo { dotnet run --project C:\path\to\d365fo-cli\src\D365FO.Cli -- @args }
```

**Self-contained binary (for distribution):**

```sh
dotnet publish src/D365FO.Cli -c Release -r win-x64 --self-contained
# also: linux-x64, osx-arm64
```

### First run

```sh
# Point at your packages folder
$env:D365FO_PACKAGES_PATH = "K:\AosService\PackagesLocalDirectory"

# Build + populate the index
d365fo index build
d365fo index extract

# Verify
d365fo doctor --output json
d365fo index status --output json

# Search
d365fo search table Cust --output json
d365fo get table CustTable --output json
d365fo find coc SalesTable::insert --output json
d365fo resolve label @SYS12345 --lang en-us,cs
```

### Scaffold your first object

```sh
# New table
d365fo generate table FmVehicle \
  --label "@Fleet:Vehicle" \
  --field VIN:VinEdt:mandatory \
  --field Make:Name \
  --field Year:YearEdt \
  --out src/MyModel/AxTable/FmVehicle.xml

# Chain-of-Command extension
d365fo generate coc SalesTable --method insert --out src/MyModel/AxClass/SalesTable_MyExt.xml

# Form (SimpleList pattern)
d365fo generate form FmVehicles \
  --pattern SimpleList \
  --table FmVehicle \
  --field VIN --field Make --field Year \
  --out src/MyModel/AxForm/FmVehicles.xml
```

---

## AI Agent Integration

### GitHub Copilot (VS Code / Visual Studio)

Copy `.github/copilot-instructions.md` into your consuming repo's `.github/` folder. It contains the full X++ rule canon with MS Learn citations.

Optionally copy the Skills:

```sh
# Emit GitHub Copilot instructions
python3 scripts/emit-skills.py

# Then copy to your repo
cp skills/copilot/*.instructions.md /your-repo/.github/instructions/
```

### Claude Code / Claude Desktop

```sh
# Emit Anthropic SKILL.md files
python3 scripts/emit-skills.py

# Reference in your project or ~/.claude/skills/
cp -r skills/anthropic/ /your-repo/.claude/skills/
```

### Codex CLI / Gemini CLI / Cursor

Reference the `SKILL.md` files from `skills/anthropic/` in your session prompt or `AGENTS.md`.

### MCP (Claude Desktop, Continue, VS Code MCP)

```json
{
  "mcpServers": {
    "d365fo": {
      "command": "d365fo-mcp",
      "args": [],
      "env": {
        "D365FO_PACKAGES_PATH": "K:\\AosService\\PackagesLocalDirectory"
      }
    }
  }
}
```

---

## Why CLI instead of MCP?

MCP servers inject every tool definition into the model's context on every single turn. For this project that used to be **54 tools ≈ 2,900 tokens every turn**.

| | MCP server | CLI + Skills |
|---|---|---|
| Tool definitions per turn | 54 tools (~2,900 tokens) | 1 shell tool (~100 tokens) |
| Discovery round-trips | 2–3 per task | often 1 (`d365fo get table X`) |
| Scriptable (shell, CI/CD) | No | Yes |
| Works in any AI harness | No — MCP hosts only | Yes — Copilot, Claude, Codex, Gemini, … |
| Token cost over 15-turn workflow | baseline | **~90% reduction** |

See [`docs/TOKEN_ECONOMICS.md`](docs/TOKEN_ECONOMICS.md) for the full analysis and the cases where MCP still wins.

---

## Commands at a Glance

| Group | Commands |
|---|---|
| **Index** | `index build`, `index extract`, `index refresh`, `index status` |
| **Discover** | `search class\|table\|edt\|enum\|form\|query\|view\|entity\|report\|service\|workflow\|label` |
| **Get** | `get table\|class\|edt\|enum\|form\|menu-item\|security\|label\|role\|duty\|privilege\|query\|view\|entity\|report\|service` |
| **Find** | `find coc`, `find relations`, `find usages`, `find extensions`, `find handlers`, `find refs`, `find form-patterns` |
| **Read** | `read class`, `read table`, `read form` |
| **Resolve** | `resolve label` |
| **Generate** | `generate table\|class\|coc\|form\|entity\|extension\|event-handler\|privilege\|duty\|role` |
| **Analyze** | `analyze completeness`, `lint`, `suggest extension` |
| **Review** | `review diff` |
| **Models** | `models list`, `models deps` |
| **Agent** | `agent-prompt`, `schema` |
| **Daemon** | `daemon start\|status\|stop` |
| **Ops (Windows VM)** | `build`, `sync`, `test run`, `bp check` |

See [`docs/EXAMPLES.md`](docs/EXAMPLES.md) for one worked example per command.

---

## Documentation

| Doc | What's inside |
|---|---|
| [docs/SETUP.md](docs/SETUP.md) | Install, configure, verify — dev alias vs. self-contained distribution |
| [docs/EXAMPLES.md](docs/EXAMPLES.md) | One worked example per command (discover, scaffold, review, ops, agents, daemon, CI) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Index schema (v9), guardrails, bridge, daemon file watcher |
| [docs/TOKEN_ECONOMICS.md](docs/TOKEN_ECONOMICS.md) | Why CLI+Skills is cheaper per turn, with numbers |
| [docs/MIGRATION_FROM_MCP.md](docs/MIGRATION_FROM_MCP.md) | Coming from `d365fo-mcp-server`? Read this first |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Planned and deferred items |

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `PACKAGES_PATH_NOT_FOUND` | Set `D365FO_PACKAGES_PATH` or pass `--packages <PATH>` |
| `UNSUPPORTED_PLATFORM` | `build` / `sync` / `test` / `bp` require Windows + a D365FO dev VM |
| `NO_INDEX` | Run `d365fo index build` then `d365fo index extract` |
| Index appears stale after editing XML | Run `d365fo index refresh --model <Model>` |
| Index file locked | Stop any running `d365fo daemon` or `d365fo-mcp` process; WAL sidecar files (`-wal`, `-shm`) are normal |

More in [docs/SETUP.md](docs/SETUP.md#troubleshooting).

---

## License

MIT. The sibling [`d365fo-mcp-server`](https://github.com/dynamics365ninja/d365fo-mcp-server) is also MIT.

---

## Disclaimer

This project is an independent research effort and is not affiliated with, endorsed by, or associated with Microsoft or any other organization. It is provided as-is for educational and development purposes.
