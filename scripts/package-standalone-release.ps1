param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$OutputPath = "",
    [string]$BuildRoot = "",
    [string]$SourceCommit = "",
    [string]$ReleaseTag = "",
    [switch]$TrackedDefaults
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $ProjectRoot "artifacts\reachy-standalone-windows-x64.zip"
}

if ([string]::IsNullOrWhiteSpace($BuildRoot)) {
    $BuildRoot = Join-Path $ProjectRoot "Build"
}
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
    $SourceCommit = (& git -C $ProjectRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not resolve the source commit." }
}
$SourceCommit = (& git -C $ProjectRoot rev-parse --verify ($SourceCommit + '^{commit}')).Trim()
if ($LASTEXITCODE -ne 0) { throw "Invalid source commit." }
$buildRoot = $BuildRoot
$releaseItems = @(
    ".local_voice_models",
    "MonoBleedingEdge",
    "Reachy controller & simulator_Data",
    "ReachyControlApp",
    "Reachy controller & simulator.exe",
    "UnityCrashHandler64.exe",
    "UnityPlayer.dll"
)

if (-not (Test-Path -LiteralPath $buildRoot)) {
    throw "Build directory was not found at '$buildRoot'."
}

$missingItems = @()
foreach ($item in $releaseItems) {
    $sourcePath = Join-Path $buildRoot $item
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        $missingItems += $sourcePath
    }
}

if ($missingItems.Count -gt 0) {
    throw "Standalone build is incomplete. Missing item(s): $($missingItems -join ', ')"
}

$outputDirectory = Split-Path -Parent $OutputPath
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    $outputDirectory = $ProjectRoot
    $OutputPath = Join-Path $outputDirectory $OutputPath
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

$checksumPath = "$OutputPath.sha256"
if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("reachy-standalone-release-" + [System.Guid]::NewGuid().ToString("N"))
$stagingRoot = [System.IO.Path]::GetFullPath($stagingRoot)
$stagingParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
if ([System.IO.Directory]::GetParent($stagingRoot).FullName -ne $stagingParent -or
    -not [System.IO.Path]::GetFileName($stagingRoot).StartsWith('reachy-standalone-release-')) {
    throw "Unexpected release staging directory."
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

try {
    foreach ($item in $releaseItems) {
        Copy-Item -LiteralPath (Join-Path $buildRoot $item) -Destination $stagingRoot -Recurse -Force
    }

    if ($TrackedDefaults) {
        foreach ($relativeConfig in @('voice_agent_config.json', 'LocalVoiceAgent/local_voice_agent_sidecar_config.json')) {
            $sourcePath = 'Assets/ReachyControlApp/' + $relativeConfig
            $sourceJson = & git -C $ProjectRoot show ($SourceCommit + ':' + $sourcePath)
            if ($LASTEXITCODE -ne 0) { throw "Could not load tracked configuration '$sourcePath'." }
            $configuration = ($sourceJson -join "`n") | ConvertFrom-Json
            if ($relativeConfig -eq 'voice_agent_config.json') {
                $configuration.sidecar_python_command = 'PythonRuntime\python.exe'
                $voiceDefaults = $configuration
            } else {
                $configuration.stt_model_path = '../../' + $voiceDefaults.model_path.Replace('\', '/')
                $configuration.help_model_path = '../../' + $voiceDefaults.help_model_path.Replace('\', '/')
            }
            $configDestination = Join-Path $stagingRoot ('ReachyControlApp/' + $relativeConfig)
            $configuration | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $configDestination -Encoding utf8
        }

        # Saved personalities and logs belong to the local operator, not the release defaults.
        $personalitiesPath = [System.IO.Path]::GetFullPath((Join-Path $stagingRoot 'ReachyControlApp/OnlineAiCustomPersonalities'))
        if (-not $personalitiesPath.StartsWith($stagingRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected personalities staging path."
        }
        if (Test-Path -LiteralPath $personalitiesPath) { Remove-Item -LiteralPath $personalitiesPath -Recurse -Force }
        New-Item -ItemType Directory -Path $personalitiesPath -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $stagingRoot 'ReachyControlApp') -Recurse -File -Filter '*.log' |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    }

    $assemblyRelativePath = 'Reachy controller & simulator_Data/Managed/Assembly-CSharp.dll'
    $buildInfo = [ordered]@{
        source_commit = $SourceCommit
        release_tag = $ReleaseTag
        unity_version = '2021.3.24f1'
        platform = 'Windows x64'
        assembly_sha256 = (Get-FileHash -LiteralPath (Join-Path $stagingRoot $assemblyRelativePath) -Algorithm SHA256).Hash.ToLowerInvariant()
        tracked_defaults = [bool]$TrackedDefaults
    }
    $buildInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagingRoot 'ReachyControlApp/build-info.json') -Encoding utf8

    $pathsToArchive = Get-ChildItem -LiteralPath $stagingRoot -Force | ForEach-Object { $_.FullName }
    if ($pathsToArchive.Count -eq 0) {
        throw "Nothing was staged for the standalone release archive."
    }

    Compress-Archive -LiteralPath $pathsToArchive -DestinationPath $OutputPath -CompressionLevel Optimal

    $hash = Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
    $checksumLine = "{0} *{1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $OutputPath)
    Set-Content -LiteralPath $checksumPath -Value $checksumLine -Encoding ascii

    $archiveSizeMb = [math]::Round((Get-Item -LiteralPath $OutputPath).Length / 1MB, 2)
    Write-Host "Created release archive at '$OutputPath' ($archiveSizeMb MB)."
    Write-Host "Wrote SHA256 checksum to '$checksumPath'."
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
