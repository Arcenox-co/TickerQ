#!/bin/bash

# Setup script for Branch Sync Workflow
# This script helps configure the TARGET_BRANCHES repository variable

set -e

echo "🚀 Setting up Branch Sync Workflow"
echo

# Check if we're in a git repository
if ! git remote get-url origin > /dev/null 2>&1; then
    echo "❌ Not in a git repository or no remote origin found"
    exit 1
fi

# Get repository info
REPO_URL=$(git remote get-url origin)
REPO_NAME=$(basename "$REPO_URL" .git)

echo "📋 Repository: $REPO_NAME"
echo

# Default branches
DEFAULT_BRANCHES='["net8"]'

# Ask user for target branches
echo "Enter target branches (comma-separated, default: net8):"
read -r user_input

if [ -n "$user_input" ]; then
    # Convert comma-separated to JSON array
    BRANCHES=$(echo "$user_input" | sed 's/ *, */,/g' | sed 's/,/","/g' | sed 's/^/["/;s/$/"]/')
else
    BRANCHES=$DEFAULT_BRANCHES
fi

echo
echo "🔧 Configuration:"
echo "TARGET_BRANCHES = $BRANCHES"
echo

# Instructions for manual setup
echo "📝 Manual Setup Instructions:"
echo "1. Go to your repository on GitHub"
echo "2. Navigate to Settings → Secrets and variables → Actions"
echo "3. Click 'Variables' → 'New repository variable'"
echo "4. Name: TARGET_BRANCHES"
echo "5. Value: $BRANCHES"
echo

echo "Alternatively, you can set it via GitHub CLI:"
echo "gh variable set TARGET_BRANCHES -b '$BRANCHES' -R <owner>/$REPO_NAME"
echo

echo "✅ Setup complete! The workflow will use these branches:"
echo "$BRANCHES" | jq -r '.[]' 2>/dev/null || echo "  $(echo "$BRANCHES" | sed 's/["\[\]]//g' | sed 's/,/ /g')"
echo

echo "💡 Next steps:"
echo "- Push to main branch to trigger the workflow"
echo "- Or manually run the workflow from GitHub Actions tab"
