$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force dist | Out-Null
Compress-Archive -Path Softlight.exe,Softlight.exe.config,NocnyFiltr.Engine.dll,Softlight.FirefoxHost.exe,assets,firefox,Register-Firefox.ps1,LICENSE,FIREFOX.md -DestinationPath dist/Softlight-Firefox-2.1.0.zip -Force
Get-FileHash dist/Softlight-Firefox-2.1.0.zip | Select-Object Hash | Format-List
