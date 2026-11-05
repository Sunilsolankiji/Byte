param(
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$DisplayName = 'Byte',
    [string]$MakeAppx,
    [switch]$Store,
    [switch]$TestPackage
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if ($TestPackage) {
        if ($Store) { throw 'Choose either -TestPackage or -Store.' }
        if ($IdentityName -or $Publisher -or $PublisherDisplayName) {
            throw 'Do not combine -TestPackage with Store identity values.'
        }
        $IdentityName = 'Byte.LocalTest'
        $Publisher = 'CN=Byte Local Test'
        $PublisherDisplayName = 'Byte local packaging test'
        $DisplayName = 'Byte MSIX Test'
    }
    elseif ($Store) {
        if ($IdentityName -or $Publisher -or $PublisherDisplayName) { throw 'Do not override identity values with -Store.' }
        $storeIdentity = Get-Content '.\msix\StoreIdentity.json' -Raw | ConvertFrom-Json
        $IdentityName = $storeIdentity.IdentityName
        $Publisher = $storeIdentity.Publisher
        $PublisherDisplayName = $storeIdentity.PublisherDisplayName
        $DisplayName = $storeIdentity.DisplayName
    }
    elseif (!$IdentityName -or !$Publisher -or !$PublisherDisplayName) {
        throw 'Supply the exact Package/Identity/Name, Package/Identity/Publisher and publisher display name from Partner Center, or use -TestPackage for local validation only.'
    }
    if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$' -or $Publisher -notmatch '^CN=') {
        throw 'Invalid package identity. Copy the exact values from Partner Center.'
    }
    $exe = Join-Path $PSScriptRoot 'dist\Byte\Byte.exe'
    if (!(Test-Path $exe)) { throw 'Run .\build.ps1 first.' }
    [xml]$project = Get-Content '.\Byte.App\Byte.App.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$' -or (Get-Item $exe).VersionInfo.ProductVersion.Split('+')[0] -ne $version) {
        throw 'Published version differs from source. Run .\build.ps1 again.'
    }
    if (!$MakeAppx) {
        $command = Get-Command MakeAppx.exe -ErrorAction SilentlyContinue
        if ($command) { $MakeAppx = $command.Source }
        else {
            $kit = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
            $MakeAppx = Get-ChildItem $kit -Directory -ErrorAction SilentlyContinue |
                Where-Object Name -Match '^10\.0\.\d+\.0$' | Sort-Object { [version]$_.Name } -Descending |
                ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } |
                Where-Object { Test-Path $_ } | Select-Object -First 1
        }
    }
    if (!$MakeAppx -or !(Test-Path $MakeAppx)) { throw 'Install Windows SDK packaging tools or pass -MakeAppx.' }
    $staging = Join-Path $PSScriptRoot 'dist\msix-staging'
    if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse }
    New-Item -ItemType Directory "$staging\Assets" -Force | Out-Null
    Copy-Item $exe, '.\LICENSE', '.\README.md' $staging
    [xml]$manifest = Get-Content '.\msix\AppxManifest.xml' -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $ns.AddNamespace('p', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $ns.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
    $identity = $manifest.SelectSingleNode('/p:Package/p:Identity', $ns)
    $identity.SetAttribute('Name', $IdentityName)
    $identity.SetAttribute('Publisher', $Publisher)
    $identity.SetAttribute('Version', "$version.0")
    $manifest.SelectSingleNode('/p:Package/p:Properties/p:DisplayName', $ns).InnerText = $DisplayName
    $manifest.SelectSingleNode('/p:Package/p:Properties/p:PublisherDisplayName', $ns).InnerText = $PublisherDisplayName
    $manifest.SelectSingleNode('//uap:VisualElements', $ns).SetAttribute('DisplayName', $DisplayName)
    $manifest.Save("$staging\AppxManifest.xml")
    Add-Type -AssemblyName System.Drawing
    # Original terminal-robot icon, drawn offscreen; no desktop capture.
    foreach ($asset in @(
        @{ Name = 'StoreLogo.png'; Size = 50 },
        @{ Name = 'Square44x44Logo.png'; Size = 44 },
        @{ Name = 'Square150x150Logo.png'; Size = 150 }
    )) {
        $bitmap = New-Object System.Drawing.Bitmap($asset.Size, $asset.Size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $ink = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#132B36'))
        $mint = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#87F5D0'))
        $gold = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#FFC66B'))
        $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#132B36'), 3)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.ScaleTransform($asset.Size / 150.0, $asset.Size / 150.0)
            $graphics.DrawLine($pen, 75, 38, 88, 23)
            $graphics.FillEllipse($gold, 81, 11, 15, 15)
            $graphics.FillRectangle($ink, 28, 37, 94, 83)
            $graphics.FillRectangle($mint, 32, 41, 86, 75)
            $graphics.FillRectangle($ink, 39, 49, 72, 57)
            $graphics.FillRectangle($mint, 50, 63, 10, 19)
            $graphics.FillRectangle($mint, 91, 63, 10, 19)
            $graphics.FillRectangle($mint, 67, 91, 17, 4)
            $graphics.FillRectangle($ink, 39, 120, 25, 16)
            $graphics.FillRectangle($ink, 86, 120, 25, 16)
            $graphics.FillEllipse($mint, 15, 87, 13, 16)
            $graphics.FillEllipse($mint, 122, 87, 13, 16)
            $bitmap.Save((Join-Path "$staging\Assets" $asset.Name), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $pen.Dispose(); $ink.Dispose(); $mint.Dispose(); $gold.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $suffix = if ($TestPackage) { 'local-test' } else { 'store-upload' }
    $output = Join-Path $PSScriptRoot "dist\Byte-$version-win-x64-$suffix.msix"
    & $MakeAppx pack /d $staging /p $output /o
    if ($LASTEXITCODE -ne 0) { throw 'MSIX packing or manifest validation failed.' }
    Write-Output ("SHA256: " + (Get-FileHash $output -Algorithm SHA256).Hash)
    Write-Output "Unsigned MSIX: $output"
    if ($TestPackage) { Write-Output 'Local test identity only: do not submit this package to the Store or publish it as an installable download.' }
    else { Write-Output 'For Partner Center submission/certification only. Direct public installation still requires a trusted package signature.' }
}
finally { Pop-Location }
