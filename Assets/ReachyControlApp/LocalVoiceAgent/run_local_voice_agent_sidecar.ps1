param(
    [string]$Python = "python",
    [string]$Config = "local_voice_agent_sidecar_config.json",
    [ValidateSet("debug", "info", "warning", "error")]
    [string]$LogLevel = "info"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$scriptPath = Join-Path $scriptDir "local_voice_agent_sidecar.py"
$configPath = if ([System.IO.Path]::IsPathRooted($Config)) { $Config } else { Join-Path $scriptDir $Config }
$bundledPython = Join-Path $scriptDir ".venv\\Scripts\\python.exe"
$pythonExe = $Python
if ($pythonExe -eq "python" -and (Test-Path $bundledPython)) {
    $pythonExe = $bundledPython
}

& $pythonExe $scriptPath --config $configPath --log-level $LogLevel
