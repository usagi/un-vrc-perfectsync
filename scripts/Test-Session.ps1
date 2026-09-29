param(
  [Parameter(Mandatory = $true)]
  [string]$UnityPath
)

$ErrorActionPreference = 'Stop'
$UnityPath = (Resolve-Path -LiteralPath $UnityPath).Path
$repoRoot = Split-Path $PSScriptRoot -Parent
$projectDir = Join-Path $repoRoot ('target/session-tests-' + [guid]::NewGuid().ToString('N'))
$testDir = Join-Path $projectDir 'Assets/SessionRegression'
$packageDir = Join-Path $repoRoot 'unity/un-vrc-perfectsync'
$logPath = Join-Path $projectDir 'regression.log'
New-Item -ItemType Directory -Path $testDir, (Join-Path $projectDir 'Packages') -Force | Out-Null
$manifest = @{
  dependencies = @{
    'network.usagi.vrc-perfectsync' = 'file:' + $packageDir.Replace('\', '/')
    'com.unity.modules.animation' = '1.0.0'
    'com.unity.modules.assetbundle' = '1.0.0'
    'com.unity.modules.imgui' = '1.0.0'
    'com.unity.modules.jsonserialize' = '1.0.0'
    'com.unity.modules.physics' = '1.0.0'
    'com.unity.modules.uielements' = '1.0.0'
  }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $projectDir 'Packages/manifest.json') -Encoding utf8
Copy-Item (Join-Path $PSScriptRoot 'Tests/SessionRegression.cs') $testDir
'{"reference":"UNVRC.PerfectSync.Editor"}' | Set-Content (Join-Path $testDir 'SessionRegression.asmref') -Encoding utf8
$unityArguments = @(
  '-batchmode', '-nographics', '-quit',
  '-projectPath', ('"' + $projectDir + '"'),
  '-executeMethod', 'UNVRC.PerfectSync.Editor.SessionRegression.Run',
  '-logFile', ('"' + $logPath + '"')
)
Write-Output "Regression log: $logPath"
$process = Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
$log = Get-Content -LiteralPath $logPath -Raw
$log -split '\r?\n' | Where-Object { $_ -match '^(PASS:|FAIL:|PERFECTSYNC_REGRESSION:)' } | Write-Output
if ($process.ExitCode -ne 0 -or $log -notmatch 'PERFECTSYNC_REGRESSION: all passed') {
  throw "Unity regression failed (exit $($process.ExitCode)). See $logPath"
}
