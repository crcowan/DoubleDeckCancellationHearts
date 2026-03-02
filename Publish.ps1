$ErrorActionPreference = "Stop"

$ProjectRoot = "C:\Users\crcow\.gemini\antigravity\scratch\DoubleDeckCancellationHearts"
$ReleaseFolder = "$ProjectRoot\DoubleDeckCancellationHearts_Release"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Building Double Deck Cancellation Hearts" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 1. Clean the release folder
if (Test-Path $ReleaseFolder) {
    Write-Host "[1/5] Cleaning old release folder..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $ReleaseFolder
}
New-Item -ItemType Directory -Force -Path $ReleaseFolder | Out-Null
Write-Host "[1/5] Release folder ready." -ForegroundColor Green

# 2. Build the React Frontend
Write-Host "[2/5] Compiling React Frontend to Static HTML/JS/CSS..." -ForegroundColor Yellow
Set-Location "$ProjectRoot\frontend"
npm install --silent
npm run build
Write-Host "[2/5] Frontend compilation complete." -ForegroundColor Green

# 3. Publish Backend API (Self-Contained, No .NET Required on target)
Write-Host "[3/5] Publishing Backend API (Self-Contained .NET 10)..." -ForegroundColor Yellow
Set-Location "$ProjectRoot\GameEngine.Api"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$ReleaseFolder\Api" 
Write-Host "[3/5] Backend API published." -ForegroundColor Green

# 4. Publish Desktop Wrapper (Self-Contained, No .NET Required on target)
Write-Host "[4/5] Publishing Photino Desktop Wrapper (Self-Contained .NET 8)..." -ForegroundColor Yellow
Set-Location "$ProjectRoot\GameEngine.Desktop"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$ReleaseFolder"
Write-Host "[4/5] Desktop Wrapper published." -ForegroundColor Green

# 5. Cleanup
Write-Host "[5/6] Finalizing standalone package..." -ForegroundColor Yellow
# Ensure the wwwroot was copied to the release Api folder
if (Test-Path "$ProjectRoot\GameEngine.Api\wwwroot") {
    Copy-Item -Recurse -Force "$ProjectRoot\GameEngine.Api\wwwroot" "$ReleaseFolder\Api\"
}

# 6. Zip everything up
Write-Host "[6/6] Zipping the final packaged game..." -ForegroundColor Yellow
$ZipPath = "$ProjectRoot\DoubleDeckCancellationHearts_Release.zip"
if (Test-Path $ZipPath) {
    Remove-Item -Force $ZipPath
}
Compress-Archive -Path "$ReleaseFolder\*" -DestinationPath $ZipPath
Write-Host "[6/6] Zip completed at $ZipPath." -ForegroundColor Green

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " BUILD COMPLETE!" -ForegroundColor Green
Write-Host " The standalone zero-install game ZIP is ready at:" -ForegroundColor White
Write-Host " $ZipPath" -ForegroundColor White
Write-Host "=============================================" -ForegroundColor Cyan
