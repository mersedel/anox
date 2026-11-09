# deploy.ps1 - builds the ANOX server and deploys it to the AWS instance.
# Usage: .\deploy.ps1

Set-Location $PSScriptRoot

Write-Host "1/5 Building..."
dotnet publish ANOX.Server -c Release -r linux-arm64 --self-contained false -o publish\server
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed" -ForegroundColor Red; exit 1 }

Write-Host "2/5 Cleaning the upload folder on the server..."
ssh anox "rm -rf ~/anox-upload"
if ($LASTEXITCODE -ne 0) { Write-Host "Cleanup failed" -ForegroundColor Red; exit 1 }

Write-Host "3/5 Uploading..."
scp -r publish\server anox:~/anox-upload
if ($LASTEXITCODE -ne 0) { Write-Host "Upload failed" -ForegroundColor Red; exit 1 }

Write-Host "4/5 Stopping, installing and starting..."
ssh anox "sudo systemctl stop anox && sudo cp ~/anox-upload/* /opt/anox/ && sudo systemctl start anox"

Write-Host "5/5 Checking..."
Start-Sleep -Seconds 3
ssh anox "systemctl is-active anox"
if ($LASTEXITCODE -ne 0) { Write-Host "Server is not running! Logs: ssh anox journalctl -u anox -n 30" -ForegroundColor Red; exit 1 }

Write-Host "Done!" -ForegroundColor Green