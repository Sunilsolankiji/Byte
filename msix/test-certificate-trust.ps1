param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Import', 'Remove')]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$Thumbprint,
    [string]$CertificatePath
)
$ErrorActionPreference = 'Stop'
$path = "Cert:\LocalMachine\TrustedPeople\$Thumbprint"
if ($Action -eq 'Import') {
    if (!$CertificatePath) { throw 'A temporary test certificate file is required.' }
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertificatePath)
    if ($cert.Thumbprint -ne $Thumbprint -or $cert.Subject -ne 'CN=Byte Local Test' -or
        $cert.NotAfter -gt (Get-Date).AddDays(2)) {
        throw 'Only the exact short-lived Byte local test certificate may be trusted.'
    }
    Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
}
elseif (Test-Path $path) {
    if ((Get-Item $path).Subject -ne 'CN=Byte Local Test') { throw 'Refusing to remove an unrelated certificate.' }
    Remove-Item -LiteralPath $path
}
