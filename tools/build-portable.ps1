# © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "out\portable\$Runtime"))
$allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'out\portable'))
if (-not $outputRoot.StartsWith($allowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Portable output path escaped out\portable.'
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$projects = @(
    @{ Name = 'ui'; File = 'src\YApex.UI\YApex.UI.csproj' },
    @{ Name = 'service'; File = 'src\YApex.Service\YApex.Service.csproj' },
    @{ Name = 'cli'; File = 'src\YApex.Cli\YApex.Cli.csproj' }
)
foreach ($project in $projects) {
    $projectPath = Join-Path $repositoryRoot $project.File
    $publishPath = Join-Path $outputRoot $project.Name
    dotnet publish $projectPath -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $publishPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($project.Name) with exit code $LASTEXITCODE."
    }
}

$readme = @'
Y-Apex portable package

From an elevated session in the signed-in desktop, register its SID with YApex.Cli\YApex.Cli.exe register-pipe-user --interactive.
Then run YApex.Service\YApex.Service.exe --install-service from that elevated PowerShell session inside a Windows VM, and run YApex.UI\YApex.UI.exe as the normal user.
Use YApex.Cli\YApex.Cli.exe emergency-remove to review and remove Y-Apex rules only.
Before uninstalling, run YApex.Service\YApex.Service.exe --uninstall-service. Remove rules separately with --remove-owned-rules-confirmed only after stopping the service and deciding you want every Y-Apex-owned rule removed.
Test firewall changes only in a VM with a restore snapshot. See docs/build-local.md.
'@
[System.IO.File]::WriteAllText((Join-Path $outputRoot 'README.txt'), $readme, [System.Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'generate-sha256sums.ps1') -Target $outputRoot
