#!/bin/bash
set -e

# Azure Storage RBAC Setup Script
# This script assigns the necessary roles for local development with Azure Blob Storage

# Configuration
RESOURCE_GROUP="semhub-eus-dev-rg"
STORAGE_ACCOUNT="semhubeusdevstorage"

echo "Setting up Azure Storage RBAC for local development..."
echo "Resource Group: $RESOURCE_GROUP"
echo "Storage Account: $STORAGE_ACCOUNT"
echo ""

# Get current user's object ID
USER_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
echo "Current user object ID: $USER_OBJECT_ID"

# Get subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
echo "Subscription ID: $SUBSCRIPTION_ID"
echo ""

# Assign Storage Blob Data Contributor role
echo "Assigning 'Storage Blob Data Contributor' role..."
az role assignment create \
  --assignee "$USER_OBJECT_ID" \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Storage/storageAccounts/$STORAGE_ACCOUNT" \
  --only-show-errors

echo ""
echo "✅ Role assignment completed successfully!"
echo ""
echo "Note: It may take 5-10 minutes for the role assignment to propagate."
echo "If you encounter authentication errors, wait a few minutes and restart your application."