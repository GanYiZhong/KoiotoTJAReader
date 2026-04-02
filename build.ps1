param(
    [string]$Config = "Debug"
)

# Kill Koioto if running
$koioto = Get-Process -Name "Koioto" -ErrorAction SilentlyContinue
if ($koioto) {
    Write-Host "Stopping Koioto (PID $($koioto.Id))..."
    Stop-Process -Id $koioto.Id -Force
    Start-Sleep -Milliseconds 500
}

# Build
Write-Host "Building TJAReader ($Config)..."
dotnet build TJAReader.csproj -c $Config

if ($LASTEXITCODE -eq 0) {
    Write-Host "Done. TJAReader.dll -> ..\Plugins\TJAReader.dll"
} else {
    Write-Host "Build failed." -ForegroundColor Red
    exit 1
}
