# Script tự động fix merge conflict do build artifacts
# File: fix-build-artifacts-conflict.ps1
# Usage: .\fix-build-artifacts-conflict.ps1

Write-Host "=== FIX MERGE CONFLICT - BUILD ARTIFACTS ===" -ForegroundColor Cyan
Write-Host ""

# Bước 1: Xóa build artifacts khỏi Git
Write-Host "Step 1: Removing build artifacts from Git tracking..." -ForegroundColor Yellow
try {
    git rm -r --cached "*/bin/" "*/obj/" 2>$null
} catch {
    Write-Host "  Some paths not found (OK)" -ForegroundColor Gray
}

# Xóa cụ thể từng thư mục
$paths = @(
    "hotjob/bin/",
    "hotjob/obj/",
    "hotjob/Tests/hotjob.Tests/bin/",
    "hotjob/Tests/hotjob.Tests/obj/"
)

foreach ($path in $paths) {
    try {
        git rm -r --cached $path 2>$null
    } catch {
        # Ignore errors - file might not exist
    }
}

Write-Host ""
Write-Host "Step 2: Committing changes..." -ForegroundColor Yellow
git add .gitignore
git commit -m "fix: Remove build artifacts from Git tracking

- Remove bin/ and obj/ directories from Git
- Fix .gitignore to prevent future commits of build artifacts
- Resolve merge conflict caused by binary files"

Write-Host ""
Write-Host "Step 3: Fetching latest from origin..." -ForegroundColor Yellow
git fetch origin

Write-Host ""
Write-Host "Step 4: Merging main into current branch..." -ForegroundColor Yellow
git merge origin/main
$mergeExitCode = $LASTEXITCODE

if ($mergeExitCode -ne 0) {
    Write-Host ""
    Write-Host "⚠️  Merge conflict detected. Resolving..." -ForegroundColor Red

    # Xóa tất cả bin/obj conflict files
    Write-Host "  Removing conflicted build artifact files..." -ForegroundColor Gray

    Get-ChildItem -Path . -Include bin,obj -Recurse -Directory | ForEach-Object {
        try {
            git rm -rf $_.FullName 2>$null
        } catch {
            # Ignore
        }
    }

    git add .
    git commit -m "Resolve merge conflict by removing build artifacts"
}

Write-Host ""
Write-Host "Step 5: Pushing to remote..." -ForegroundColor Yellow
git push origin HEAD

Write-Host ""
Write-Host "✅ DONE! Merge conflict should be resolved." -ForegroundColor Green
Write-Host ""
Write-Host "Check your PR on GitHub - it should be mergeable now." -ForegroundColor Cyan
Write-Host ""

# Kiểm tra status cuối cùng
Write-Host "Current status:" -ForegroundColor Yellow
git status
