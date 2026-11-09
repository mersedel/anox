# deploy.ps1 - builds the ANOX server and deploys it to the AWS instance.
# Usage: .\deploy.ps1

Set-Location $PSScriptRoot

Write-Host "1/5 Building..."
# dotnet publish doesn't clear the output folder, so remove leftovers of earlier builds
if (Test-Path publish\server) { Remove-Item publish\server -Recurse -Force }
dotnet publish ANOX.Server -c Release -r linux-arm64 --self-contained false -o publish\server
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed" -ForegroundColor Red; exit 1 }

Write-Host "2/5 Cleaning the upload folder on the server..."
ssh anox "rm -rf ~/anox-upload"
if ($LASTEXITCODE -ne 0) { Write-Host "Cleanup failed" -ForegroundColor Red; exit 1 }

Write-Host "3/5 Uploading..."
scp -r publish\server anox:~/anox-upload
if ($LASTEXITCODE -ne 0) { Write-Host "Upload failed" -ForegroundColor Red; exit 1 }

Write-Host "4/5 Stopping, installing and starting..."
# /opt/anox holds only the build output (the config is in /etc/anox), so it is emptied
# first: files that are no longer part of the build must not stay behind.
ssh anox "sudo systemctl stop anox && sudo find /opt/anox -mindepth 1 -delete && sudo cp -r ~/anox-upload/. /opt/anox/ && sudo systemctl start anox"
if ($LASTEXITCODE -ne 0) { Write-Host "Install failed, the server may be stopped! Check: ssh anox systemctl status anox" -ForegroundColor Red; exit 1 }

Write-Host "5/5 Checking..."
Start-Sleep -Seconds 3
ssh anox "systemctl is-active anox"
if ($LASTEXITCODE -ne 0) { Write-Host "Server is not running! Logs: ssh anox journalctl -u anox -n 30" -ForegroundColor Red; exit 1 }

Write-Host "Done!" -ForegroundColor Green