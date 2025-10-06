# Silo - Scalable File Management System

A scalable, containerized .NET-based system for storing and indexing millions of files. The solution leverages MinIO for high-performance S3-compatible object storage, OpenSearch for distributed search and indexing capabilities, and Hangfire for resilient background processing and AI-driven metadata enrichment.

## Architecture Overview

- **MinIO**: S3-compatible object storage for file storage
- **OpenSearch**: Distributed search and indexing engine
- **Hangfire**: Background job processing with Redis
- **PostgreSQL**: Application data and metadata storage
- **ClamAV**: Malware scanning for uploaded files
- **Redis**: Caching and Hangfire job storage
- **.NET 8**: Core application framework

## Project Structure

```
├── src/
│   ├── Silo.Api/           # REST/gRPC API endpoints
│   ├── Silo.Core/          # Shared models and interfaces
│   ├── Silo.Agent/         # File sync agent (FileSystemWatcher + rsync)
│   └── Silo.BackupWorker/  # Backup service worker
├── docker/                 # Docker configurations and volumes
├── docker-compose.yml      # Production configuration
├── docker-compose.dev.yml  # Development configuration
└── docker-compose.prod.yml # Production with clustering
```

## Features

### Core Features
- ✅ File upload with malware scanning
- ✅ Distributed object storage with MinIO
- ✅ Full-text search with OpenSearch
- ✅ Background processing with Hangfire
- ✅ Automated backup workers
- ✅ REST API with Swagger documentation
- ✅ File sync agent with FileSystemWatcher
- ✅ Multi-environment Docker support

### AI-Powered Features (Planned)
- 🚧 Automatic metadata extraction
- 🚧 Content-based tagging
- 🚧 Semantic search capabilities
- 🚧 Intelligent file categorization
- 🚧 Duplicate detection

### Enterprise Features (Planned)
- 🚧 Authentication & authorization
- 🚧 Multi-tenant support
- 🚧 Audit logging
- 🚧 Performance monitoring
- 🚧 Advanced backup strategies

## Quick Start

### Prerequisites

- Docker Desktop
- .NET 8 SDK (for development)
- Git

### Development Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/YourUsername/silo.git
   cd silo
   ```

2. **Start the development environment**
   ```bash
   # Copy environment configuration
   cp .env.dev .env
   
   # Start all services
   docker-compose -f docker-compose.dev.yml up -d
   
   # View logs
   docker-compose -f docker-compose.dev.yml logs -f
   ```

3. **Build and run the API**
   ```bash
   dotnet restore
   dotnet build
   cd src/Silo.Api
   dotnet run
   ```

4. **Access the services**
   - API: http://localhost:5000
   - Swagger UI: http://localhost:5000/swagger
   - MinIO Console: http://localhost:9001 (dev/devpassword)
   - OpenSearch: http://localhost:9200
   - Hangfire Dashboard: http://localhost:5000/hangfire

### Production Setup

1. **Configure environment**
   ```bash
   cp .env.prod.template .env.prod
   # Edit .env.prod with your production values
   ```

2. **Deploy with Docker Compose**
   ```bash
   docker-compose -f docker-compose.prod.yml up -d
   ```

## API Documentation

### File Operations

#### Upload File
```http
POST /api/files/upload
Content-Type: multipart/form-data

{
  "file": "binary_file_data"
}
```

#### Search Files
```http
GET /api/files/search?query=document&skip=0&take=20
```

#### Download File
```http
GET /api/files/{id}/download
```

### Backup Operations

#### Create Backup Job
```http
POST /api/backup/jobs
Content-Type: application/json

{
  "name": "Daily Backup",
  "type": "Full",
  "sourcePath": "files/",
  "destinationPath": "backups/daily/",
  "schedule": "0 2 * * *"
}
```

## Development

### Building the Solution

```bash
# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Run tests
dotnet test

# Package for deployment
dotnet publish -c Release
```

### Adding New Services

1. Create new project in `src/` directory
2. Add project reference to solution
3. Implement core interfaces from `Silo.Core`
4. Add service registration in `Program.cs`

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `SILO_MINIO_ENDPOINT` | MinIO server endpoint | `http://localhost:9000` |
| `SILO_OPENSEARCH_ENDPOINT` | OpenSearch endpoint | `http://localhost:9200` |
| `SILO_REDIS_CONNECTION` | Redis connection string | `localhost:6379` |
| `SILO_POSTGRES_CONNECTION` | PostgreSQL connection | See `.env.dev` |
| `SILO_MAX_FILE_SIZE` | Maximum file upload size | `100MB` |

## Docker Configuration

### Development Environment
```bash
# Start with minimal services (no ClamAV for faster startup)
docker-compose -f docker-compose.dev.yml up -d

# Start with all services including antivirus
docker-compose -f docker-compose.dev.yml --profile full up -d
```

### Production Environment
```bash
# Production deployment with clustering
docker-compose -f docker-compose.prod.yml up -d
```

## Monitoring and Health Checks

- **Health Endpoint**: `/health`
- **Metrics**: Integrated with OpenSearch for log aggregation
- **Hangfire Dashboard**: Real-time job monitoring
- **MinIO Console**: Storage monitoring and management

## Security

- File scanning with ClamAV before storage
- Quarantine system for suspicious files
- JWT authentication (when enabled)
- Network isolation with Docker networks
- Configurable file size and type restrictions

## Backup Strategy

The system includes automated backup workers that:

1. **Full Backups**: Complete system backup on schedule
2. **Incremental Backups**: Only changed files
3. **Retention Policies**: Automatic cleanup of old backups
4. **Verification**: Checksum validation of backup integrity
5. **Cross-Region**: Support for external backup destinations

## Performance Tuning

### MinIO Optimization
- Use SSD storage for better I/O performance
- Configure appropriate number of drives for erasure coding
- Tune memory limits based on concurrent connections

### OpenSearch Optimization
- Allocate sufficient heap memory (typically 50% of RAM)
- Configure proper shard and replica settings
- Use index templates for consistent mapping

### Application Optimization
- Configure Hangfire worker counts based on CPU cores
- Tune database connection pools
- Implement appropriate caching strategies

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

For support and questions:
- Create an issue on GitHub
- Check the [Documentation](docs/)
- Review existing [Issues](https://github.com/YourUsername/silo/issues)

## Roadmap

- [ ] Complete MVP implementation
- [ ] Add AI-powered metadata extraction
- [ ] Implement semantic search
- [ ] Add web frontend
- [ ] Mobile app support
- [ ] Kubernetes deployment manifests
- [ ] Enterprise security features
- [ ] Advanced analytics and reporting