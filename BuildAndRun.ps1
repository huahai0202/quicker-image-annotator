param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sources = @(Get-ChildItem -LiteralPath $root -Filter "*.cs" | Sort-Object Name)
$exe = Join-Path $root "AnnotatorApp.exe"
$icon = Join-Path $root "AppIcon.ico"

$candidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)

$csc = $null
foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
        $csc = $candidate
        break
    }
}

if ($null -eq $csc) {
    throw "Cannot find csc.exe from .NET Framework."
}

$needsBuild = -not (Test-Path -LiteralPath $exe)
if (-not $needsBuild) {
    $exeWriteTime = (Get-Item -LiteralPath $exe).LastWriteTimeUtc
    foreach ($source in $sources) {
        if ($source.LastWriteTimeUtc -gt $exeWriteTime) {
            $needsBuild = $true
            break
        }
    }
}
if (-not $needsBuild -and (Test-Path -LiteralPath $icon)) {
    $needsBuild = (Get-Item -LiteralPath $icon).LastWriteTimeUtc -gt (Get-Item -LiteralPath $exe).LastWriteTimeUtc
}

if ($needsBuild) {
    $compileArgs = @(
        "/nologo",
        "/target:winexe",
        "/optimize+",
        "/out:$exe",
        "/r:System.Windows.Forms.dll",
        "/r:System.Drawing.dll"
    )
    if (Test-Path -LiteralPath $icon) {
        $compileArgs += "/win32icon:$icon"
    }
    $compileArgs += $sources.FullName
    & $csc @compileArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

& $exe @RemainingArgs
$code = $LASTEXITCODE
if (($RemainingArgs -contains "-SelfTest") -and $code -eq 0) {
    Write-Output "OK"
}
exit $code
