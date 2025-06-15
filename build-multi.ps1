# Build RevTun for Multiple Platforms
Write-Host "Building RevTun for Multiple Platforms..." -ForegroundColor Green
Write-Host

# Change to project directory
Set-Location -Path "revtun"

# Clean all builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean -c Release

# Build for .NET Framework 4.8 (Cobalt Strike)
Write-Host "Building for .NET Framework 4.8 (Cobalt Strike execute-assembly)..." -ForegroundColor Cyan
dotnet build -c Release -f net48 -p:Platform=AnyCPU

# Build for Linux x64
Write-Host "Building for Linux x64..." -ForegroundColor Cyan
dotnet publish -c Release -f net8.0 -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Build for Windows x64  
Write-Host "Building for Windows x64..." -ForegroundColor Cyan
dotnet publish -c Release -f net8.0 -r win-x64 --self-contained true -p:PublishSingleFile=true

Write-Host ""
Write-Host "=== BUILD COMPLETE ===" -ForegroundColor Green
Write-Host ""
Write-Host "Built executables:" -ForegroundColor Yellow
Write-Host "• Cobalt Strike (.NET Framework 4.8): .\bin\Release\net48\revtun.exe" -ForegroundColor White
Write-Host "• Linux x64: .\bin\Release\net8.0\linux-x64\publish\revtun" -ForegroundColor White  
Write-Host "• Windows x64: .\bin\Release\net8.0\win-x64\publish\revtun.exe" -ForegroundColor White
Write-Host ""
Write-Host "=== USAGE EXAMPLES ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "Cobalt Strike execute-assembly:" -ForegroundColor Cyan
Write-Host "execute-assembly revtun.exe server --port 1433 --proxy-port 1080" -ForegroundColor White
Write-Host "execute-assembly revtun.exe client --host [target] --port 1433" -ForegroundColor White
Write-Host ""
Write-Host "Linux:" -ForegroundColor Cyan  
Write-Host "./revtun server --port 1433 --proxy-port 1080 --verbose" -ForegroundColor White
Write-Host "./revtun client --host [target] --port 1433 --debug" -ForegroundColor White
