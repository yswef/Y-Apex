# © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
if ($env:YAPEX_INSTALLER_SMOKE_TEST -ne '1') {
    throw 'Set YAPEX_INSTALLER_SMOKE_TEST=1 only in an isolated Windows CI runner or disposable VM.'
}

$installer = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer not found: $installer"
}

$serviceName = 'YApexService'
$installDirectory = Join-Path $env:ProgramFiles 'YSWEF\Y-Apex'
$dataDirectory = Join-Path $env:ProgramData 'YSWEF\Y-Apex'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    throw "Refusing to test over an existing $serviceName service."
}
if (Test-Path -LiteralPath $dataDirectory) {
    throw "Refusing to overwrite existing service data: $dataDirectory"
}
if (Test-Path -LiteralPath $installDirectory) {
    throw "Refusing to overwrite an existing installation directory: $installDirectory"
}

$serviceExe = Join-Path $installDirectory 'service\YApex.Service.exe'
$cliExe = Join-Path $installDirectory 'cli\YApex.Cli.exe'
$uninstaller = Join-Path $installDirectory 'unins000.exe'
$installed = $false
$emptyEmergencyVerified = $false
$failure = $null

try {
    $setupArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR=`"$installDirectory`""
    $setup = Start-Process -FilePath $installer -ArgumentList $setupArguments -Wait -PassThru
    if ($setup.ExitCode -ne 0) {
        throw "Installer exited with code $($setup.ExitCode)."
    }
    $installed = $true

    if (-not (Test-Path -LiteralPath $cliExe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $serviceExe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw 'The installed UI, service, CLI, or uninstaller payload is incomplete.'
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
            break
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $service -or $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        throw 'The installed Windows service did not reach Running state.'
    }

    # Headless CI has no Explorer session. Enroll its admin client SID, then restart the service so the pipe ACL is refreshed.
    $registrationOutput = & $cliExe register-pipe-user 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "The client SID could not be registered for the smoke test: $($registrationOutput -join ' ')"
    }
    $stopOutput = & $serviceExe --uninstall-service 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "The installed service could not be stopped for pipe-ACL refresh: $($stopOutput -join ' ')"
    }
    $startOutput = & $serviceExe --install-service 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "The installed service could not be restarted after pipe-ACL refresh: $($startOutput -join ' ')"
    }

    $statusOutput = & $cliExe status 2>&1
    $statusCode = $LASTEXITCODE
    $statusText = ($statusOutput | ForEach-Object { "$_" }) -join [Environment]::NewLine
    if ($statusCode -ne 0 -or $statusText -notmatch 'Service is running\. Y-Apex rules: 0\.') {
        throw "The installed CLI could not query a clean service over the named pipe. Exit=$statusCode; output=$statusText"
    }

    $emergencyOutput = "REMOVE`n" | & $cliExe emergency-remove 2>&1
    $emergencyCode = $LASTEXITCODE
    $emergencyText = ($emergencyOutput | ForEach-Object { "$_" }) -join [Environment]::NewLine
    if ($emergencyCode -ne 0 -or $emergencyText -notmatch 'Removed 0 Y-Apex rule\(s\)\.') {
        throw "The no-rule emergency-removal smoke test failed. Exit=$emergencyCode; output=$emergencyText"
    }
    $emptyEmergencyVerified = $true

    Write-Output 'Installer smoke test passed: install, service startup, restricted-pipe CLI status, empty emergency flow.'
}
catch {
    $failure = $_
}
finally {
    if ($installed -and (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        $remove = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -Wait -PassThru
        if ($remove.ExitCode -ne 0 -and $null -eq $failure) {
            $failure = "Silent uninstaller exited with code $($remove.ExitCode)."
        }
    }
    elseif (Test-Path -LiteralPath $serviceExe -PathType Leaf) {
        $removeServiceOutput = & $serviceExe --uninstall-service 2>&1
        if ($LASTEXITCODE -ne 0 -and $null -eq $failure) {
            $failure = "Service cleanup failed: $($removeServiceOutput -join ' ')"
        }
    }
}

$remainingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $remainingService -and $null -eq $failure) {
    $failure = "The $serviceName service remains after uninstall."
}
if ((Test-Path -LiteralPath $installDirectory) -and $null -eq $failure) {
    $failure = "The install directory remains after uninstall: $installDirectory"
}
if ($emptyEmergencyVerified -and $null -eq $remainingService -and -not (Test-Path -LiteralPath $installDirectory) -and (Test-Path -LiteralPath $dataDirectory)) {
    Remove-Item -LiteralPath $dataDirectory -Recurse -Force
}
if ($null -ne $failure) {
    throw $failure
}
