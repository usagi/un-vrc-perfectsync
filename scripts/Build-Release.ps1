param(
  [Parameter(Mandatory = $true)]
  [string]$Version,

  [string]$BaseUrl = "https://github.com/usagi/un-vrc-perfectsync/releases/download/$Version",

  [string]$PackageRoot = "unity/un-vrc-perfectsync",

  [string]$OutputDir = "release-packages",

  [string]$RepoIndex = "docs/vcc/index.json"
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$') {
  throw "Version must be SemVer without a v prefix: $Version"
}

function GetRepoPath {
  param(
    [string]$Value
  )

  if ([System.IO.Path]::IsPathRooted($Value)) {
    return $Value
  }

  return Join-Path (Join-Path $PSScriptRoot "..") $Value
}

$packageRootPath = GetRepoPath -Value $PackageRoot
$outputDirPath = GetRepoPath -Value $OutputDir
$repoIndexPath = GetRepoPath -Value $RepoIndex

if (-not (Test-Path $packageRootPath -PathType Container)) {
  throw "Package root not found: $packageRootPath"
}

$packageJsonPath = Join-Path $packageRootPath "package.json"
$packageJson = Get-Content -LiteralPath $packageJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($packageJson.version -ne $Version) {
  throw "package.json version '$($packageJson.version)' does not match release version '$Version'."
}

$requiredPackageEntries = @(
  "package.json",
  "README.md",
  "CHANGELOG.md",
  "LICENSE.md",
  "Editor/UNVRC.PerfectSync.Editor.asmdef"
)
foreach ($entry in $requiredPackageEntries) {
  $entryPath = Join-Path $packageRootPath $entry
  if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) {
    throw "Required package file not found: $entry"
  }
}

$changelogPath = Join-Path $packageRootPath "CHANGELOG.md"
if (-not (Select-String -LiteralPath $changelogPath -SimpleMatch "## $Version" -Quiet)) {
  throw "CHANGELOG.md does not contain a '$Version' entry."
}

$packageName = $packageJson.name
$zipName = "$packageName-$Version.zip"
$packageUrl = "$($BaseUrl.TrimEnd('/'))/$zipName"
$packageJson | Add-Member -NotePropertyName "url" -NotePropertyValue $packageUrl -Force

$stagingRoot = Join-Path (GetRepoPath -Value "target") "vcc-package"
$stagingPackage = Join-Path $stagingRoot $packageName
if (Test-Path $stagingPackage) {
  Remove-Item -LiteralPath $stagingPackage -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingPackage | Out-Null
Get-ChildItem -LiteralPath $packageRootPath -Force | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName -Destination $stagingPackage -Recurse -Force
}

$stagedPackageJsonPath = Join-Path $stagingPackage "package.json"
$packageJson | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $stagedPackageJsonPath -Encoding UTF8

New-Item -ItemType Directory -Force -Path $outputDirPath | Out-Null
$zipPath = Join-Path $outputDirPath $zipName
if (Test-Path $zipPath) {
  Remove-Item -LiteralPath $zipPath -Force
}

Push-Location $stagingPackage
try {
  Compress-Archive -Path * -DestinationPath $zipPath -CompressionLevel Optimal
}
finally {
  Pop-Location
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
  $archiveEntries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
  foreach ($entry in $requiredPackageEntries) {
    if ($archiveEntries -notcontains $entry) {
      throw "Generated package is missing required entry: $entry"
    }
  }
}
finally {
  $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

if (Test-Path $repoIndexPath) {
  $index = Get-Content -LiteralPath $repoIndexPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
else {
  $index = [pscustomobject]@{
    name = "U.N. VRC PerfectSync"
    id = "network.usagi.vrc-perfectsync.repository"
    url = "https://usagi.github.io/un-vrc-perfectsync/vcc/index.json"
    author = "USAGI.NETWORK"
    packages = [pscustomobject]@{}
  }
}

if ($null -eq $index.packages) {
  $index | Add-Member -NotePropertyName "packages" -NotePropertyValue ([pscustomobject]@{}) -Force
}

$packageEntry = $index.packages.PSObject.Properties[$packageName]
if ($null -eq $packageEntry) {
  $index.packages | Add-Member -NotePropertyName $packageName -NotePropertyValue ([pscustomobject]@{ versions = [pscustomobject]@{} })
}
elseif ($null -eq $packageEntry.Value.versions) {
  $packageEntry.Value | Add-Member -NotePropertyName "versions" -NotePropertyValue ([pscustomobject]@{}) -Force
}

$manifestForIndex = $packageJson | ConvertTo-Json -Depth 20 | ConvertFrom-Json
$manifestForIndex | Add-Member -NotePropertyName "zipSHA256" -NotePropertyValue $hash -Force
$index.packages.$packageName.versions | Add-Member -NotePropertyName $Version -NotePropertyValue $manifestForIndex -Force

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $repoIndexPath) | Out-Null
$index | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $repoIndexPath -Encoding UTF8

Write-Host "package: $zipPath"
Write-Host "package_url: $packageUrl"
Write-Host "zipSHA256: $hash"
Write-Host "repo_index: $repoIndexPath"
