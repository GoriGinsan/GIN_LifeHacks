param(
    [Parameter(Mandatory = $true)]
    [string] $GamePath,

    [ValidateSet('All', 'Timeline', 'Advanced')]
    [string] $Target = 'All'
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v3.5\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v2.0.50727'

$referenceFiles = @(
    'BepInEx\core\BepInEx.dll'
    'BepInEx\core\0Harmony.dll'
    'CharaStudio_Data\Managed\UnityEngine.dll'
    'CharaStudio_Data\Managed\UnityEngine.UI.dll'
    'CharaStudio_Data\Managed\Assembly-CSharp.dll'
    'CharaStudio_Data\Managed\Assembly-CSharp-firstpass.dll'
)

$references = $referenceFiles | ForEach-Object { Join-Path $GamePath $_ }
foreach ($path in $references) {
    if (!(Test-Path -LiteralPath $path)) {
        throw "Missing local reference: $path"
    }
}

foreach ($part in @('Timeline', 'Advanced')) {
    if ($Target -ne 'All' -and $Target -ne $part) {
        continue
    }

    $project = Join-Path $PSScriptRoot $part
    $output = Join-Path $project 'bin'
    New-Item -ItemType Directory -Force -Path $output | Out-Null

    $filename = if ($part -eq 'Timeline') {
        'GIN Timeline life hack.dll'
    } else {
        'GIN Advance mode life hack.dll'
    }

    $arguments = @(
        '/noconfig'
        '/nologo'
        '/target:library'
        '/optimize+'
        '/debug-'
        ('/out:' + (Join-Path $output $filename))
        ('/reference:' + (Join-Path $framework 'System.dll'))
        ('/reference:' + (Join-Path $framework 'System.Xml.dll'))
        ('/reference:' + (Join-Path $GamePath 'CharaStudio_Data\Managed\System.Core.dll'))
    )
    $arguments += $references | ForEach-Object { '/reference:' + $_ }

    if ($part -eq 'Timeline') {
        $arguments += '/reference:' + (Join-Path $GamePath 'BepInEx\plugins\Timeline.dll')
    }

    $arguments += Get-ChildItem -LiteralPath $project -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]' } |
        ForEach-Object FullName

    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$part compilation failed"
    }

    Get-FileHash -LiteralPath (Join-Path $output $filename) -Algorithm SHA256
}

