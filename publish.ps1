dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o "$PSScriptRoot\publish"
Copy-Item "$PSScriptRoot\publish\Robo.exe" "C:\APrograms\robo.exe" -Force
Write-Host "Published to C:\APrograms\robo.exe"
