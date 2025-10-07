#!/bin/bash

# Silo Development Environment Startup Script
# This script manages the development environment by:
# - Stopping and removing existing Docker containers if running
# - Starting fresh Docker services (MongoDB, MinIO, OpenSearch)
# - Building and starting the Silo API server

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Parse command line arguments
CLEAN=false
while [[ $# -gt 0 ]]; do
    case $1 in
        --clean)
            CLEAN=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--clean]"
            exit 1
            ;;
    esac
done

echo -e "${GREEN}🚀 Starting Silo Development Environment${NC}"

# Function to check if Docker is running
check_docker() {
    if ! docker version &> /dev/null; then
        echo -e "${RED}❌ Docker is not running. Please start Docker first.${NC}"
        exit 1
    fi
}

# Function to stop and remove existing containers
stop_existing_containers() {
    echo -e "${YELLOW}🛑 Stopping existing Docker containers...${NC}"
    
    # Stop containers if they exist
    containers=("silo-mongodb" "silo-minio" "silo-opensearch")
    for container in "${containers[@]}"; do
        if docker ps -q --filter "name=$container" &> /dev/null; then
            running=$(docker ps -q --filter "name=$container")
            if [ ! -z "$running" ]; then
                echo -e "${YELLOW}  Stopping $container...${NC}"
                docker stop "$container" &> /dev/null || true
            fi
        fi
        
        if docker ps -aq --filter "name=$container" &> /dev/null; then
            exists=$(docker ps -aq --filter "name=$container")
            if [ ! -z "$exists" ]; then
                echo -e "${YELLOW}  Removing $container...${NC}"
                docker rm "$container" &> /dev/null || true
            fi
        fi
    done
    
    if [ "$CLEAN" = true ]; then
        echo -e "${YELLOW}🧹 Cleaning Docker volumes...${NC}"
        docker volume prune -f &> /dev/null || true
    fi
}

# Function to stop Silo API if running
stop_silo_api() {
    echo -e "${YELLOW}🛑 Stopping Silo API if running...${NC}"
    
    # Stop any running Silo.Api processes
    if pgrep -f "Silo.Api" &> /dev/null; then
        pkill -f "Silo.Api" || true
        echo -e "${YELLOW}  Stopped existing Silo API processes${NC}"
        sleep 2
    fi
    
    # Also try to kill any process using port 5289
    if command -v lsof &> /dev/null; then
        port_process=$(lsof -ti:5289 2>/dev/null || true)
        if [ ! -z "$port_process" ]; then
            kill -9 "$port_process" 2>/dev/null || true
            echo -e "${YELLOW}  Stopped process using port 5289${NC}"
        fi
    elif command -v netstat &> /dev/null; then
        port_process=$(netstat -tlnp 2>/dev/null | grep ":5289" | awk '{print $7}' | cut -d'/' -f1 || true)
        if [ ! -z "$port_process" ]; then
            kill -9 "$port_process" 2>/dev/null || true
            echo -e "${YELLOW}  Stopped process using port 5289${NC}"
        fi
    fi
}

# Function to start Docker services
start_docker_services() {
    echo -e "${BLUE}🐳 Starting Docker services...${NC}"
    
    # Get script directory and navigate to project root
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    cd "$SCRIPT_DIR"
    
    # Start Docker Compose services
    if docker-compose -f docker-compose.dev.yml up -d; then
        echo -e "${GREEN}✅ Docker services started successfully${NC}"
        
        # Wait for services to be ready
        echo -e "${BLUE}⏳ Waiting for services to be ready...${NC}"
        sleep 10
        
        # Check service health
        echo -e "${BLUE}🔍 Checking service health...${NC}"
        
        # MongoDB
        if docker exec silo-mongodb mongosh --eval 'db.runCommand({ping: 1})' --quiet &> /dev/null; then
            echo -e "${GREEN}  ✅ MongoDB is healthy${NC}"
        else
            echo -e "${YELLOW}  ⚠️  MongoDB may still be starting...${NC}"
        fi
        
        # MinIO
        if curl -s http://localhost:9000/minio/health/live &> /dev/null; then
            echo -e "${GREEN}  ✅ MinIO is healthy${NC}"
        else
            echo -e "${YELLOW}  ⚠️  MinIO may still be starting...${NC}"
        fi
        
        # OpenSearch
        if curl -s http://localhost:9200/_cluster/health &> /dev/null; then
            echo -e "${GREEN}  ✅ OpenSearch is healthy${NC}"
        else
            echo -e "${YELLOW}  ⚠️  OpenSearch may still be starting...${NC}"
        fi
    else
        echo -e "${RED}❌ Failed to start Docker services${NC}"
        exit 1
    fi
}

# Function to build and start API
start_silo_api() {
    echo -e "${BLUE}🔨 Building Silo API...${NC}"
    
    # Change to API project directory
    cd "$SCRIPT_DIR/src/Silo.Api"
    
    # Build the project
    if dotnet build; then
        echo -e "${GREEN}✅ Build successful${NC}"
        
        # Create temp directory if it doesn't exist
        temp_dir="/tmp/silo-sync"
        if [ ! -d "$temp_dir" ]; then
            mkdir -p "$temp_dir"
            echo -e "${BLUE}📁 Created sync directory: $temp_dir${NC}"
        fi
        
        echo -e "${BLUE}🚀 Starting Silo API server...${NC}"
        echo -e "${CYAN}📡 API will be available at: http://localhost:5289${NC}"
        echo -e "${CYAN}🌐 Test page at: http://localhost:8081/test-page.html${NC}"
        echo -e "${CYAN}📚 API docs at: http://localhost:5289/swagger${NC}"
        echo ""
        echo -e "${YELLOW}Press Ctrl+C to stop the server${NC}"
        echo ""
        
        # Start the API server
        dotnet run --urls "http://localhost:5289"
    else
        echo -e "${RED}❌ Build failed${NC}"
        exit 1
    fi
}

# Cleanup function for graceful shutdown
cleanup() {
    echo -e "\n${YELLOW}🛑 Shutting down...${NC}"
    stop_silo_api
    exit 0
}

# Set trap for cleanup
trap cleanup SIGINT SIGTERM

# Main execution
main() {
    check_docker
    stop_silo_api
    stop_existing_containers
    start_docker_services
    start_silo_api
}

# Run main function
main "$@"