param(
    [Parameter(Mandatory = $true)]
    [string]$ReachyHost,

    [string]$ReachyUser = "reachy",

    [string]$ReachyPassword = "reachy",

    [switch]$PromptForPassword,

    [string]$RemoteProjectRoot = "~/reachy1-unityproject",

    [string]$PythonCommand = "python3",

    [int]$Port = 8101,

    [string]$TtsBackend = "auto",

    [string]$RemoteLogPath = "~/reachy_robot_speaker_server.log",

    [string]$LocalLogPath = ""
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($LocalLogPath)) {
    $LocalLogPath = Join-Path $scriptDirectory "start_reachy_robot_speaker_server_last_run.log"
}

if (Test-Path $LocalLogPath) {
    Remove-Item $LocalLogPath -Force -ErrorAction SilentlyContinue
}

function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )

    $safeMessage = if ($null -eq $Message) { "" } else { $Message }
    $line = "{0} [{1}] {2}" -f (Get-Date).ToString("s"), $Level.ToUpperInvariant(), $safeMessage
    Write-Host $line
    Add-Content -LiteralPath $LocalLogPath -Value $line
}

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function ConvertTo-PlainTextPassword {
    param([Security.SecureString]$SecurePassword)

    if ($null -eq $SecurePassword) {
        return ""
    }

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecurePassword)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

if ($PromptForPassword) {
    $securePassword = Read-Host "Reachy SSH password" -AsSecureString
    $ReachyPassword = ConvertTo-PlainTextPassword $securePassword
}

$localScriptPath = Join-Path $scriptDirectory "reachy_robot_speaker_server.py"
if (-not (Test-Path $localScriptPath)) {
    throw "Local script not found: $localScriptPath"
}

$target = "$ReachyUser@$ReachyHost"
$remoteDirectory = "$RemoteProjectRoot/Assets/ReachyControlApp/LocalVoiceAgent"
$remoteScriptPath = "$remoteDirectory/reachy_robot_speaker_server.py"

$plinkCommand = Get-Command "plink" -ErrorAction SilentlyContinue
$pscpCommand = Get-Command "pscp" -ErrorAction SilentlyContinue
$usePuttyBackend = $null -ne $plinkCommand -and $null -ne $pscpCommand

if ($usePuttyBackend) {
    Write-Log "Using PuTTY backend (plink/pscp) for remote copy/start."
}
else {
    Require-Command "ssh"
    Require-Command "scp"
    if (-not [string]::IsNullOrWhiteSpace($ReachyPassword)) {
        Write-Log "PuTTY tools were not found. Falling back to native ssh/scp, which may prompt interactively for the password." "WARN"
    }
    else {
        Write-Log "Using native ssh/scp backend."
    }
}

function Invoke-RemoteCommand {
    param(
        [string]$CommandText,
        [string]$StepName
    )

    Write-Log "$StepName on $target"
    if ($usePuttyBackend) {
        & plink -batch -pw $ReachyPassword $target $CommandText
    }
    else {
        & ssh $target $CommandText
    }

    if ($LASTEXITCODE -ne 0) {
        throw "$StepName failed on $target."
    }
}

function Copy-RemoteFile {
    param(
        [string]$SourcePath,
        [string]$TargetPath
    )

    Write-Log ("Copying {0} to {1}:{2}" -f $SourcePath, $target, $TargetPath)
    if ($usePuttyBackend) {
        & pscp -batch -pw $ReachyPassword $SourcePath "${target}:$TargetPath"
    }
    else {
        & scp $SourcePath "${target}:$TargetPath"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Copy to $target failed."
    }
}

Write-Log "Helper log path: $LocalLogPath"
Write-Log "Preparing remote directory '$remoteDirectory'."
Invoke-RemoteCommand -CommandText "mkdir -p '$remoteDirectory'" -StepName "Create remote directory"

Copy-RemoteFile -SourcePath $localScriptPath -TargetPath $remoteScriptPath

$startCommand =
    "nohup $PythonCommand '$remoteScriptPath' --bind-host 0.0.0.0 --bind-port $Port --tts-backend '$TtsBackend' > '$RemoteLogPath' 2>&1 < /dev/null &"
Write-Log "Starting robot speaker server via: $remoteScriptPath --bind-port $Port --tts-backend $TtsBackend"
Invoke-RemoteCommand -CommandText $startCommand -StepName "Start robot speaker server"

$healthCommand =
    "if command -v curl >/dev/null 2>&1; then " +
    "curl -fsS http://127.0.0.1:$Port/health; " +
    "elif command -v wget >/dev/null 2>&1; then " +
    "wget -qO- http://127.0.0.1:$Port/health; " +
    "else echo 'No curl/wget available to read /health.'; fi"

Write-Log "Checking remote health endpoint on port $Port."
if ($usePuttyBackend) {
    & plink -batch -pw $ReachyPassword $target $healthCommand
}
else {
    & ssh $target $healthCommand
}

if ($LASTEXITCODE -ne 0) {
    Write-Log "The server start command ran, but the immediate health check failed. Check $RemoteLogPath on the robot." "WARN"
}
else {
    Write-Log "Remote health probe completed successfully."
}

Write-Log "Remote log path: $RemoteLogPath"
