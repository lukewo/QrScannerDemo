<#
.SYNOPSIS
    Builds a project with Visual Studio's MSBuild and prints a compact error summary.
.EXAMPLE
    tools\build.ps1 LiveKitBindings\LiveKitBindings.csproj
    tools\build.ps1 LiveKitDemo\LiveKitDemo.Android\LiveKitDemo.Android.csproj -Targets "Restore;Build"
#>
param(
    [Parameter(Mandatory)] [string]$Project,
    [string]$Targets = 'Build',
    [string]$Configuration = 'Debug',
    [string[]]$Properties = @()
)

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -version '[17.0,18.0)' -products * -requires Component.Xamarin -property installationPath | Select-Object -First 1
if (-not $vs) { $vs = 'C:\Program Files\Microsoft Visual Studio\2022\Professional' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'

# JDK 17: what Xamarin.Android 13.2 expects.
$jdk = @(
    'C:\Program Files\Microsoft\jdk-17.0.16.8-hotspot',
    "${env:ProgramFiles(x86)}\Android\openjdk\jdk-17.0.14"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

$log = Join-Path $env:TEMP ("build-" + [IO.Path]::GetFileNameWithoutExtension($Project) + ".log")
$props = @("/p:Configuration=$Configuration", "/p:JavaSdkDirectory=$jdk") + ($Properties | ForEach-Object { "/p:$_" })

& $msbuild $Project "/t:$Targets" @props /restore /m /v:q /nologo "/flp:logfile=$log;verbosity=normal" | Out-Null
$code = $LASTEXITCODE

$lines = Get-Content $log
$errors = $lines | Where-Object { $_ -match ':\s+error\s+\w+' } |
    ForEach-Object { ($_ -replace '\s*\[[^\]]+\.csproj\]\s*$', '').Trim() } | Sort-Object -Unique
$summary = $lines | Where-Object { $_ -match '^\s+\d+ (Warning|Error)\(s\)' }

Write-Host "exit code: $code"
$summary | ForEach-Object { Write-Host $_.Trim() }
$errors | Select-Object -First 40 | ForEach-Object { Write-Host $_ }
Write-Host "log: $log"
exit $code
