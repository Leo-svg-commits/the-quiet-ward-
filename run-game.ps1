param(
    [ValidateRange(1, 65535)]
    [int]$Port = 8000,
    [switch]$LocalOnly
)

$ErrorActionPreference = "Stop"

if (-not $LocalOnly) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdministrator) {
        Write-Host "Requesting administrator permission so other devices on your local network can join."
        $scriptPath = '"' + $PSCommandPath + '"'
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File $scriptPath -Port $Port"
        try {
            $elevated = Start-Process -FilePath (Get-Process -Id $PID).Path `
                -ArgumentList $arguments -Verb RunAs -Wait -PassThru
            exit $elevated.ExitCode
        }
        catch {
            Write-Error "LAN hosting was not started. Accept the administrator prompt, or run .\run-game.ps1 -LocalOnly for this computer only."
            exit 1
        }
    }
}

Add-Type -Path (Join-Path $PSScriptRoot "QuietWardServer.cs") -ReferencedAssemblies "System.Web.Extensions.dll"
$server = New-Object QuietWardServer($PSScriptRoot, $Port, (-not $LocalOnly))

try {
    $server.Start()
    while ($true) {
        Start-Sleep -Seconds 1
    }
}
finally {
    $server.Dispose()
}
