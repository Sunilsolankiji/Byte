param(
    [switch]$SkipNativeSmoke
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { $sdk = $dotnet.Source }
elseif (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
    $sdk = "$env:USERPROFILE\.dotnet\dotnet.exe"
    $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
}
else { throw 'Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0, then run this script again.' }
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force '.\artifacts' | Out-Null
    & $sdk run --project '.\Byte.Tests\Byte.Tests.csproj' -c Release |
        Tee-Object -FilePath '.\artifacts\core-tests.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & $sdk publish '.\Byte.App\Byte.App.csproj' -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -o '.\dist\Byte' --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Publishing Byte failed.' }
    Copy-Item '.\README.md', '.\LICENSE' '.\dist\Byte'
    if (!$SkipNativeSmoke) {
        $report = Join-Path $PSScriptRoot 'artifacts\native-smoke.json'
        if (Test-Path $report) { Remove-Item -LiteralPath $report }
        $app = Start-Process '.\dist\Byte\Byte.exe' -ArgumentList @('--smoke-test', ('"' + $report + '"')) -PassThru
        if (!$app.WaitForExit(90000)) {
            Stop-Process -Id $app.Id
            throw 'Native smoke tests timed out. Quit other running instances of Byte, then retry.'
        }
        if (!(Test-Path $report)) { throw 'Native smoke report was not produced.' }
        $result = Get-Content $report -Raw | ConvertFrom-Json
        if (!$result.passed) { throw ("Native smoke tests failed: " + $result.error) }
        Write-Output ("Native smoke: " + $result.checks.Count + " checks passed on " + $result.actualMonitorCount + " monitors.")
    }
    $zip = Join-Path $PSScriptRoot 'dist\Byte-win-x64.zip'
    Compress-Archive -Path '.\dist\Byte\*' -DestinationPath $zip -Force
    Get-FileHash $zip -Algorithm SHA256 |
        Format-List | Out-File '.\artifacts\package-sha256.txt'
    Write-Output "Runnable application: $PSScriptRoot\dist\Byte\Byte.exe"
    Write-Output "Portable package: $zip"
}
finally { Pop-Location }
