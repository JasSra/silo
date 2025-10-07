#!/bin/bash

# Silo Development Environment Stop Script
# This script stops the development environment by:
# - Stopping the Silo API server
# - Stopping and optionally removing Docker containers
# - Optionally cleaning up Docker volumes

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
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

echo -e "${YELLOW}🛑 Stopping Silo Development Environment${NC}"

# Function to stop Silo API
stop_silo_api() {
    echo -e "${YELLOW}🛑 Stopping Silo API...${NC}"
    
    # Stop any running Silo.Api processes
    if pgrep -f "Silo.Api" &> /dev/null; then
        pkill -f "Silo.Api" || true
        echo -e "${GREEN}  ✅ Stopped Silo API processes${NC}"
        sleep 2
    fi
    
    # Also try to kill any process using port 5289
    if command -v lsof &> /dev/null; then
        port_process=$(lsof -ti:5289 2>/dev/null || true)
        if [ ! -z "$port_process" ]; then
            kill -9 "$port_process" 2>/dev/null || true
            echo -e "${GREEN}  ✅ Stopped process using port 5289${NC}"
        fi
    elif command -v netstat &> /dev/null; then
        port_process=$(netstat -tlnp 2>/dev/null | grep ":5289" | awk '{print $7}' | cut -d'/' -f1 || true)
        if [ ! -z "$port_process" ]; then
            kill -9 "$port_process" 2>/dev/null || true
            echo -e "${GREEN}  ✅ Stopped process using port 5289${NC}"
        fi
    fi
    
    if ! pgrep -f "Silo.Api" &> /dev/null; then
        echo -e "${BLUE}  ℹ️  No Silo API processes found${NC}"
    fi
}

# Function to stop Docker services
stop_docker_services() {
    echo -e "${YELLOW}🐳 Stopping Docker services...${NC}"
    
    # Get script directory and navigate to project root
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    cd "$SCRIPT_DIR"
    
    if [ "$CLEAN" = true ]; then
        echo -e "${YELLOW}  🧹 Stopping and removing containers...${NC}"
        docker-compose -f docker-compose.dev.yml down -v 2>/dev/null || true
        
        # Remove any orphaned containers
        containers=("silo-mongodb" "silo-minio" "silo-opensearch")
        for container in "${containers[@]}"; do
            if docker ps -aq --filter "name=$container" &> /dev/null; then
                exists=$(docker ps -aq --filter "name=$container")
                if [ ! -z "$exists" ]; then
                    docker rm -f "$container" &> /dev/null || true
                fi
            fi
        done
        
        echo -e "${YELLOW}  🧹 Cleaning Docker volumes...${NC}"
        docker volume prune -f &> /dev/null || true
        
        echo -e "${GREEN}  ✅ Docker services stopped and cleaned${NC}"
    else
        echo -e "${YELLOW}  🛑 Stopping containers...${NC}"
        docker-compose -f docker-compose.dev.yml stop 2>/dev/null || true
        echo -e "${GREEN}  ✅ Docker services stopped${NC}"
    fi
}

# Function to show status
show_status() {
    echo ""
    echo -e "${BLUE}📊 Current Status:${NC}"
    
    # Check API process
    if pgrep -f "Silo.Api" &> /dev/null; then
        echo -e "${RED}  🔴 Silo API: Still running${NC}"
    else
        echo -e "${GREEN}  ✅ Silo API: Stopped${NC}"
    fi
    
    # Check Docker containers
    containers=("silo-mongodb" "silo-minio" "silo-opensearch")
    for container in "${containers[@]}"; do
        if docker ps -q --filter "name=$container" &> /dev/null; then
            running=$(docker ps -q --filter "name=$container")
            if [ ! -z "$running" ]; then
                echo -e "${RED}  🔴 $container: Running${NC}"
            else
                echo -e "${GREEN}  ✅ $container: Stopped${NC}"
            fi
        else
            echo -e "${GREEN}  ✅ $container: Stopped${NC}"
        fi
    done
    
    echo ""
    if [ "$CLEAN" = true ]; then
        echo -e "${GREEN}💚 Development environment completely cleaned!${NC}"
    else
        echo -e "${YELLOW}💛 Development environment stopped (containers preserved)${NC}"
        echo -e "${BLUE}   Use './dev-stop.sh --clean' for complete cleanup${NC}"
    fi
}

# Main execution
main() {
    stop_silo_api
    stop_docker_services
    show_status
}

# Run main function
main "$@"