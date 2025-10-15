#!/bin/bash
set -e

# Complete Azure RBAC Setup Script
# This script runs all RBAC setup scripts for local development

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "========================================"
echo "Azure RBAC Setup for SemanticHub"
echo "========================================"
echo ""
echo "This script will assign all necessary roles for local development."
echo "You must have Owner or User Access Administrator role on the resource group."
echo ""
read -p "Press Enter to continue or Ctrl+C to cancel..."
echo ""

# Make all scripts executable
chmod +x "$SCRIPT_DIR"/*.sh

# Run each setup script
echo "1/3 Setting up Azure OpenAI roles..."
"$SCRIPT_DIR/setup-openai-rbac.sh"
echo ""
echo "========================================"
echo ""

echo "2/3 Setting up Azure AI Search roles..."
"$SCRIPT_DIR/setup-search-rbac.sh"
echo ""
echo "========================================"
echo ""

echo "3/3 Setting up Azure Storage roles..."
"$SCRIPT_DIR/setup-storage-rbac.sh"
echo ""
echo "========================================"
echo ""

echo "✅ All role assignments completed successfully!"
echo ""
echo "Next steps:"
echo "1. Wait 5-10 minutes for role propagation"
echo "2. Run: dotnet run --project src/SemanticHub.AppHost"
echo "3. If you see authentication errors, wait a bit longer and try again"
