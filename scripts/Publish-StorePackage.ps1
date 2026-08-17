[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern("^[A-Za-z0-9.-]+$")]
    [string]$PackageIdentityName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Publisher,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublisherDisplayName,

    [ValidatePattern("^\d+\.\d+\.\d+\.\d+$")]
    [string]$Version,

    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $projectRoot "PCModeSwitcher.csproj"
$templatePath = Join-Path $projectRoot "packaging\AppxManifest.template.xml"
$toolProjectPath = Join-Path $projectRoot "packaging\PCModeSwitcher.StorePackaging.csproj"
$packageCache = Join-Path $projectRoot ".packages"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $projectRoot "artifacts\store"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $prefix = $fullParent + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "安全のため、出力ルート外のパスは操作できません: $fullPath"
    }

    return $fullPath
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $projectVersion = [version][string]$project.Project.PropertyGroup.Version
    $Version = "$($projectVersion.Major).$($projectVersion.Minor).$($projectVersion.Build).0"
}

$buildRoot = Assert-ChildPath -Path (Join-Path $OutputRoot "build") -Parent $OutputRoot
$publishDirectory = Assert-ChildPath -Path (Join-Path $buildRoot "publish") -Parent $OutputRoot
$packageDirectory = Assert-ChildPath -Path (Join-Path $buildRoot "package") -Parent $OutputRoot
$verifyDirectory = Assert-ChildPath -Path (Join-Path $buildRoot "verify") -Parent $OutputRoot
$msixPath = Assert-ChildPath -Path (Join-Path $OutputRoot "PCModeSwitcher-$Version-x64.msix") -Parent $OutputRoot

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}
if (Test-Path -LiteralPath $msixPath) {
    Remove-Item -LiteralPath $msixPath -Force
}
New-Item -ItemType Directory -Path $publishDirectory, $packageDirectory -Force | Out-Null

dotnet restore $toolProjectPath --packages $packageCache
if ($LASTEXITCODE -ne 0) {
    throw "MSIX作成ツールの復元に失敗しました。終了コード: $LASTEXITCODE"
}

$makeAppx = Get-ChildItem -LiteralPath $packageCache -Filter makeappx.exe -Recurse |
    Where-Object { $_.FullName -match "[\\/]x64[\\/]makeappx\.exe$" } |
    Select-Object -First 1
if ($null -eq $makeAppx) {
    throw "makeappx.exeを見つけられませんでした。"
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) {
    throw "Store用アプリの発行に失敗しました。終了コード: $LASTEXITCODE"
}

Get-ChildItem -LiteralPath $publishDirectory -Force |
    Copy-Item -Destination $packageDirectory -Recurse -Force

$assetsDirectory = Join-Path $packageDirectory "Assets"
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing

function Write-SquarePng {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image]$Source,

        [Parameter(Mandatory)]
        [int]$Size,

        [Parameter(Mandatory)]
        [string]$Destination
    )

    $bitmap = New-Object System.Drawing.Bitmap(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bitmap.SetResolution(96, 96)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($Source, 0, 0, $Size, $Size)
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

$sourceIcon = [System.Drawing.Image]::FromFile((Join-Path $projectRoot "Assets\AppIcon.png"))
try {
    Write-SquarePng -Source $sourceIcon -Size 50 -Destination (Join-Path $assetsDirectory "StoreLogo.png")
    Write-SquarePng -Source $sourceIcon -Size 44 -Destination (Join-Path $assetsDirectory "Square44x44Logo.png")
    Write-SquarePng -Source $sourceIcon -Size 44 -Destination (Join-Path $assetsDirectory "Square44x44Logo.targetsize-44_altform-unplated.png")
    Write-SquarePng -Source $sourceIcon -Size 150 -Destination (Join-Path $assetsDirectory "Square150x150Logo.png")
}
finally {
    $sourceIcon.Dispose()
}

function Escape-XmlText([string]$Value) {
    return [System.Security.SecurityElement]::Escape($Value)
}

$manifest = Get-Content -LiteralPath $templatePath -Raw
$manifest = $manifest.Replace("{{PACKAGE_IDENTITY_NAME}}", (Escape-XmlText $PackageIdentityName))
$manifest = $manifest.Replace("{{PUBLISHER}}", (Escape-XmlText $Publisher))
$manifest = $manifest.Replace("{{PUBLISHER_DISPLAY_NAME}}", (Escape-XmlText $PublisherDisplayName))
$manifest = $manifest.Replace("{{VERSION}}", $Version)
Set-Content -LiteralPath (Join-Path $packageDirectory "AppxManifest.xml") -Value $manifest -Encoding utf8NoBOM

& $makeAppx.FullName pack /d $packageDirectory /p $msixPath /o /l
if ($LASTEXITCODE -ne 0) {
    throw "MSIXパッケージの作成に失敗しました。終了コード: $LASTEXITCODE"
}

& $makeAppx.FullName unpack /p $msixPath /d $verifyDirectory /o
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $verifyDirectory "AppxManifest.xml"))) {
    throw "作成したMSIXパッケージの検証に失敗しました。"
}

$packageInfo = Get-Item -LiteralPath $msixPath
$hash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Store提出用MSIXを作成しました。"
Write-Host "MSIX: $($packageInfo.FullName)"
Write-Host "サイズ: $([Math]::Round($packageInfo.Length / 1MB, 2)) MB"
Write-Host "SHA-256: $hash"
