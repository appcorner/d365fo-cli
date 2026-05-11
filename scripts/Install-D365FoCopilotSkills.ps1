<#
.SYNOPSIS
    Deploys d365fo Copilot Skills and the X++ rule canon to an X++ project repo.

.DESCRIPTION
    Copies:
      - .github/copilot-instructions.md  (full X++ / CoC / BP rule canon)
      - .github/instructions/*.instructions.md  (15 topic Skills)
    from this d365fo-cli clone into the target X++ project repository so that
    GitHub Copilot in Visual Studio 2022 / 2026 has the D365FO rule canon in
    scope without manual setup.

    Re-run after pulling updates to d365fo-cli to keep Skills current.

.PARAMETER CliRepo
    Absolute path to your d365fo-cli clone.
    Default: the parent of the directory that contains this script.

.PARAMETER XppRepo
    Absolute path to the root of your X++ project repository (the folder that
    contains — or will contain — the .github/ directory).

.EXAMPLE
    .\Install-D365FoCopilotSkills.ps1 `
        -CliRepo  "C:\source\d365fo-cli" `
        -XppRepo  "K:\D365FO\MyProject"

.EXAMPLE
    # Run from inside the d365fo-cli scripts folder — CliRepo is auto-detected
    .\Install-D365FoCopilotSkills.ps1 -XppRepo "K:\D365FO\MyProject"

.NOTES
    After running, commit .github/ in your X++ repo so teammates get the same
    Copilot context automatically:
        git add .github/
        git commit -m "chore: add d365fo Copilot skills"
#>

[CmdletBinding()]
param(
    [string] $CliRepo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [Parameter(Mandatory)][string] $XppRepo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve paths ──────────────────────────────────────────────────────────────
$skillsSrc  = Join-Path $CliRepo 'skills\copilot'
$canonSrc   = Join-Path $CliRepo '.github\copilot-instructions.md'
$dstRoot    = Join-Path $XppRepo '.github'
$dstInstr   = Join-Path $dstRoot 'instructions'

Write-Host "d365fo-cli repo : $CliRepo"
Write-Host "X++ project repo: $XppRepo"
Write-Host ""

# ── Validate source ────────────────────────────────────────────────────────────
if (-not (Test-Path $CliRepo)) {
    Write-Error "CliRepo not found: $CliRepo"
}
if (-not (Test-Path $XppRepo)) {
    Write-Error "XppRepo not found: $XppRepo"
}

# ── Regenerate Skills if the source folder is stale ───────────────────────────
$skillFiles = Get-ChildItem -Path $skillsSrc -Filter '*.instructions.md' -ErrorAction SilentlyContinue
if ($skillFiles.Count -eq 0) {
    Write-Warning "No *.instructions.md found in $skillsSrc - running emit-skills.py first..."
    $py = Get-Command python -ErrorAction SilentlyContinue
    if (-not $py) { $py = Get-Command python3 -ErrorAction SilentlyContinue }
    if ($py) {
        & $py.Source (Join-Path $CliRepo 'scripts\emit-skills.py') `
              --source (Join-Path $CliRepo 'skills\_source') `
              --out-root (Join-Path $CliRepo 'skills')
        $skillFiles = Get-ChildItem -Path $skillsSrc -Filter '*.instructions.md'
    } else {
        Write-Warning "Python not found. Run 'python scripts/emit-skills.py' manually in the d365fo-cli repo, then re-run this script."
    }
}

# ── Create target directories ─────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $dstRoot  | Out-Null
New-Item -ItemType Directory -Force -Path $dstInstr | Out-Null

# ── Copy X++ rule canon ────────────────────────────────────────────────────────
if (Test-Path $canonSrc) {
    Copy-Item -Path $canonSrc -Destination $dstRoot -Force
    Write-Host "[OK] copilot-instructions.md"
} else {
    Write-Warning "copilot-instructions.md not found at: $canonSrc"
}

# ── Copy Skills ────────────────────────────────────────────────────────────────
$copied = 0
foreach ($f in $skillFiles) {
    Copy-Item -Path $f.FullName -Destination $dstInstr -Force
    Write-Host "[OK] instructions\$($f.Name)"
    $copied++
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Deployed $copied skill(s) + copilot-instructions.md to:"
Write-Host "  $dstRoot"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Restart Visual Studio to pick up the new instructions."
Write-Host "  2. Commit .github/ in your X++ project:"
Write-Host "       git -C `"$XppRepo`" add .github/"
Write-Host "       git -C `"$XppRepo`" commit -m `"chore: add d365fo Copilot skills`""
