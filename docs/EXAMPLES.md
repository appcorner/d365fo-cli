# Examples

One worked example per command. Setup (install, env vars, first run) lives in [SETUP.md](SETUP.md).

Every example assumes `d365fo` is on your `PATH` and `D365FO_PACKAGES_PATH` + a populated index are in place.

---

## Output contract

Every command returns a predictable result:

- **Interactive terminal** → rendered tables.
- **Piped / script / CI** → JSON envelope.
- Force either with `--output json|table|raw`.

The JSON envelope is always one of:

```json
{ "ok": true,  "data": { /* … */ }, "warnings": [] }
{ "ok": false, "error": { "code": "…", "message": "…", "hint": "…" } }
```

**Exit codes:** `0` success · `1` controlled failure (error envelope still prints) · `2` unhandled exception.

---

## Discover

### `search` — fuzzy-find AOT objects

```sh
d365fo search class Cust
```

Same pattern for `search table|edt|enum|form|query|view|entity|report|service|workflow|label`. `search label` sanitises control characters by default; pass `--raw-text` to opt out.

### `get` — full metadata for one object

```sh
d365fo get table CustTable
```

Works for `get class|edt|enum|form|menu-item|security|label|role|duty|privilege|query|view|entity|report|service|service-group`. Misspelled names return a `*_NOT_FOUND` envelope with a Levenshtein-ranked `hint: "Did you mean: …"`.

Rewrite `@File+Id` tokens to human text with `--resolve-labels` (language picked from `D365FO_LABEL_LANGUAGES`, falls back to `en-us`):

```sh
d365fo get table CustTable --resolve-labels
```

### `find` — trace cross-references

```sh
d365fo find coc CustTable::validateWrite
```

Also available: `find relations|usages|extensions|handlers|refs`. `find refs --xref` queries `DYNAMICSXREFDB` through the bridge for path/line/column/kind precision.

### `resolve label` — look up a label token

```sh
d365fo resolve label @SYS12345 --lang en-US,cs
```

### `read` — pull X++ source from AOT XML

```sh
d365fo read class CustTable_Extension --method validateWriteExt
```

`read table` and `read form` work the same way; add `--lines 10-40` or `--declaration` to scope the snippet.

### `models` — inspect indexed models

```sh
d365fo models deps ApplicationSuite
```

`models list` enumerates every model with publisher, layer, and custom-flag.

---

## Scaffold

`generate` writes atomically (`.tmp` + move) and keeps a `.bak` when `--overwrite` is used. Pass `--install-to <Model>` to drop the artefact straight into a model folder via the bridge (requires `D365FO_BRIDGE_ENABLED=1`, `D365FO_PACKAGES_PATH`, `D365FO_BIN_PATH`).

### Table

```sh
d365fo generate table FmVehicle \
  --label "@Fleet:Vehicle" \
  --field VIN:VinEdt:mandatory \
  --field Make:Name \
  --out src/MyModel/AxTable/FmVehicle.xml
```

### Class

```sh
d365fo generate class FmVehicleService --extends RunBase \
  --out src/MyModel/AxClass/FmVehicleService.xml
```

### Chain-of-Command extension

```sh
d365fo generate coc CustTable --method update --method insert \
  --out src/MyModel/AxClass/CustTable_Extension.xml
```

### Simple-list form

```sh
d365fo generate simple-list FmVehicleListPage --table FmVehicle \
  --out src/MyModel/AxForm/FmVehicleListPage.xml
```

---

## Review

```sh
d365fo review diff --base HEAD
```

Compare two revs with `--base main --head feature/my-branch`. Rules shipped today:

- `FIELD_WITHOUT_EDT` — table field without `<ExtendedDataType>`.
- `FIELD_WITHOUT_LABEL` — user-facing field without `<Label>`.
- `HARDCODED_STRING` — verbatim string literal in X++ source.
- `DYNAMIC_QUERY` — dynamic `Query` construction (flag for security review).

---

## Windows-only ops (D365FO VM)

These commands wrap the Microsoft tooling Visual Studio uses, so you can drive the IDE's workflow from a terminal, script, or CI pipeline.

```powershell
d365fo build --project C:\AosService\PackagesLocalDirectory\MyModel\MyModel.rnrproj
d365fo sync --full
d365fo test run --suite MyModel.Tests
d365fo bp check --model MyModel
```

Each parses the tool output and returns a structured JSON envelope (errors, warnings, elapsed time, tail of stdout). On non-Windows they return `UNSUPPORTED_PLATFORM`.

---

## Agent integration

### Emit the system prompt

```sh
d365fo agent-prompt --out .prompts/d365fo.md
```

`d365fo schema --full` emits a machine-readable catalog of every command.

### GitHub Copilot (VS Code / Visual Studio)

```sh
cp skills/copilot/* .github/instructions/
d365fo agent-prompt --out .github/copilot-instructions.md
```

Copilot picks up `.github/instructions/*.instructions.md` via `applyTo` globs and drives `d365fo` through its terminal tool.

### Claude Code / Claude Desktop

Drop `skills/anthropic/` into the project or `~/.claude/skills/`. Each `SKILL.md` triggers via its `applies_when` front-matter.

### Codex CLI / Gemini CLI

Paste the output of `d365fo agent-prompt` into the session system prompt, or reference it from `AGENTS.md`.

### MCP server (`d365fo-mcp`)

Standalone JSON-RPC 2.0 server (protocol `2024-11-05`) that shares the CLI's index. Config sample for Claude Desktop:

```jsonc
{
  "mcpServers": {
    "d365fo": {
      "command": "dotnet",
      "args": ["run", "--project", "/abs/path/to/src/D365FO.Mcp", "--no-build"],
      "env": { "D365FO_INDEX_DB": "/abs/path/d365fo-index.sqlite" }
    }
  }
}
```

After `dotnet publish src/D365FO.Mcp -c Release -r osx-arm64` you get a standalone `d365fo-mcp` binary you can drop on `$PATH`. The adapter exposes 16 read tools (search / get / find / index_status) — a focused subset of the CLI surface. Items still missing from MCP are tracked in [ROADMAP.md](ROADMAP.md).

---

## Daemon (warm cache)

For latency-sensitive integrations, run the CLI as a daemon so the SQLite handle and read caches stay hot:

```sh
d365fo daemon start
d365fo daemon status
d365fo daemon stop
```

Transport: Windows named pipe `\\.\pipe\d365fo-cli`; Unix socket at `$XDG_RUNTIME_DIR/d365fo-cli.sock` (fallback `$TMPDIR`). The frame format matches `d365fo-mcp`: one newline-terminated JSON-RPC request per connection, one response, close.

---

## CI / automation

Every command is scriptable: exit codes are reliable, output is JSON by default in non-TTY, no interactive prompts.

```yaml
- name: D365 review
  run: |
    d365fo index build
    d365fo review diff --base origin/main --head HEAD --output json \
      | jq -e '.data.violationCount == 0'
```
