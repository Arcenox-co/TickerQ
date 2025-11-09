# Branch Sync Workflow

This workflow automatically syncs changes from `main` branch to version-specific branches (like `net8`, `net7`, etc.) while preserving target branch configurations.

## Features

- ✅ Automatic merging from `main` to version branches on every push
- ✅ Preserves `.csproj` files from target branches (keeps correct .NET versions)
- ✅ Updates version numbers and target framework in `Directory.Build.props` (e.g., `9.0.x` → `8.0.x`, `net9.0` → `net8.0`)
- ✅ Only commits when there are actual code changes
- ✅ Parallel processing of multiple target branches
- ✅ Manual trigger support with custom branch selection
- ✅ PR-based approach (no direct pushes, avoids merge conflicts)
- ✅ Automatic PR creation with detailed descriptions

## Setup

### 1. Repository Variables

Configure target branches via GitHub repository variables:

1. Go to **Settings** → **Secrets and variables** → **Actions**
2. Click **Variables** → **New repository variable**
3. Name: `TARGET_BRANCHES`
4. Value: `["net8"]` (for single branch) or `["net8", "net7", "net6"]` (for multiple branches)

### 2. Initial Setup

The workflow will automatically trigger on pushes to `main`. For the first run or manual testing:

1. Go to **Actions** tab
2. Select **Sync Version Branches** workflow
3. Click **Run workflow**
4. Optionally specify custom branches or enable dry-run

## How It Works

1. **Trigger**: Runs automatically on `main` branch pushes
2. **Branch Check**: Verifies target branches exist
3. **Merge Analysis**: Checks if new commits need merging
4. **Smart Merge**: Merges code while preserving `.csproj` files
5. **Version & Framework Update**: Transforms version numbers and target framework (e.g., `9.0.0-beta.10` → `8.0.0-beta.10`, `net9.0` → `net8.0`)
6. **Clean Application**: Uses cherry-pick to avoid merge conflicts
7. **PR Creation**: Creates pull requests for review instead of direct pushes

## Configuration Examples

### Single Branch Setup
```json
TARGET_BRANCHES: ["net8"]
```

### Multiple Branches Setup
```json
TARGET_BRANCHES: ["net8", "net7", "net6"]
```

### Manual Workflow Dispatch
- **target_branches**: `net8,net7` (comma-separated)
- **dry_run**: `true` (for testing without pushing)

## PR-Based Sync Approach

This workflow creates **Pull Requests** instead of direct pushes to avoid merge conflicts. Each sync:

1. Creates a new branch (e.g., `sync-main-to-net8-20241109-143022`)
2. Cherry-picks commits from main to avoid merge conflicts
3. Updates version numbers and target frameworks appropriately
4. Creates a PR with detailed description for review
5. Preserves target branch configurations (.csproj files)

**Benefits:**
- ✅ No merge conflicts in Directory.Build.props
- ✅ Clean commit history
- ✅ Review process for synced changes
- ✅ Easy rollback if needed

## Workflow Structure

- **sync-branches**: Matrix job that processes each target branch in parallel
- **summary**: Provides a summary of all sync operations

## Branch Naming Convention

The workflow expects branches named like `net8`, `net7`, `net6`, etc. The numeric part is automatically extracted to determine the target .NET version for version number updates.

## Error Handling

- Skips branches that don't exist
- Aborts merges with no changes
- Continues processing other branches if one fails
- Provides detailed logs for troubleshooting