#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Development environment stop script for Silo
.DESCRIPTION
    This script stops the development environment by:
    - Stopping the Silo API server
    - Stopping and optionally removing Docker containers
    - Optionally cleaning up Docker volumes
.PARAMETER Clean
    If specified, removes all Docker containers and volumes for a complete cleanup
#>

param(
    [switch]$Clean
)

Write-Host "🛑 Stopping Silo Development Environment" -ForegroundColor Yellow

# Function to stop Silo API
function Stop-SiloApi {
    Write-Host "🛑 Stopping Silo API..." -ForegroundColor Yellow
    
    # Stop any running Silo.Api processes
    $processes = Get-Process -Name "Silo.Api" -ErrorAction SilentlyContinue
    if ($processes) {
        $processes | Stop-Process -Force
        Write-Host "  ✅ Stopped Silo API processes" -ForegroundColor Green
        Start-Sleep 2
    }
    
    # Also try to kill any process using port 5289
    $portProcess = netstat -ano | Select-String ":5289" | Select-Object -First 1
    if ($portProcess) {
        $processId = ($portProcess.ToString().Split() | Where-Object {$_ -match '^\d+$'})[-1]
        if ($processId) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
            Write-Host "  ✅ Stopped process using port 5289" -ForegroundColor Green
        }
    }
    else {
        Write-Host "  ℹ️  No Silo API processes found" -ForegroundColor Blue
    }
}

# Function to stop Docker services
function Stop-DockerServices {
    Write-Host "🐳 Stopping Docker services..." -ForegroundColor Yellow
    
    # Change to project root
    Push-Location (Split-Path $PSScriptRoot)
    
    try {
        if ($Clean) {
            Write-Host "  🧹 Stopping and removing containers..." -ForegroundColor Yellow
            docker-compose -f docker-compose.dev.yml down -v
            
            # Remove any orphaned containers
            $containers = @("silo-mongodb", "silo-minio", "silo-opensearch")
            foreach ($container in $containers) {
                $exists = docker ps -aq --filter "name=$container"
                if ($exists) {
                    docker rm -f $container | Out-Null
                }
            }
            
            Write-Host "  🧹 Cleaning Docker volumes..." -ForegroundColor Yellow
            docker volume prune -f | Out-Null
            
            Write-Host "  ✅ Docker services stopped and cleaned" -ForegroundColor Green
        }
        else {
            Write-Host "  🛑 Stopping containers..." -ForegroundColor Yellow
            docker-compose -f docker-compose.dev.yml stop
            Write-Host "  ✅ Docker services stopped" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "  ⚠️  Some Docker operations may have failed" -ForegroundColor Yellow
    }
    finally {
        Pop-Location
    }
}

# Function to show status
function Show-Status {
    Write-Host ""
    Write-Host "📊 Current Status:" -ForegroundColor Blue
    
    # Check API process
    $apiProcess = Get-Process -Name "Silo.Api" -ErrorAction SilentlyContinue
    if ($apiProcess) {
        Write-Host "  🔴 Silo API: Still running" -ForegroundColor Red
    }
    else {
        Write-Host "  ✅ Silo API: Stopped" -ForegroundColor Green
    }
    
    # Check Docker containers
    $containers = @("silo-mongodb", "silo-minio", "silo-opensearch")
    foreach ($container in $containers) {
        $running = docker ps -q --filter "name=$container" 2>$null
        if ($running) {
            Write-Host "  🔴 ${container}: Running" -ForegroundColor Red
        }
        else {
            Write-Host "  ✅ ${container}: Stopped" -ForegroundColor Green
        }
    }
    
    Write-Host ""
    if ($Clean) {
        Write-Host "💚 Development environment completely cleaned!" -ForegroundColor Green
    }
    else {
        Write-Host "💛 Development environment stopped (containers preserved)" -ForegroundColor Yellow
        Write-Host "   Use 'dev-stop.ps1 -Clean' for complete cleanup" -ForegroundColor Blue
    }
}

# Main execution
try {
    Stop-SiloApi
    Stop-DockerServices
    Show-Status
}
catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}