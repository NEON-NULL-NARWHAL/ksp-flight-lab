# Ships a KSP Flight Lab release: runs the tests, builds the exe, and publishes a GitHub release with it attached.
# Every copy of Flight Lab then offers the update by itself (Settings → Updates).
# Before running: bump <Version> in src\KspFlightLab\KspFlightLab.csproj, add a "## <version>" section to CHANGELOG.md,
# update README's "**Version x.y" line, commit and push. Guide: Meat and Potatoes/05-Install-And-Settings.md
param([switch]$SkipTests, [switch]$Draft)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = ([xml](Get-Content "src\KspFlightLab\KspFlightLab.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$tag = "v$version"
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "C:\Program Files\GitHub CLI\gh.exe" }

# The release notes are this version's CHANGELOG section; refuse to ship without one.
$changelog = Get-Content CHANGELOG.md -Raw
$section = [regex]::Match($changelog, "(?ms)^## $([regex]::Escape($version))\b.*?(?=^## |\z)").Value.Trim()
if (-not $section) { throw "CHANGELOG.md has no '## $version' section. Write one first." }
$short = ($version -split '\.')[0..1] -join '.'
if (-not (Select-String -Path README.md -SimpleMatch "**Version $short" -Quiet)) { throw "README.md doesn't say **Version $short. Update the version line first." }
if (git status --porcelain) { throw "There are uncommitted changes. Commit and push them first, so the release matches the code." }
# "release not found" on stderr is the answer we want; Windows PowerShell would treat it as an error under "Stop".
$ErrorActionPreference = "Continue"
& $gh release view $tag 2>&1 | Out-Null
$exists = $LASTEXITCODE -eq 0
$ErrorActionPreference = "Stop"
if ($exists) { throw "Release $tag already exists. Bump the version first." }

if (-not $SkipTests) {
    dotnet test KspFlightLab.slnx
    if ($LASTEXITCODE -ne 0) { throw "Tests failed; nothing was released." }
}
.\publish.ps1 | Out-Null
$exe = "dist\KspFlightLab-$version-win-x64\KspFlightLab.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()

$notes = @"
**Download ``KspFlightLab.exe`` below.** One file for Windows 10/11 x64, nothing to install. Already have Flight Lab? It offers this update by itself (Settings → Updates).

SHA-256: ``$hash`` (check with ``Get-FileHash .\KspFlightLab.exe``). The exe isn't code-signed, so SmartScreen may ask the first time.

$section
"@
$notesFile = New-TemporaryFile
Set-Content $notesFile $notes -Encoding utf8
$flags = @("--title", "KSP Flight Lab $version", "--notes-file", $notesFile, "--target", (git rev-parse HEAD))
if ($Draft) { $flags += "--draft" } else { $flags += "--latest" }
& $gh release create $tag $exe @flags
Remove-Item $notesFile
if ($LASTEXITCODE -ne 0) { throw "gh couldn't create the release." }
"Released $tag ($hash)."
