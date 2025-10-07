#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Development environment startup script for Silo
.DESCRIPTION
    This script manages the development environment by:
    - Stopping and removing existing Docker containers if running
    - Starting fresh Docker services (MongoDB, MinIO, OpenSearch)
    - Building and starting the Silo API server
.PARAMETER Clean
    If specified, removes all Docker volumes for a complete reset
#>

param(
    [switch]$Clean
)

Write-Host "🚀 Starting Silo Development Environment" -ForegroundColor Green

# Function to check if Docker is running
function Test-DockerRunning {
    try {
        docker version | Out-Null
        return $true
    }
    catch {
        Write-Host "❌ Docker is not running. Please start Docker Desktop first." -ForegroundColor Red
        exit 1
    }
}

# Function to stop and remove existing containers
function Stop-ExistingContainers {
    Write-Host "🛑 Stopping existing Docker containers..." -ForegroundColor Yellow
    
    # Stop containers if they exist
    $containers = @("silo-mongodb", "silo-minio", "silo-opensearch")
    foreach ($container in $containers) {
        $running = docker ps -q --filter "name=$container"
        if ($running) {
            Write-Host "  Stopping $container..." -ForegroundColor Yellow
            docker stop $container | Out-Null
        }
        
        $exists = docker ps -aq --filter "name=$container"
        if ($exists) {
            Write-Host "  Removing $container..." -ForegroundColor Yellow
            docker rm $container | Out-Null
        }
    }
    
    if ($Clean) {
        Write-Host "🧹 Cleaning Docker volumes..." -ForegroundColor Yellow
        docker volume prune -f | Out-Null
    }
}

# Function to stop Silo API if running
function Stop-SiloApi {
    Write-Host "🛑 Stopping Silo API if running..." -ForegroundColor Yellow
    
    # Stop any running Silo.Api processes
    $processes = Get-Process -Name "Silo.Api" -ErrorAction SilentlyContinue
    if ($processes) {
        $processes | Stop-Process -Force
        Write-Host "  Stopped existing Silo API processes" -ForegroundColor Yellow
        Start-Sleep 2
    }
    
    # Also try to kill any process using port 5289
    $portProcess = netstat -ano | Select-String ":5289" | Select-Object -First 1
    if ($portProcess) {
        $processId = ($portProcess.ToString().Split() | Where-Object {$_ -match '^\d+$'})[-1]
        if ($processId) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
            Write-Host "  Stopped process using port 5289" -ForegroundColor Yellow
        }
    }
}

# Function to start Docker services
function Start-DockerServices {
    Write-Host "🐳 Starting Docker services..." -ForegroundColor Blue
    
    # Change to project root
    Push-Location $PSScriptRoot
    
    try {
        # Start Docker Compose services
        docker-compose -f docker-compose.dev.yml up -d
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Docker services started successfully" -ForegroundColor Green
            
            # Wait for services to be ready
            Write-Host "⏳ Waiting for services to be ready..." -ForegroundColor Blue
            Start-Sleep 10
            
            # Check service health
            Write-Host "🔍 Checking service health..." -ForegroundColor Blue
            $services = @(
                @{Name="MongoDB"; Command="docker exec silo-mongodb mongosh --eval 'db.runCommand({ping: 1})' --quiet"},
                @{Name="MinIO"; Command="curl -s http://localhost:9000/minio/health/live"},
                @{Name="OpenSearch"; Command="curl -s http://localhost:9200/_cluster/health"}
            )
            
            foreach ($service in $services) {
                try {
                    Invoke-Expression $service.Command | Out-Null
                    Write-Host "  ✅ $($service.Name) is healthy" -ForegroundColor Green
                }
                catch {
                    Write-Host "  ⚠️  $($service.Name) may still be starting..." -ForegroundColor Yellow
                }
            }
        }
        else {
            Write-Host "❌ Failed to start Docker services" -ForegroundColor Red
            exit 1
        }
    }
    finally {
        Pop-Location
    }
}

# Function to build and start API
function Start-SiloApi {
    Write-Host "🔨 Building Silo API..." -ForegroundColor Blue
    
    # Change to API project directory
    Push-Location "$PSScriptRoot\src\Silo.Api"
    
    try {
        # Build the project
        dotnet build
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Build successful" -ForegroundColor Green
            
            # Create temp directory if it doesn't exist
            $tempDir = "C:\temp\silo-sync"
            if (!(Test-Path $tempDir)) {
                New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
                Write-Host "📁 Created sync directory: $tempDir" -ForegroundColor Blue
            }
            
            Write-Host "🚀 Starting Silo API server..." -ForegroundColor Blue
            Write-Host "📡 API will be available at: http://localhost:5289" -ForegroundColor Cyan
            Write-Host "🌐 Test page at: http://localhost:8081/test-page.html" -ForegroundColor Cyan
            Write-Host "📚 API docs at: http://localhost:5289/swagger" -ForegroundColor Cyan
            Write-Host ""
            Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Yellow
            Write-Host ""
            
            # Start the API server
            dotnet run --urls "http://localhost:5289"
        }
        else {
            Write-Host "❌ Build failed" -ForegroundColor Red
            exit 1
        }
    }
    finally {
        Pop-Location
    }
}

# Main execution
try {
    Test-DockerRunning
    Stop-SiloApi
    Stop-ExistingContainers
    Start-DockerServices
    Start-SiloApi
}
catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}