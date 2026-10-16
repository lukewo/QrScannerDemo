<#
  Dev helper: lists public type and method names in binding DLLs by reading ECMA-335
  metadata (System.Reflection.Metadata), which works without loading Mono.Android.
  Usage: tools\list-types.ps1 -Dll <path> [-TypeFilter regex] [-Members]
#>
param([Parameter(Mandatory)][string]$Dll, [string]$TypeFilter = '.', [switch]$Members)
$ErrorActionPreference = 'Stop'
$srm = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin" -Recurse -Filter System.Reflection.Metadata.dll | Select-Object -First 1
$imm = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin" -Recurse -Filter System.Collections.Immutable.dll | Select-Object -First 1
Add-Type -Path $imm.FullName
Add-Type -Path $srm.FullName
$stream = [IO.File]::OpenRead($Dll)
try {
    $pe = New-Object System.Reflection.PortableExecutable.PEReader($stream)
    $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    foreach ($h in $md.TypeDefinitions) {
        $t = $md.GetTypeDefinition($h)
        if (($t.Attributes -band [Reflection.TypeAttributes]::VisibilityMask) -eq 0) { continue }
        $ns = $md.GetString($t.Namespace); $name = $md.GetString($t.Name)
        if (-not $ns) {
            $dt = $t.GetDeclaringType()
            if (-not $dt.IsNil) { $d = $md.GetTypeDefinition($dt); $ns = $md.GetString($d.Namespace); $name = $md.GetString($d.Name) + '+' + $name }
        }
        $full = "$ns.$name"
        if ($full -notmatch $TypeFilter) { continue }
        Write-Output $full
        if ($Members) {
            $names = foreach ($mh in $t.GetMethods()) {
                $m = $md.GetMethodDefinition($mh)
                if (($m.Attributes -band [Reflection.MethodAttributes]::MemberAccessMask) -ne [Reflection.MethodAttributes]::Public) { continue }
                $md.GetString($m.Name)
            }
            $names | Sort-Object -Unique | ForEach-Object { Write-Output "    $_" }
        }
    }
} finally { $stream.Dispose() }
