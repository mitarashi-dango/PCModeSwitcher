[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $projectRoot "PCModeSwitcher.csproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $projectRoot "artifacts"
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
    $parentPrefix = $fullParent + [System.IO.Path]::DirectorySeparatorChar

    if (-not $fullPath.StartsWith($parentPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "安全のため、出力ルート外のパスは操作できません: $fullPath"
    }

    return $fullPath
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$project.Project.PropertyGroup.Version

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "PCModeSwitcher.csproj からバージョンを取得できませんでした。"
}

$releaseNotesPath = Join-Path $projectRoot "docs\RELEASE_NOTES_$version.md"
if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) {
    throw "バージョン $version のリリースノートがありません: $releaseNotesPath"
}

$packageName = "PCModeSwitcher-v$version-$Runtime"
$publishRoot = Assert-ChildPath -Path (Join-Path $OutputRoot "publish") -Parent $OutputRoot
$publishDirectory = Assert-ChildPath -Path (Join-Path $publishRoot $packageName) -Parent $OutputRoot
$packageDirectory = Assert-ChildPath -Path (Join-Path $OutputRoot $packageName) -Parent $OutputRoot
$zipPath = Assert-ChildPath -Path (Join-Path $OutputRoot "$packageName.zip") -Parent $OutputRoot
$checksumPath = Assert-ChildPath -Path (Join-Path $OutputRoot "SHA256SUMS.txt") -Parent $OutputRoot

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

foreach ($directory in @($publishDirectory, $packageDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}

foreach ($file in @($zipPath, $checksumPath)) {
    if (Test-Path -LiteralPath $file) {
        Remove-Item -LiteralPath $file -Force
    }
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishProfile=win-x64

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish に失敗しました。終了コード: $LASTEXITCODE"
}

$executablePath = Join-Path $publishDirectory "PCModeSwitcher.exe"

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "配布用実行ファイルが生成されませんでした: $executablePath"
}

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
Copy-Item -LiteralPath $executablePath -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot "docs\DISTRIBUTION_README.md") `
    -Destination (Join-Path $packageDirectory "README.md")
Copy-Item -LiteralPath $releaseNotesPath `
    -Destination (Join-Path $packageDirectory "RELEASE_NOTES.md")
Copy-Item -LiteralPath (Join-Path $projectRoot "Assets\FluentEmojiHighContrast\THIRD-PARTY-NOTICE.txt") `
    -Destination (Join-Path $packageDirectory "THIRD-PARTY-NOTICE.txt")

Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash *$([System.IO.Path]::GetFileName($zipPath))" -Encoding ascii

$zipInfo = Get-Item -LiteralPath $zipPath

Write-Host "配布パッケージを作成しました。"
Write-Host "ZIP: $($zipInfo.FullName)"
Write-Host "サイズ: $([Math]::Round($zipInfo.Length / 1MB, 2)) MB"
Write-Host "SHA-256: $hash"
