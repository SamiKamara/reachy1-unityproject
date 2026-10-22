param(
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\2021.3.24f1\Editor',
    [switch]$Live
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path $projectRoot 'Temp\EthernetChecks'
New-Item -ItemType Directory -Force -Path $testOutput | Out-Null

$testArgs = [System.Collections.Generic.List[string]]::new()
$testArgs.Add('/nologo')
$testArgs.Add('/target:exe')
$testArgs.Add('/langversion:9.0')
$testArgs.Add('/reference:"' + (Join-Path $UnityEditorPath 'Data\UnityReferenceAssemblies\unity-4.8-api\Facades\System.Runtime.dll') + '"')
$testArgs.Add('/out:"' + (Join-Path $testOutput 'EthernetDiscoveryTests.exe') + '"')
$pluginPaths = @(
    'Assets\Plugins\Grpc.Core\lib\net45\Grpc.Core.dll',
    'Assets\Plugins\Grpc.Core.Api\lib\net45\Grpc.Core.Api.dll',
    'Assets\Plugins\Google.Protobuf\lib\net45\Google.Protobuf.dll',
    'Assets\Plugins\System.Memory\lib\net45\System.Memory.dll',
    'Assets\Plugins\System.Buffers\lib\net45\System.Buffers.dll',
    'Assets\Plugins\System.Runtime.CompilerServices.Unsafe\lib\net45\System.Runtime.CompilerServices.Unsafe.dll'
)
foreach ($relativePath in $pluginPaths) {
    $pluginPath = Join-Path $projectRoot $relativePath
    $testArgs.Add('/reference:"' + $pluginPath + '"')
    Copy-Item -LiteralPath $pluginPath -Destination $testOutput -Force
}
# Compile the same generated protobuf sources used by the Unity robot client.
foreach ($sourceName in @('Joint.cs', 'JointGrpc.cs')) {
    $testArgs.Add('"' + (Join-Path $projectRoot "Packages\ReachySimulator\Scripts\$sourceName") + '"')
}
$testArgs.Add('"' + (Join-Path $projectRoot 'Assets\ReachyControlApp\Scripts\ReachyEthernetDiscovery.cs') + '"')
$testArgs.Add('"' + (Join-Path $PSScriptRoot 'EthernetDiscoveryTests.cs') + '"')
Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets\Plugins\Grpc.Core\runtimes\win\x64\grpc_csharp_ext.dll') -Destination (Join-Path $testOutput 'grpc_csharp_ext.x64.dll') -Force
$responsePath = Join-Path $testOutput 'tests.rsp'
[System.IO.File]::WriteAllLines($responsePath, $testArgs)
& (Join-Path $UnityEditorPath 'Data\MonoBleedingEdge\bin\mono.exe') `
    (Join-Path $UnityEditorPath 'Data\MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe') "@$responsePath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Live) { & (Join-Path $testOutput 'EthernetDiscoveryTests.exe') --live }
else { & (Join-Path $testOutput 'EthernetDiscoveryTests.exe') }
exit $LASTEXITCODE
