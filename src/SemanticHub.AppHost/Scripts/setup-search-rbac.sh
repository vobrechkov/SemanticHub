#!/bin/bash
set -e

# Azure AI Search RBAC Setup Script
# This script assigns the necessary roles for local development with Azure AI Search

# Configuration
RESOURCE_GROUP="semhub-eus-dev-rg"
SEARCH_SERVICE="semhub-eus-dev-search"

echo "Setting up Azure AI Search RBAC for local development..."
echo "Resource Group: $RESOURCE_GROUP"
echo "Search Service: $SEARCH_SERVICE"
echo ""

# Get current user's object ID
USER_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
echo "Current user object ID: $USER_OBJECT_ID"

# Get subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
echo "Subscription ID: $SUBSCRIPTION_ID"
echo ""

# Assign Search Index Data Contributor role
echo "Assigning 'Search Index Data Contributor' role..."
az role assignment create \
  --assignee "$USER_OBJECT_ID" \
  --role "Search Index Data Contributor" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Search/searchServices/$SEARCH_SERVICE" \
  --only-show-errors

# Assign Search Service Contributor role
echo "Assigning 'Search Service Contributor' role..."
az role assignment create \
  --assignee "$USER_OBJECT_ID" \
  --role "Search Service Contributor" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Search/searchServices/$SEARCH_SERVICE" \
  --only-show-errors

echo ""
echo "✅ Role assignments completed successfully!"
echo ""
echo "Note: It may take 5-10 minutes for the role assignments to propagate."
echo "If you encounter authentication errors, wait a few minutes and restart your application."
