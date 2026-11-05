param(
    [string]$SignTool,
    [switch]$MachineTrust
)
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content (Join-Path $PSScriptRoot 'Byte.App\Byte.App.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$package = Get-Item (Join-Path $PSScriptRoot "dist\Byte-$version-win-x64-local-test.msix") -ErrorAction SilentlyContinue
if (!$package) { throw 'Run .\build-msix.ps1 -TestPackage first.' }
if (Get-AppxPackage -Name Byte.LocalTest) { throw 'Byte.LocalTest is already installed; refusing to replace an existing test package.' }
if (Get-Process Byte -ErrorAction SilentlyContinue) { throw 'Quit running Byte from its tray before native MSIX checks.' }
if (!$SignTool) {
    $SignTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory |
        Where-Object Name -Match '^10\.0\.\d+\.0$' | Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (!$SignTool -or !(Test-Path $SignTool)) { throw 'Install Windows SDK signing tools or pass -SignTool.' }
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory $artifacts -Force | Out-Null
$id = [Guid]::NewGuid().ToString('N')
$signed = Join-Path $artifacts "Byte-msix-test-$id.msix"
$cer = Join-Path $artifacts "Byte-msix-test-$id.cer"
$reportPath = Join-Path $artifacts 'msix-native-smoke.json'
$cert = $null
$trusted = $null
$machineTrusted = $false
$installed = $null
$process = $null
$startupKey = Get-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
$startupBefore = if ($startupKey) { $startupKey.GetValue('ByteDesktopCompanion') } else { $null }
function Set-MachineTrust([string]$action) {
    $helper = Join-Path $PSScriptRoot 'msix\test-certificate-trust.ps1'
    $arguments = @('-NoProfile', '-File', "`"$helper`"", '-Action', $action, '-Thumbprint', $cert.Thumbprint)
    if ($action -eq 'Import') { $arguments += @('-CertificatePath', "`"$cer`"") }
    $elevated = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -PassThru -Wait
    if ($elevated.ExitCode -ne 0) { throw "Temporary machine certificate $action failed: $($elevated.ExitCode)" }
}
try {
    $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=Byte Local Test' -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyUsage DigitalSignature -FriendlyName "Byte temporary MSIX validation $id" `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') -NotAfter (Get-Date).AddDays(1)
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    if ($MachineTrust) {
        Set-MachineTrust 'Import'
        $machineTrusted = $true
    }
    else { $trusted = Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' }
    Copy-Item $package.FullName $signed
    & $SignTool sign /fd SHA256 /s My /sha1 $cert.Thumbprint $signed
    if ($LASTEXITCODE -ne 0) { throw 'Local test signing failed.' }
    $signature = Get-AuthenticodeSignature $signed
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
        throw 'Test package signer differs from the temporary certificate.'
    }
    # MSIX deployment checks the signature against TrustedPeople; no test root CA is installed.
    Add-AppxPackage -Path $signed
    $installed = Get-AppxPackage -Name Byte.LocalTest
    if (!$installed) { throw 'MSIX registration was not found after install.' }
    if (Test-Path $reportPath) { Remove-Item -LiteralPath $reportPath }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BytePackageTestLauncher {
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
        [PreserveSig] int ActivateForFile(IntPtr items, string verb, out uint processId);
        [PreserveSig] int ActivateForProtocol(IntPtr items, out uint processId);
    }
    public static uint Launch(string app, string arguments) {
        var obj = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
        try {
            uint pid;
            Marshal.ThrowExceptionForHR(((IApplicationActivationManager)obj).ActivateApplication(app, arguments, 0, out pid));
            return pid;
        } finally { Marshal.FinalReleaseComObject(obj); }
    }
}
'@
    Write-Output 'Native mouse checks begin in five seconds; leave the pointer idle until cleanup.'
    Start-Sleep -Seconds 5
    $appProcessId = [BytePackageTestLauncher]::Launch(($installed.PackageFamilyName + '!Byte'), ('--smoke-test "' + $reportPath + '"'))
    $process = Get-Process -Id $appProcessId
    if (!$process.WaitForExit(90000)) { throw 'Packaged native smoke tests timed out.' }
    if (!(Test-Path $reportPath)) { throw 'Packaged native smoke report was not created.' }
    $report = Get-Content $reportPath -Raw | ConvertFrom-Json
    if (!$report.passed -or !$report.msixPackageIdentityVerified) {
        throw ("Packaged native smoke failed: " + $report.error)
    }
    $startupKey = Get-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
    $startupAfter = if ($startupKey) { $startupKey.GetValue('ByteDesktopCompanion') } else { $null }
    if ($startupBefore -ne $startupAfter) { throw 'Packaged startup changed the portable login registry entry.' }
    Write-Output ("MSIX native smoke: " + $report.checks.Count + ' checks passed with real package identity.')
}
finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id }
    try {
        $installed = Get-AppxPackage -Name Byte.LocalTest
        if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName }
        if (Get-AppxPackage -Name Byte.LocalTest) { throw 'Temporary MSIX package could not be removed.' }
    }
    finally {
        if ($machineTrusted) { Set-MachineTrust 'Remove' }
        if ($trusted) { Remove-Item -LiteralPath ("Cert:\CurrentUser\TrustedPeople\" + $trusted.Thumbprint) }
        if ($cert) { Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $cert.Thumbprint) }
        foreach ($path in @($signed, $cer)) {
            if (Test-Path $path) { Remove-Item -LiteralPath $path }
        }
    }
}
