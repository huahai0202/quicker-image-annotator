param(
    [ValidateSet("anycpu", "x64", "x86")]
    [string]$Platform = "anycpu",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sources = @(Get-ChildItem -LiteralPath $root -Filter "*.cs" | Sort-Object Name)
$platformName = $Platform.ToLowerInvariant()
$exeName = if ($platformName -eq "anycpu") { "AnnotatorApp.exe" } else { "AnnotatorApp-$platformName.exe" }
$exe = Join-Path $root $exeName
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
if (-not $needsBuild) {
    $needsBuild = (Get-Item -LiteralPath $MyInvocation.MyCommand.Path).LastWriteTimeUtc -gt (Get-Item -LiteralPath $exe).LastWriteTimeUtc
}

if ($needsBuild) {
    $compileArgs = @(
        "/nologo",
        "/target:winexe",
        "/platform:$platformName",
        "/unsafe+",
        "/optimize+",
        "/codepage:65001",
        "/out:$exe",
        "/r:System.dll"
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

if ($RemainingArgs -contains "-SelfTest") {
    $process = Start-Process -FilePath $exe -ArgumentList $RemainingArgs -Wait -PassThru -WindowStyle Hidden
    $code = $process.ExitCode
    if ($code -eq 0) {
        Write-Output "OK"
    }
    exit $code
}

& $exe @RemainingArgs
$code = $LASTEXITCODE
exit $code
