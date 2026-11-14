#!/bin/bash
# Script tự động fix merge conflict do build artifacts
# File: fix-build-artifacts-conflict.sh

echo "=== FIX MERGE CONFLICT - BUILD ARTIFACTS ==="
echo ""

# Bước 1: Xóa build artifacts khỏi Git
echo "Step 1: Removing build artifacts from Git tracking..."
git rm -r --cached "*/bin/" "*/obj/" 2>/dev/null || echo "  Some paths not found (OK)"
git rm -r --cached hotjob/bin/ 2>/dev/null || true
git rm -r --cached hotjob/obj/ 2>/dev/null || true
git rm -r --cached hotjob/Tests/hotjob.Tests/bin/ 2>/dev/null || true
git rm -r --cached hotjob/Tests/hotjob.Tests/obj/ 2>/dev/null || true

echo ""
echo "Step 2: Ensuring .gitignore is correct..."
# .gitignore đã được update rồi

echo ""
echo "Step 3: Committing changes..."
git add .gitignore
git commit -m "fix: Remove build artifacts from Git tracking

- Remove bin/ and obj/ directories from Git
- Fix .gitignore to prevent future commits of build artifacts
- Resolve merge conflict caused by binary files"

echo ""
echo "Step 4: Fetching latest from origin..."
git fetch origin

echo ""
echo "Step 5: Merging main into current branch..."
git merge origin/main || {
    echo ""
    echo "⚠️  Merge conflict detected. Resolving..."

    # Xóa tất cả bin/obj conflict files
    find . -type d -name "bin" -o -name "obj" | while read dir; do
        git rm -rf "$dir" 2>/dev/null || true
    done

    git add .
    git commit -m "Resolve merge conflict by removing build artifacts"
}

echo ""
echo "Step 6: Pushing to remote..."
git push origin HEAD

echo ""
echo "✅ DONE! Merge conflict should be resolved."
echo ""
echo "Check your PR on GitHub - it should be mergeable now."
