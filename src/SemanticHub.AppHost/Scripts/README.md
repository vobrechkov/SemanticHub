# Azure Setup Scripts

This directory contains scripts for setting up Azure RBAC role assignments required for local development.

## Prerequisites

- Azure CLI installed and logged in (`az login`)
- Appropriate permissions to assign roles in the resource group
- Resources already provisioned in Azure (via `azd provision` or manual deployment)

## Usage

### Option 1: Run Individual Scripts

```bash
# Make scripts executable
chmod +x Scripts/*.sh

# Run specific setup script
./Scripts/setup-storage-rbac.sh
./Scripts/setup-search-rbac.sh
./Scripts/setup-openai-rbac.sh
```

### Option 2: Run Complete Setup

```bash
# Run all RBAC assignments at once
./Scripts/setup-all-rbac.sh
```

## Script Descriptions

| Script | Purpose | Roles Assigned |
|--------|---------|----------------|
| `setup-storage-rbac.sh` | Azure Storage access | Storage Blob Data Contributor |
| `setup-search-rbac.sh` | Azure AI Search access | Search Index Data Contributor, Search Service Contributor |
| `setup-openai-rbac.sh` | Azure OpenAI access | Cognitive Services OpenAI User |
| `setup-all-rbac.sh` | Run all above scripts | All roles |

## Configuration

The scripts use the following default values (modify in each script if needed):

- **Resource Group**: `semhub-eus-dev-rg`
- **Storage Account**: `semhubeusdevstorage`
- **Search Service**: `semhub-eus-dev-search`
- **OpenAI Account**: `semhub-eus-dev-openai`

## Troubleshooting

### "Insufficient privileges" error
You need `Owner` or `User Access Administrator` role on the resource group to assign roles.

### "Resource not found" error
Ensure resources are provisioned first:
```bash
azd provision
```

### Role assignments not taking effect
Wait 5-10 minutes for Azure RBAC propagation, then restart your application.

## For New Developers

1. Clone the repository
2. Run `azd auth login` or `az login`
3. Run `./Scripts/setup-all-rbac.sh`
4. Wait a few minutes for roles to propagate
5. Run the application with `dotnet run --project src/SemanticHub.AppHost`
