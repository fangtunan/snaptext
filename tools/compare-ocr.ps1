param(
    [string]$Fixtures = (Join-Path $PSScriptRoot "..\tests\ocr-fixtures"),
    [string]$AppDll = (Join-Path $PSScriptRoot "..\dist\SnapText.App.dll")
)

$ErrorActionPreference = "Stop"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$appDll = (Resolve-Path $AppDll).Path

function Get-Cer([string]$Expected, [string]$Actual) {
    $a = ($Expected -replace '\s+', '').ToCharArray()
    $b = ($Actual -replace '\s+', '').ToCharArray()
    $prev = New-Object int[] ($b.Length + 1)
    $curr = New-Object int[] ($b.Length + 1)
    for ($j = 0; $j -le $b.Length; $j++) { $prev[$j] = $j }
    for ($i = 1; $i -le $a.Length; $i++) {
        $curr[0] = $i
        for ($j = 1; $j -le $b.Length; $j++) {
            $cost = if ($a[$i - 1] -eq $b[$j - 1]) { 0 } else { 1 }
            $curr[$j] = [Math]::Min([Math]::Min($curr[$j - 1] + 1, $prev[$j] + 1), $prev[$j - 1] + $cost)
        }
        $tmp = $prev; $prev = $curr; $curr = $tmp
    }
    if ($a.Length -eq 0) { return 0.0 }
    return [double]$prev[$b.Length] / $a.Length
}

$cases = @(
    @{ Name = "mixed"; Image = "mixed.png"; Expected = "mixed.expected.txt" },
    @{ Name = "english"; Image = "english.png"; Expected = "english.expected.txt" }
)

foreach ($mode in @("fast", "accurate")) {
    Write-Host ""
    Write-Host "=== $mode ===" -ForegroundColor Cyan
    $totalCer = 0.0
    foreach ($case in $cases) {
        $image = Join-Path $Fixtures $case.Image
        $expected = Get-Content (Join-Path $Fixtures $case.Expected) -Raw -Encoding UTF8
        $out = Join-Path $env:TEMP ("snaptext-cer-" + $case.Name + "-" + $mode + ".txt")
        Remove-Item $out, "$out.meta.txt" -ErrorAction SilentlyContinue
        $proc = Start-Process -FilePath $dotnet -ArgumentList @(
            $appDll, "--ocr", $image, $out, "auto", $mode
        ) -PassThru -Wait -NoNewWindow
        if ($proc.ExitCode -ne 0 -or -not (Test-Path $out)) {
            throw "OCR probe failed for $($case.Name) / $mode"
        }
        $actual = Get-Content $out -Raw -Encoding UTF8
        $cer = Get-Cer $expected $actual
        $totalCer += $cer
        Write-Host ("{0,-8} CER={1:P1}" -f $case.Name, $cer)
        Write-Host ($actual.Trim())
    }
    Write-Host ("average CER={0:P1}" -f ($totalCer / $cases.Count)) -ForegroundColor Yellow
}
