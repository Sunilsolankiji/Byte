param(
    [string]$Installer
)
$ErrorActionPreference = 'Stop'
if (!$Installer) {
    [xml]$project = Get-Content (Join-Path $PSScriptRoot 'Byte.App\Byte.App.csproj') -Raw
    $Installer = Join-Path $PSScriptRoot "dist\Byte-Setup-$($project.Project.PropertyGroup.Version)-win-x64.exe"
}
if (!(Test-Path $Installer)) { throw 'Run .\build-installer.ps1 first.' }
if (Get-Process Byte -ErrorAction SilentlyContinue) { throw 'Quit Byte from its tray before running installer checks.' }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{98511899-8AC7-42F0-947C-750F85F5D827}_is1'
if (Test-Path $uninstallKey) { throw 'An installed Byte already exists. Run installer checks on a user account without it.' }
$id = [Guid]::NewGuid().ToString('N')
$folder = Join-Path $env:TEMP "Byte-installer-test-$id"
$group = "Byte installer test $id"
$shortcut = Join-Path ([Environment]::GetFolderPath('StartMenu')) "Programs\$group\Byte.lnk"
$probePath = Join-Path $env:TEMP "Byte-installer-probe-$id.tmp"
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupName = 'ByteDesktopCompanion'
$key = Get-Item $startupKey -ErrorAction SilentlyContinue
$hadStartup = $key -and $key.GetValueNames() -contains $startupName
$startup = if ($hadStartup) { $key.GetValue($startupName) } else { $null }
$checks = [System.Collections.Generic.List[string]]::new()
$probe = $null
function Check($condition, [string]$label) {
    if (!$condition) { throw "Installer check failed: $label" }
    $checks.Add($label)
}
function Run-Setup {
    $process = Start-Process -FilePath $Installer -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=""',
        ("/DIR=`"$folder`""), ("/GROUP=`"$group`"")
    ) -PassThru -Wait
    Check ($process.ExitCode -eq 0) 'Per-user installer exits successfully'
}
function Run-Uninstall {
    $process = Start-Process (Join-Path $folder 'unins000.exe') -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
    ) -PassThru -Wait
    Check ($process.ExitCode -eq 0) 'Uninstaller exits successfully'
    for ($i = 0; $i -lt 40 -and (Test-Path (Join-Path $folder 'Byte.exe')); $i++) { Start-Sleep -Milliseconds 250 }
    Check (!(Test-Path (Join-Path $folder 'Byte.exe'))) 'Uninstaller removes application payload'
    Check (!(Test-Path $uninstallKey)) 'Uninstaller removes Windows Installed apps registration'
    Check (!(Test-Path $shortcut)) 'Uninstaller removes Start menu shortcut'
}
$savedFiles = @{}
foreach ($name in @('settings.json', 'personality.json')) {
    $path = Join-Path $env:LOCALAPPDATA "Byte\$name"
    $savedFiles[$path] = if (Test-Path $path) { (Get-FileHash $path).Hash } else { $null }
}
try {
    Run-Setup
    Check (Test-Path $uninstallKey) 'Installer registers Byte under current-user Installed apps'
    Check (Test-Path $shortcut) 'Installer creates current-user Start menu shortcut'
    $shell = New-Object -ComObject WScript.Shell
    Check ($shell.CreateShortcut($shortcut).TargetPath -eq (Join-Path $folder 'Byte.exe')) 'Start menu shortcut targets installed executable'
    Check ((Get-FileHash (Join-Path $folder 'Byte.exe')).Hash -eq
        (Get-FileHash (Join-Path $PSScriptRoot 'dist\Byte\Byte.exe')).Hash) 'Installed executable matches validated portable executable exactly'
    $currentStartup = (Get-Item $startupKey).GetValue($startupName)
    Check ($currentStartup -eq $startup) 'Installation never enables or changes launch at login'
    Run-Setup
    Check ((Get-FileHash (Join-Path $folder 'Byte.exe')).Hash -eq
        (Get-FileHash (Join-Path $PSScriptRoot 'dist\Byte\Byte.exe')).Hash) 'Reinstallation updates in place without damaging the payload'
    $probe = Start-Process (Join-Path $folder 'Byte.exe') -ArgumentList @(
        '--probe', "`"$probePath`"", '20', '20'
    ) -PassThru
    for ($i = 0; $i -lt 80 -and !(Test-Path $probePath); $i++) { Start-Sleep -Milliseconds 100 }
    Check ((Test-Path $probePath) -and !$probe.HasExited) 'Installed self-contained executable starts its native WPF test window'
    $probe.CloseMainWindow() | Out-Null
    if (!$probe.WaitForExit(5000)) { Stop-Process -Id $probe.Id }
    $probe = $null
    Set-ItemProperty $startupKey -Name $startupName -Value '"C:\Byte-portable-test\Byte.exe"'
    Run-Uninstall
    Check ((Get-Item $startupKey).GetValue($startupName) -eq '"C:\Byte-portable-test\Byte.exe"') 'Uninstall preserves another portable copy login entry'
    Run-Setup
    Set-ItemProperty $startupKey -Name $startupName -Value ('"' + (Join-Path $folder 'Byte.exe') + '"')
    Run-Uninstall
    Check ($null -eq (Get-Item $startupKey).GetValue($startupName)) 'Uninstall removes login entry pointing to the installed copy'
    foreach ($path in $savedFiles.Keys) {
        $hash = if (Test-Path $path) { (Get-FileHash $path).Hash } else { $null }
        Check ($hash -eq $savedFiles[$path]) ('Install/uninstall preserves ' + (Split-Path $path -Leaf))
    }
    New-Item -ItemType Directory (Join-Path $PSScriptRoot 'artifacts') -Force | Out-Null
    [pscustomobject]@{ passed = $true; checks = $checks.ToArray(); installer = Split-Path $Installer -Leaf } |
        ConvertTo-Json -Depth 3 | Set-Content (Join-Path $PSScriptRoot 'artifacts\installer-smoke.json')
    Write-Output ("Installer smoke: " + $checks.Count + ' checks passed.')
}
finally {
    try {
        if ($probe -and !$probe.HasExited) { Stop-Process -Id $probe.Id }
        if (Test-Path (Join-Path $folder 'unins000.exe')) { Run-Uninstall }
    }
    finally {
        if ($hadStartup) { Set-ItemProperty $startupKey -Name $startupName -Value $startup }
        else { Remove-ItemProperty $startupKey -Name $startupName -ErrorAction SilentlyContinue }
        foreach ($path in @($probePath, "$probePath.clicks")) {
            if (Test-Path $path) { Remove-Item -LiteralPath $path }
        }
        if ((Test-Path $folder) -and !(Get-ChildItem $folder -Force)) { Remove-Item -LiteralPath $folder }
    }
}
