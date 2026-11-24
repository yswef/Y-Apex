# © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
param(
    [string]$Target = (Join-Path $PSScriptRoot '..\out\portable')
)

$ErrorActionPreference = 'Stop'
$targetPath = [System.IO.Path]::GetFullPath($Target)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'out'))
$expectedPrefix = $expectedRoot + [System.IO.Path]::DirectorySeparatorChar
if (-not $targetPath.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'SHA-256 output must remain inside out.'
}
if (-not (Test-Path -LiteralPath $targetPath -PathType Container)) {
    throw "The target directory does not exist: $targetPath"
}

$sumFile = Join-Path $targetPath 'SHA256SUMS.txt'
$lines = Get-ChildItem -LiteralPath $targetPath -Recurse -File |
    Where-Object { $_.FullName -ne $sumFile } |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($targetPath, $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    }

[System.IO.File]::WriteAllLines($sumFile, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote $($lines.Count) SHA-256 entries to $sumFile"
