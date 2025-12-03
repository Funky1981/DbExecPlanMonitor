#!/bin/bash
#
# Install DbExecPlanMonitor as a systemd service on Linux
#
# Usage: sudo ./install-linux-service.sh [environment]
# Example: sudo ./install-linux-service.sh Production
#

set -e

# Configuration
SERVICE_NAME="dbexecplanmonitor"
INSTALL_DIR="/opt/dbexecplanmonitor"
SERVICE_USER="dbmonitor"
SERVICE_GROUP="dbmonitor"
ENVIRONMENT="${1:-Production}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo -e "${CYAN}========================================"
echo "DbExecPlanMonitor Linux Service Setup"
echo -e "========================================${NC}"
echo ""

# Check for root privileges
if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}Error: This script must be run as root (use sudo)${NC}"
    exit 1
fi

# Find script directory and solution root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_ROOT="$(dirname "$SCRIPT_DIR")"
WORKER_PROJECT="$SOLUTION_ROOT/src/DbExecPlanMonitor.Worker"

if [ ! -d "$WORKER_PROJECT" ]; then
    echo -e "${RED}Error: Worker project not found at: $WORKER_PROJECT${NC}"
    exit 1
fi

# Check for .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}Error: .NET SDK not found. Please install .NET 8.0 SDK.${NC}"
    exit 1
fi

# Stop existing service if running
if systemctl is-active --quiet "$SERVICE_NAME"; then
    echo -e "${YELLOW}Stopping existing service...${NC}"
    systemctl stop "$SERVICE_NAME"
fi

# Create service user if it doesn't exist
if ! id "$SERVICE_USER" &>/dev/null; then
    echo -e "${GREEN}Creating service user: $SERVICE_USER${NC}"
    useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi

# Publish the application
echo ""
echo -e "${GREEN}Step 1: Publishing application...${NC}"
echo "  Project: $WORKER_PROJECT"
echo "  Output:  $INSTALL_DIR"
echo ""

# Clean and create install directory
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"

dotnet publish "$WORKER_PROJECT" \
    -c Release \
    -o "$INSTALL_DIR" \
    -r linux-x64 \
    --self-contained false

if [ $? -ne 0 ]; then
    echo -e "${RED}Error: Failed to publish application.${NC}"
    exit 1
fi

echo -e "${GREEN}Application published successfully.${NC}"

# Copy environment-specific config
SOURCE_CONFIG="$WORKER_PROJECT/appsettings.$ENVIRONMENT.json"
if [ -f "$SOURCE_CONFIG" ]; then
    echo -e "${GREEN}Copying $ENVIRONMENT configuration...${NC}"
    cp "$SOURCE_CONFIG" "$INSTALL_DIR/"
fi

# Set ownership
chown -R "$SERVICE_USER:$SERVICE_GROUP" "$INSTALL_DIR"
chmod -R 750 "$INSTALL_DIR"

# Install systemd service
echo ""
echo -e "${GREEN}Step 2: Installing systemd service...${NC}"

cp "$SCRIPT_DIR/dbexecplanmonitor.service" /etc/systemd/system/

# Update environment in service file
sed -i "s/Environment=DOTNET_ENVIRONMENT=Production/Environment=DOTNET_ENVIRONMENT=$ENVIRONMENT/" \
    /etc/systemd/system/dbexecplanmonitor.service

# Reload systemd
systemctl daemon-reload

# Enable service to start on boot
systemctl enable "$SERVICE_NAME"

echo -e "${GREEN}Service installed successfully.${NC}"

# Create log directory
LOG_DIR="/var/log/dbexecplanmonitor"
mkdir -p "$LOG_DIR"
chown "$SERVICE_USER:$SERVICE_GROUP" "$LOG_DIR"

echo ""
echo -e "${CYAN}========================================"
echo -e "${GREEN}Installation Complete!"
echo -e "${CYAN}========================================${NC}"
echo ""
echo -e "Service Details:"
echo "  Name:        $SERVICE_NAME"
echo "  Path:        $INSTALL_DIR"
echo "  Environment: $ENVIRONMENT"
echo "  User:        $SERVICE_USER"
echo ""
echo -e "${YELLOW}Next Steps:${NC}"
echo "  1. Configure connection strings in $INSTALL_DIR/appsettings.$ENVIRONMENT.json"
echo "  2. Start the service: sudo systemctl start $SERVICE_NAME"
echo "  3. Check status: sudo systemctl status $SERVICE_NAME"
echo "  4. View logs: sudo journalctl -u $SERVICE_NAME -f"
echo ""
echo -e "Commands:"
echo "  Start:   sudo systemctl start $SERVICE_NAME"
echo "  Stop:    sudo systemctl stop $SERVICE_NAME"
echo "  Status:  sudo systemctl status $SERVICE_NAME"
echo "  Logs:    sudo journalctl -u $SERVICE_NAME -f"
echo "  Disable: sudo systemctl disable $SERVICE_NAME"
echo ""
