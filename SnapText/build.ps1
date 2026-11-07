param(
    [switch]$FrameworkDependent,
    [switch]$SkipClean
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot "SnapText.csproj"
$dist = Join-Path $root "dist"
$localDotnet = Join-Path $root ".dotnet\dotnet.exe"

$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }

if (-not (Test-Path $project)) {
    throw "找不到项目文件：$project"
}

$sdks = @(& $dotnet --list-sdks 2>$null)
if ($LASTEXITCODE -ne 0 -or $sdks.Count -eq 0) {
    Write-Host "未找到 .NET SDK，改用 csc 工具链构建..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "build-csc.ps1")
    exit $LASTEXITCODE
}

if (-not $SkipClean -and (Test-Path $dist)) {
    Get-ChildItem $dist -Force | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$arguments = @(
    "publish",
    $project,
    "-c", "Release",
    "-r", "win-x64",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:SatelliteResourceLanguages=zh-Hans",
    "-o", $dist
)

if ($FrameworkDependent) {
    $arguments += @("--self-contained", "false")
} else {
    $arguments += @("--self-contained", "true")
}

Write-Host "正在使用 $dotnet 构建..." -ForegroundColor Cyan
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "构建失败（退出码 $LASTEXITCODE）"
}

$exe = Join-Path $dist "SnapText.exe"
if (-not (Test-Path $exe)) {
    throw "构建完成，但未找到 $exe"
}

Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $dist "README.md") -Force
foreach ($folder in @("docs", "samples")) {
    $source = Join-Path $root $folder
    if (Test-Path $source) {
        Copy-Item -LiteralPath $source -Destination $dist -Recurse -Force
    }
}
Write-Host ""
Write-Host "构建完成：" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "  大小：$([Math]::Round((Get-Item $exe).Length / 1MB, 1)) MB"
