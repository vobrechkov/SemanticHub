#!/bin/bash
set -e

# Azure OpenAI RBAC Setup Script
# This script assigns the necessary roles for local development with Azure OpenAI

# Configuration
RESOURCE_GROUP="semhub-eus-dev-rg"
OPENAI_ACCOUNT="semhub-eus-dev-openai"

echo "Setting up Azure OpenAI RBAC for local development..."
echo "Resource Group: $RESOURCE_GROUP"
echo "OpenAI Account: $OPENAI_ACCOUNT"
echo ""

# Get current user's object ID
USER_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
echo "Current user object ID: $USER_OBJECT_ID"

# Get subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
echo "Subscription ID: $SUBSCRIPTION_ID"
echo ""

# Assign Cognitive Services OpenAI User role
echo "Assigning 'Cognitive Services OpenAI User' role..."
az role assignment create \
  --assignee "$USER_OBJECT_ID" \
  --role "Cognitive Services OpenAI User" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.CognitiveServices/accounts/$OPENAI_ACCOUNT" \
  --only-show-errors

echo ""
echo "✅ Role assignment completed successfully!"
echo ""
echo "Note: It may take 5-10 minutes for the role assignment to propagate."
echo "If you encounter authentication errors, wait a few minutes and restart your application."
