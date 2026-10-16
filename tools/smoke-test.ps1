<#
.SYNOPSIS
    Installs the Debug build on a device/emulator, grants camera permission, launches
    the app, and reports whether it stays alive plus any crash/scanner log lines.
    Writes a screenshot and a log excerpt next to -OutDir.
.EXAMPLE
    tools\smoke-test.ps1 -Serial emulator-5554
#>
param(
    [string]$Serial = 'emulator-5554',
    [string]$Package = 'com.lukewo.qrscannerdemo',
    [int]$WaitSeconds = 25,
    [string]$OutDir = $env:USERPROFILE
)

$adb = "${env:ProgramFiles(x86)}\Android\android-sdk\platform-tools\adb.exe"
$toolsDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path -Parent $toolsDir
$report = Join-Path $OutDir 'qr-smoke.txt'
$lines = New-Object System.Collections.Generic.List[string]

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolsDir 'build.ps1') `
    (Join-Path $root 'QrScannerDemo\QrScannerDemo.Android\QrScannerDemo.Android.csproj') `
    -Targets Install -Properties "AdbTarget=-s $Serial" | Out-Null
$lines.Add("install exit code: $LASTEXITCODE")

& $adb -s $Serial shell am force-stop $Package 2>&1 | Out-Null
& $adb -s $Serial shell pm grant $Package android.permission.CAMERA 2>&1 | Out-Null
& $adb -s $Serial logcat -c 2>&1 | Out-Null
& $adb -s $Serial shell monkey -p $Package -c android.intent.category.LAUNCHER 1 2>&1 | Out-Null
Start-Sleep -Seconds $WaitSeconds

$appPid = ("" + (& $adb -s $Serial shell pidof $Package 2>$null)).Trim()
$lines.Add("running after ${WaitSeconds}s: $([bool]$appPid)  pid: $appPid")

$log = & $adb -s $Serial logcat -d -v brief 2>$null
$hits = $log | Select-String 'FATAL EXCEPTION|AndroidRuntime: |AbstractMethodError|JNI DETECTED ERROR|UnsatisfiedLinkError|NoClassDefFoundError|NoSuchMethodError|Java\.Lang\.\w+Exception|CameraX|Camera2CameraImpl.*(Error|error)|barhopper|MLKit|BarcodeScanner'
$lines.Add("interesting log lines: $(@($hits).Count)")
$hits | Select-Object -First 40 | ForEach-Object { $lines.Add($_.Line) }

& $adb -s $Serial shell screencap -p /sdcard/qr-smoke.png 2>&1 | Out-Null
& $adb -s $Serial pull /sdcard/qr-smoke.png (Join-Path $OutDir 'qr-smoke.png') 2>&1 | Out-Null
& $adb -s $Serial shell rm /sdcard/qr-smoke.png 2>&1 | Out-Null
$lines.Add("screenshot: $(Join-Path $OutDir 'qr-smoke.png')")

$lines | Set-Content $report -Encoding utf8
$lines | ForEach-Object { Write-Output $_ }
