# fetch-builtin-content.ps1
# Run from repo root: d:\Development\aircane-tabletop
# Usage: .\fetch-builtin-content.ps1

$ErrorActionPreference = "Stop"
$basePath = "src\backend\Aircane.Workers\Resources\builtin"
$tempDir  = "$env:TEMP\aircane-srd-fetch"

if (-not (Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir -Force | Out-Null }

# ---------------------------------------------------------------------------
# D&D 5e SRD 5.1 — CC BY 4.0
# Repo: https://github.com/oldmanumby/dnd.srd.5.1
# Structure: numbered folders 01_Races, 02_Classes, 06_Gameplay, 07_Spells, etc.
# Fastest approach: download the ZIP, extract, copy the folders we want.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== D&D 5e SRD 5.1 (CC BY 4.0) ===" -ForegroundColor Yellow

$srdZip     = "$tempDir\dnd5e-srd.zip"
$srdExtract = "$tempDir\dnd5e-srd"
$srdDest    = "$basePath\dnd5e_srd"

Write-Host "  Downloading ZIP..." -ForegroundColor Cyan
Invoke-WebRequest `
    -Uri "https://github.com/oldmanumby/dnd.srd.5.1/archive/refs/heads/main.zip" `
    -OutFile $srdZip `
    -UseBasicParsing

Write-Host "  Extracting..." -ForegroundColor Cyan
Expand-Archive -Path $srdZip -DestinationPath $srdExtract -Force

# The ZIP extracts to a subfolder named dnd.srd.5.1-main
$srdRoot = "$srdExtract\dnd.srd.5.1-main"

# Map each source folder to a single merged destination .md file.
# All .md files within a folder are concatenated in alphabetical order.
$srdMappings = @(
    @{ src = "01_Races";            dest = "races.md"           },
    @{ src = "02_Classes";          dest = "classes.md"         },
    @{ src = "03_Characterization"; dest = "characterization.md"},
    @{ src = "04_Equipment";        dest = "equipment.md"       },
    @{ src = "05_Feats";            dest = "feats.md"           },
    @{ src = "06_Gameplay";         dest = "gameplay.md"        },
    @{ src = "07_Spells";           dest = "spells.md"          },
    @{ src = "08_Gamemastering";    dest = "gamemastering.md"   },
    @{ src = "09_Magic_Items";      dest = "magic-items.md"     },
    @{ src = "10_Monsters";         dest = "monsters.md"        }
)

if (-not (Test-Path $srdDest)) { New-Item -ItemType Directory -Path $srdDest -Force | Out-Null }

foreach ($m in $srdMappings) {
    $srcFolder = "$srdRoot\$($m.src)"
    $destFile  = "$srdDest\$($m.dest)"

    if (-not (Test-Path $srcFolder)) {
        Write-Host "  SKIP (folder not found): $($m.src)" -ForegroundColor Red
        continue
    }

    $mdFiles = Get-ChildItem -Path $srcFolder -Filter "*.md" | Sort-Object Name
    $content = $mdFiles | ForEach-Object { Get-Content $_.FullName -Raw }
    ($content -join "`n`n") | Set-Content -Path $destFile -Encoding UTF8NoBOM
    $size = [math]::Round((Get-Item $destFile).Length / 1KB, 1)
    Write-Host "  [OK] $($m.src) -> $($m.dest) ($size KB, $($mdFiles.Count) files merged)" -ForegroundColor Green
}

# Grab Legal.md (CC BY attribution text)
Copy-Item "$srdRoot\Legal.md" "$srdDest\Legal.md" -Force
Write-Host "  [OK] Legal.md" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== SRD written to $srdDest\ ===" -ForegroundColor Green
Write-Host ""
Write-Host "Files:" -ForegroundColor Cyan
Get-ChildItem $srdDest | ForEach-Object {
    $kb = [math]::Round($_.Length / 1KB, 1)
    Write-Host "  $($_.Name.PadRight(30)) $kb KB" -ForegroundColor White
}

Write-Host ""
Write-Host "Next: update dnd5e_srd\manifest.json files[] to reference these filenames," -ForegroundColor Yellow
Write-Host "then hand to Kiro to dotnet build and verify seeder chunk counts." -ForegroundColor Yellow

Write-Host ""
Write-Host "=== PF2e Remaster (ORC) — manual step ===" -ForegroundColor Yellow
Write-Host "  No bulk markdown export exists. Visit:" -ForegroundColor White
Write-Host "  https://2e.aonprd.com/Rules.aspx  ->  save sections as .md to $basePath\pf2e_remaster\" -ForegroundColor Cyan

Write-Host ""
Write-Host "=== PF1e PRD (OGL) — manual step ===" -ForegroundColor Yellow
Write-Host "  If Pandoc is installed, run per section e.g.:" -ForegroundColor White
Write-Host "  pandoc -f html -t markdown https://paizo.com/pathfinderRPG/prd/coreRulebook/combat.html -o $basePath\pf1e_prd\combat.md" -ForegroundColor Cyan
Write-Host "  Otherwise copy OGL content from d20pfsrd.com to .md files manually." -ForegroundColor White

Write-Host ""
Remove-Item $tempDir -Recurse -Force
Write-Host "Temp files cleaned up." -ForegroundColor DarkGray
