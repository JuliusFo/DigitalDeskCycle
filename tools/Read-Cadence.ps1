<#
.SYNOPSIS
    Taps the cadence firmware on the Pico over USB serial and prints the lines.

.DESCRIPTION
    A minimal capture for troubleshooting the sensor. Uses System.IO.Ports --
    the same class the application uses, so this doubles as a check of the .NET
    side.

    Important: Thonny (or any other terminal) must not hold the port open, or
    opening it fails with "access denied".

.PARAMETER Port
    COM port of the Pico. Without it, the only available port is used.

.PARAMETER LogFile
    Optional path; the lines are additionally written there.

.EXAMPLE
    .\Read-Cadence.ps1
    .\Read-Cadence.ps1 -Port COM5 -LogFile .\capture.csv
#>
[CmdletBinding()]
param(
    [string] $Port,
    [string] $LogFile
)

$ErrorActionPreference = 'Stop'

if (-not $Port) {
    $available = [System.IO.Ports.SerialPort]::GetPortNames()
    if ($available.Count -eq 0) {
        throw "No COM port found. Is the Pico plugged in and the firmware running?"
    }
    if ($available.Count -gt 1) {
        throw "Several COM ports found ($($available -join ', ')). Please pass -Port."
    }
    $Port = $available[0]
}

Write-Host "Reading $Port -- stop with Ctrl+C" -ForegroundColor Cyan

$sp = New-Object System.IO.Ports.SerialPort $Port, 115200, 'None', 8, 'One'
$sp.ReadTimeout = 1000
$sp.NewLine = "`n"
$sp.DtrEnable = $true   # MicroPython does not need it, some USB stacks do

try {
    $sp.Open()
    while ($true) {
        try {
            $line = $sp.ReadLine().Trim()
        }
        catch [System.TimeoutException] {
            continue    # no data this interval -- normal while standing still
        }

        if (-not $line) { continue }

        $color = 'Gray'
        if     ($line.StartsWith('PULSE')) { $color = 'Yellow' }
        elseif ($line.StartsWith('CAD'))   { $color = 'Green'  }

        Write-Host ("{0:HH:mm:ss}  {1}" -f (Get-Date), $line) -ForegroundColor $color

        if ($LogFile) { Add-Content -Path $LogFile -Value $line -Encoding utf8 }
    }
}
finally {
    if ($sp.IsOpen) { $sp.Close() }
    $sp.Dispose()
}
