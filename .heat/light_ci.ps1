<# 
.SYNOPSIS
Builds & tests a .NET solution, captures logs in a temp folder,
sends them to StaalAI heatmap, then deletes the logs. Never crashes.

.PARAMETER WorkingDirectoryPath
Path to the repo/solution folder to build & test.

.EXAMPLE
.\build-and-heat.ps1 -WorkingDirectoryPath "C:\src\MySolution"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkingDirectoryPath
)

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

# Resolve and move to working directory
try {
    $WorkingDirectoryPath = (Resolve-Path -LiteralPath $WorkingDirectoryPath).Path
} catch {
    Write-Warning "Working directory not found: $WorkingDirectoryPath"
    # Create a fake path so the rest of the script still runs safely
    $WorkingDirectoryPath = (Get-Location).Path
}
Push-Location $WorkingDirectoryPath

# Create a unique temp folder for artifacts
$outDir = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ("StaalAI_Logs_" + [System.Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Small helper to run a command and guarantee a log file exists
function Invoke-Logged {
    param(
        [Parameter(Mandatory)]
        [ScriptBlock]$Command,
        [Parameter(Mandatory)]
        [string]$LogPath,
        [string]$DisplayName
    )
    $exitCode = 0
    try {
        Write-Host "Running: $DisplayName"
        # Ensure the log file exists even if the command fails before emitting anything
        if (-not (Test-Path -LiteralPath $LogPath)) { '' | Out-File -FilePath $LogPath -Encoding utf8 }

        & $Command *>&1 | Tee-Object -FilePath $LogPath | Out-Host
        $exitCode = $LASTEXITCODE
    } catch {
        $msg = "ERROR during ${DisplayName}: $($_.Exception.Message)"
        Write-Warning $msg
        $msg | Out-File -FilePath $LogPath -Append -Encoding utf8
        $exitCode = 1
    }
    return $exitCode
}


try {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $toolLog   = Join-Path $outDir "tool-update.log"
    $buildLog  = Join-Path $outDir "dotnet-build.log"
    $testLog   = Join-Path $outDir "dotnet-test.log"

    # 1) Try to update the tool; never crash
    try {
        Write-Host "Updating Solurum.StaalAi tool (global)..."
        dotnet tool update Solurum.StaalAi -g --add-source https://api.nuget.org/v3/index.json *>&1 |
            Tee-Object -FilePath $toolLog | Out-Host
    } catch {
        $msg = "Solurum.StaalAi tool update failed: $($_.Exception.Message)"
        Write-Warning $msg
        $msg | Out-File -FilePath $toolLog -Append -Encoding utf8
    }

    # 2) dotnet build (attempt even if tool update failed)
    $buildExit = Invoke-Logged -Command { dotnet build } -LogPath $buildLog -DisplayName 'dotnet build'

    # 3) dotnet test (attempt even if build failed)
    $testExit  = Invoke-Logged -Command { dotnet test }  -LogPath $testLog  -DisplayName 'dotnet test'

    # 4) Send logs to StaalAI regardless of success/failure
    $logs = @($buildLog, $testLog) | Where-Object { Test-Path -LiteralPath $_ }
    foreach ($log in $logs) {
        Write-Host "Adding heat for: $log"
        try {
            StaalAI add-heat -wd "$WorkingDirectoryPath" -hf "$log"
        } catch {
            Write-Warning "Failed to send heat for '$log': $($_.Exception.Message)"
        }
    }

    # Always succeed per request (do not crash/propagate failure)
    if ($buildExit -ne 0 -or $testExit -ne 0) {
        Write-Warning "Build/Test had non-zero exit codes (build=$buildExit, test=$testExit)"
    } else {
        Write-Host "Build and tests completed successfully."
    }
}
finally {
    # Clean up temp folder (best effort)
    if (Test-Path $outDir) {
        try {
            Remove-Item -LiteralPath $outDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "Cleaned up temporary log folder: $outDir"
        } catch {
            Write-Warning "Could not remove temporary log folder: $outDir"
        }
    }
    Pop-Location
    # Force successful exit
    $global:LASTEXITCODE = 0
    exit 0
}
