param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "SnapText"
$dist = Join-Path $root "dist"
$tools = Join-Path $root "tools"
$packages = Join-Path $tools "packages"
$csc = Join-Path $packages "microsoft.net.compilers.toolset.4.8.0\tasks\net472\csc.exe"
$netRef = Join-Path $packages "microsoft.netcore.app.ref.8.0.6\ref\net8.0"
$desktopRef = Join-Path $packages "microsoft.windowsdesktop.app.ref.8.0.6\ref\net8.0"
$sdkRef = Join-Path $packages "microsoft.windows.sdk.net.ref.10.0.19041.86\lib\net8.0"
$hostModel = Join-Path $packages "microsoft.net.hostmodel.3.1.16\lib\netstandard2.0\Microsoft.NET.HostModel.dll"
$appHostTemplate = Join-Path $packages "microsoft.netcore.app.host.win-x64.8.0.6\runtimes\win-x64\native\apphost.exe"
$ocrBin = Join-Path $packages "ocr"
$ocrManaged = @(
    "RapidOcrNet.dll",
    "SkiaSharp.dll",
    "Clipper2Lib.dll",
    "Microsoft.ML.OnnxRuntime.dll",
    "System.Numerics.Tensors.dll"
)
$ocrNative = @(
    "onnxruntime.dll",
    "onnxruntime_providers_shared.dll",
    "libSkiaSharp.dll"
)
$ocrModels = Join-Path $project "models\ppocrv5"

foreach ($required in @($csc, $netRef, $desktopRef, $sdkRef, $hostModel, $appHostTemplate, $ocrBin, $ocrModels)) {
    if (-not (Test-Path $required)) {
        throw "缺少构建组件：$required"
    }
}
foreach ($file in $ocrManaged + $ocrNative) {
    if (-not (Test-Path (Join-Path $ocrBin $file))) {
        throw "缺少 OCR 组件：$file"
    }
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Get-ChildItem $dist -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

$sources = Get-ChildItem $project -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|tools)\\' } |
    ForEach-Object { $_.FullName }

$references = @()
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($dir in @($netRef, $desktopRef)) {
    foreach ($file in Get-ChildItem $dir -Filter *.dll) {
        if ($seen.Add($file.Name)) {
            $references += $file.FullName
        }
    }
}
foreach ($file in @("Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll")) {
    $path = Join-Path $sdkRef $file
    if ($seen.Add($file)) {
        $references += $path
    }
}
foreach ($file in $ocrManaged) {
    $path = Join-Path $ocrBin $file
    if ($seen.Add($file)) {
        $references += $path
    }
}

$icon = Join-Path $project "Assets\app.ico"
$manifest = Join-Path $project "app.manifest"
$outDll = Join-Path $dist "SnapText.App.dll"
$rsp = Join-Path $tools "build-app.rsp"

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("/nologo")
$lines.Add("/target:winexe")
$lines.Add("/platform:x64")
$lines.Add("/langversion:latest")
$lines.Add("/nullable:enable")
$lines.Add("/optimize+")
$lines.Add("/deterministic+")
$lines.Add("/nostdlib+")
$lines.Add("/out:`"$outDll`"")
$lines.Add("/win32icon:`"$icon`"")
$lines.Add("/win32manifest:`"$manifest`"")
$lines.Add("/resource:`"$icon`",SnapText.Assets.app.ico")
foreach ($reference in $references) {
    $lines.Add("/r:`"$reference`"")
}
foreach ($source in $sources) {
    $lines.Add("`"$source`"")
}
$lines | Set-Content -LiteralPath $rsp -Encoding UTF8

Write-Host "编译 $($sources.Count) 个源文件..." -ForegroundColor Cyan
& $csc /noconfig "@$rsp"
if ($LASTEXITCODE -ne 0) {
    throw "C# 编译失败（退出码 $LASTEXITCODE）"
}

Copy-Item (Join-Path $sdkRef "Microsoft.Windows.SDK.NET.dll") $dist -Force
Copy-Item (Join-Path $sdkRef "WinRT.Runtime.dll") $dist -Force
foreach ($file in $ocrManaged + $ocrNative) {
    Copy-Item (Join-Path $ocrBin $file) $dist -Force
}
$modelOut = Join-Path $dist "models\ppocrv5"
New-Item -ItemType Directory -Force -Path $modelOut | Out-Null
Copy-Item (Join-Path $ocrModels "*") $modelOut -Force
$notice = Join-Path $project "NOTICE-OCR.txt"
if (Test-Path $notice) {
    Copy-Item $notice $dist -Force
}

$runtimeConfig = @'
{
  "runtimeOptions": {
    "tfm": "net8.0",
    "framework": {
      "name": "Microsoft.WindowsDesktop.App",
      "version": "8.0.0"
    },
    "configProperties": {
      "System.GC.Server": false
    }
  }
}
'@
Set-Content -LiteralPath (Join-Path $dist "SnapText.App.runtimeconfig.json") -Value $runtimeConfig -Encoding UTF8

Write-Host "生成应用宿主..." -ForegroundColor Cyan
Add-Type -Path $hostModel
$outExe = Join-Path $dist "SnapText.exe"
[Microsoft.NET.HostModel.AppHost.HostWriter]::CreateAppHost(
    $appHostTemplate,
    $outExe,
    $outDll,
    $true,
    $outDll)

# The apphost resolves the embedded path relative to the current working directory.
# Embed the absolute app path so launching dist\SnapText.exe works from any folder.
$exeText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($outExe))
if (-not $exeText.Contains($outDll)) {
    throw "应用宿主路径写入失败"
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
Get-ChildItem $dist | Select-Object Name,Length | Format-Table -AutoSize
