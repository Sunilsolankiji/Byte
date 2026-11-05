param(
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $exe = Join-Path $PSScriptRoot 'dist\Byte\Byte.exe'
    if (!(Test-Path $exe)) { throw 'Run .\build.ps1 before building the installer.' }
    if (!$Compiler) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $Compiler = $command.Source }
        else {
            foreach ($candidate in @(
                "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
                "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
                "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
                "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
            )) {
                if (Test-Path $candidate) { $Compiler = $candidate; break }
            }
        }
    }
    if (!$Compiler -or !(Test-Path $Compiler)) {
        throw 'Install Inno Setup 6.3 or later from https://jrsoftware.org/isdl.php, or pass -Compiler with the ISCC.exe path.'
    }
    [xml]$project = Get-Content '.\Byte.App\Byte.App.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The project must have a stable three-part release version.' }
    if ((Get-Item $exe).VersionInfo.ProductVersion.Split('+')[0] -ne $version) {
        throw 'Published application version differs from the source. Run .\build.ps1 again.'
    }
    & $Compiler /Qp ("/DByteVersion=$version") '.\installer\Byte.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $PSScriptRoot "dist\Byte-Setup-$version-win-x64.exe"
    if (!(Test-Path $installer)) { throw 'Installer output was not produced.' }
    $assets = @(
        $installer,
        (Join-Path $PSScriptRoot 'dist\Byte-win-x64.zip')
    )
    $lines = foreach ($asset in $assets) {
        if (!(Test-Path $asset)) { throw "Missing release asset: $asset" }
        '{0}  {1}' -f (Get-FileHash $asset -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $asset -Leaf)
    }
    $lines | Set-Content (Join-Path $PSScriptRoot 'dist\SHA256SUMS.txt') -Encoding ASCII
    Write-Output "Windows installer: $installer"
    Write-Output "Release checksums: $PSScriptRoot\dist\SHA256SUMS.txt"
}
finally { Pop-Location }
