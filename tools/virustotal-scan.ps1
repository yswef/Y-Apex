# © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:VIRUSTOTAL_API_KEY)) {
    Write-Output 'VirusTotal scan skipped because no API key is configured.'
    exit 0
}

$executables = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\src') -Recurse -File -Filter '*.exe' |
    Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]' }
if ($executables.Count -eq 0) {
    Write-Output 'VirusTotal scan skipped because no Release executables were built.'
    exit 0
}

$headers = @{ 'x-apikey' = $env:VIRUSTOTAL_API_KEY }
foreach ($executable in $executables) {
    if ($executable.Length -gt 32MB) {
        throw "The file is larger than the direct upload limit: $($executable.Name)"
    }

    Write-Output "Submitting $($executable.Name) to VirusTotal for analysis."
    $result = Invoke-RestMethod -Method Post `
        -Uri 'https://www.virustotal.com/api/v3/files' `
        -Headers $headers `
        -Form @{ file = $executable }
    if (-not $result.data.id) {
        throw "VirusTotal did not return an analysis identifier for $($executable.Name)."
    }
}
