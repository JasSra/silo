# Silo Development Helper Scripts

# Build the entire solution
build:
	dotnet restore
	dotnet build

# Run tests
test:
	dotnet test

# Start development environment
dev-up:
	docker-compose -f docker-compose.dev.yml up -d

# Start development environment with full services (including ClamAV)
dev-up-full:
	docker-compose -f docker-compose.dev.yml --profile full up -d

# Stop development environment
dev-down:
	docker-compose -f docker-compose.dev.yml down

# View development logs
dev-logs:
	docker-compose -f docker-compose.dev.yml logs -f

# Start production environment
prod-up:
	docker-compose -f docker-compose.prod.yml up -d

# Stop production environment  
prod-down:
	docker-compose -f docker-compose.prod.yml down

# Clean Docker volumes and rebuild
clean:
	docker-compose -f docker-compose.dev.yml down -v
	docker system prune -f
	docker volume prune -f

# Run the API locally
run-api:
	cd src/Silo.Api && dotnet run

# Run the Agent locally
run-agent:
	cd src/Silo.Agent && dotnet run

# Run the Backup Worker locally
run-backup:
	cd src/Silo.BackupWorker && dotnet run

# Database migrations (when EF Core is set up)
migrate:
	cd src/Silo.Api && dotnet ef database update

# Create new migration
migration:
	cd src/Silo.Api && dotnet ef migrations add $(name)

# Package for deployment
package:
	dotnet publish -c Release -o ./publish

# Health check all services
health:
	@echo "Checking MinIO..."
	@curl -f http://localhost:9000/minio/health/live || echo "MinIO not healthy"
	@echo "Checking OpenSearch..."
	@curl -f http://localhost:9200/_cluster/health || echo "OpenSearch not healthy"
	@echo "Checking Redis..."
	@redis-cli ping || echo "Redis not healthy"
	@echo "Checking API..."
	@curl -f http://localhost:5000/health || echo "API not healthy"

# Initialize MinIO buckets
init-minio:
	docker run --rm --network silo-dev-network minio/mc:latest sh -c "\
		mc alias set local http://minio:9000 dev devpassword && \
		mc mb local/files local/backups local/quarantine local/temp && \
		mc policy set public local/files"

# Show service URLs
urls:
	@echo "API: http://localhost:5000"
	@echo "Swagger: http://localhost:5000/swagger"
	@echo "MinIO Console: http://localhost:9001 (dev/devpassword)"
	@echo "OpenSearch: http://localhost:9200"
	@echo "Hangfire: http://localhost:5000/hangfire"

.PHONY: build test dev-up dev-up-full dev-down dev-logs prod-up prod-down clean run-api run-agent run-backup migrate migration package health init-minio urls