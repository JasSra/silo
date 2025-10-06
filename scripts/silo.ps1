# PowerShell Development Helper Scripts for Silo

# Build the entire solution
function Build {
    dotnet restore
    dotnet build
}

# Run tests
function Test {
    dotnet test
}

# Start development environment
function Start-Dev {
    docker-compose -f docker-compose.dev.yml up -d
}

# Start development environment with full services (including ClamAV)
function Start-DevFull {
    docker-compose -f docker-compose.dev.yml --profile full up -d
}

# Stop development environment
function Stop-Dev {
    docker-compose -f docker-compose.dev.yml down
}

# View development logs
function Show-DevLogs {
    docker-compose -f docker-compose.dev.yml logs -f
}

# Start production environment
function Start-Prod {
    docker-compose -f docker-compose.prod.yml up -d
}

# Stop production environment  
function Stop-Prod {
    docker-compose -f docker-compose.prod.yml down
}

# Clean Docker volumes and rebuild
function Clean-Docker {
    docker-compose -f docker-compose.dev.yml down -v
    docker system prune -f
    docker volume prune -f
}

# Run the API locally
function Start-Api {
    Set-Location src/Silo.Api
    dotnet run
    Set-Location ../..
}

# Run the Agent locally
function Start-Agent {
    Set-Location src/Silo.Agent
    dotnet run
    Set-Location ../..
}

# Run the Backup Worker locally
function Start-BackupWorker {
    Set-Location src/Silo.BackupWorker
    dotnet run
    Set-Location ../..
}

# Package for deployment
function Package {
    dotnet publish -c Release -o ./publish
}

# Health check all services
function Test-Health {
    Write-Host "Checking MinIO..." -ForegroundColor Yellow
    try { 
        Invoke-RestMethod -Uri "http://localhost:9000/minio/health/live" -Method Get
        Write-Host "MinIO: Healthy" -ForegroundColor Green
    } catch { 
        Write-Host "MinIO: Not healthy" -ForegroundColor Red 
    }
    
    Write-Host "Checking OpenSearch..." -ForegroundColor Yellow
    try { 
        Invoke-RestMethod -Uri "http://localhost:9200/_cluster/health" -Method Get
        Write-Host "OpenSearch: Healthy" -ForegroundColor Green
    } catch { 
        Write-Host "OpenSearch: Not healthy" -ForegroundColor Red 
    }
    
    Write-Host "Checking API..." -ForegroundColor Yellow
    try { 
        Invoke-RestMethod -Uri "http://localhost:5000/health" -Method Get
        Write-Host "API: Healthy" -ForegroundColor Green
    } catch { 
        Write-Host "API: Not healthy" -ForegroundColor Red 
    }
}

# Initialize MinIO buckets
function Initialize-MinIO {
    docker run --rm --network silo-dev-network minio/mc:latest sh -c "
        mc alias set local http://minio:9000 dev devpassword &&
        mc mb local/files local/backups local/quarantine local/temp &&
        mc policy set public local/files"
}

# Show service URLs
function Show-Urls {
    Write-Host ""
    Write-Host "Service URLs:" -ForegroundColor Cyan
    Write-Host "  API: http://localhost:5000" -ForegroundColor White
    Write-Host "  Swagger: http://localhost:5000/swagger" -ForegroundColor White
    Write-Host "  MinIO Console: http://localhost:9001 (dev/devpassword)" -ForegroundColor White
    Write-Host "  OpenSearch: http://localhost:9200" -ForegroundColor White
    Write-Host "  Hangfire: http://localhost:5000/hangfire" -ForegroundColor White
    Write-Host ""
}

# Quick start function
function Start-Silo {
    Write-Host "Starting Silo development environment..." -ForegroundColor Cyan
    
    # Copy environment file if it doesn't exist
    if (-not (Test-Path ".env")) {
        Copy-Item ".env.dev" ".env"
        Write-Host "Created .env from .env.dev" -ForegroundColor Green
    }
    
    # Start Docker services
    Start-Dev
    
    # Wait a bit for services to start
    Write-Host "Waiting for services to start..." -ForegroundColor Yellow
    Start-Sleep -Seconds 10
    
    # Initialize MinIO
    Write-Host "Initializing MinIO buckets..." -ForegroundColor Yellow
    Initialize-MinIO
    
    # Show URLs
    Show-Urls
    
    Write-Host "Silo is ready! 🚀" -ForegroundColor Green
}

# Export functions as aliases
Set-Alias -Name silo-build -Value Build
Set-Alias -Name silo-test -Value Test
Set-Alias -Name silo-start -Value Start-Silo
Set-Alias -Name silo-dev -Value Start-Dev
Set-Alias -Name silo-dev-full -Value Start-DevFull
Set-Alias -Name silo-stop -Value Stop-Dev
Set-Alias -Name silo-logs -Value Show-DevLogs
Set-Alias -Name silo-clean -Value Clean-Docker
Set-Alias -Name silo-api -Value Start-Api
Set-Alias -Name silo-agent -Value Start-Agent
Set-Alias -Name silo-backup -Value Start-BackupWorker
Set-Alias -Name silo-health -Value Test-Health
Set-Alias -Name silo-urls -Value Show-Urls
Set-Alias -Name silo-package -Value Package

Write-Host ""
Write-Host "Silo PowerShell Module Loaded! 🚀" -ForegroundColor Cyan
Write-Host "Available commands:" -ForegroundColor Yellow
Write-Host "  silo-start      - Quick start development environment" -ForegroundColor White
Write-Host "  silo-build      - Build the solution" -ForegroundColor White
Write-Host "  silo-dev        - Start development services" -ForegroundColor White
Write-Host "  silo-stop       - Stop development services" -ForegroundColor White
Write-Host "  silo-logs       - View service logs" -ForegroundColor White
Write-Host "  silo-health     - Check service health" -ForegroundColor White
Write-Host "  silo-urls       - Show service URLs" -ForegroundColor White
Write-Host "  silo-clean      - Clean Docker environment" -ForegroundColor White
Write-Host ""