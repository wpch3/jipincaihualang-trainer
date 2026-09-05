# Run from the repo root on Windows with .NET 6 SDK installed:
#   powershell -ExecutionPolicy Bypass -File QteTrainer\build-locally.ps1
$ErrorActionPreference = "Stop"
dotnet build "QteTrainer\QteTrainer.csproj" -c Release
$out = "QteTrainer\bin\Release\net6.0\QteTrainer.dll"
Write-Host "Built: $out"
Write-Host "Copy it to BepInEx\plugins\QteTrainer\ (or xmod folder if you keep this alongside FlowerPicker)."
