param([Parameter(Mandatory=$true)][string]$GamePath, [ValidateSet('All','Timeline','Advanced')][string]$Target='All')
$ErrorActionPreference='Stop'
$compiler='C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
$references=@('BepInEx\core\BepInEx.dll','BepInEx\core\0Harmony.dll','CharaStudio_Data\Managed\UnityEngine.dll','CharaStudio_Data\Managed\UnityEngine.UI.dll','CharaStudio_Data\Managed\Assembly-CSharp.dll','CharaStudio_Data\Managed\Assembly-CSharp-firstpass.dll') | ForEach-Object { Join-Path $GamePath $_ }
foreach($path in $references) { if(!(Test-Path -LiteralPath $path)) {throw "Missing local reference: $path"} }
foreach($part in @('Timeline','Advanced')) {
    if($Target -ne 'All' -and $Target -ne $part) {continue}
    $project=Join-Path $PSScriptRoot $part
    $output=Join-Path $project 'bin'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $name=if($part -eq 'Timeline'){'GIN Timeline life hack.dll'}else{'GIN Advance mode life hack.dll'}
    $argsList=@('/nologo','/target:library','/optimize+','/debug:pdbonly',('/out:'+(Join-Path $output $name)),'/reference:System.Core.dll','/reference:System.Xml.dll')
    $argsList+=$references | ForEach-Object {'/reference:'+$_}
    if($part -eq 'Timeline') { $argsList+='/reference:'+(Join-Path $GamePath 'BepInEx\plugins\Timeline.dll') }
    $argsList+=Get-ChildItem -LiteralPath $project -Filter '*.cs' -File -Recurse | Where-Object {$_.FullName -notmatch '[\\/]bin[\\/]'} | ForEach-Object FullName
    & $compiler $argsList
    if($LASTEXITCODE -ne 0) {throw "$part compilation failed"}
    Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256
}

