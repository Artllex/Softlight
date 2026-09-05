param([Parameter(Mandatory=$true)][string]$IsccPath)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$version = '2.1.0'
New-Item -ItemType Directory -Force dist | Out-Null
& $IsccPath installer/Softlight.iss
if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
Compress-Archive -Path Softlight.exe,Softlight.exe.config,NocnyFiltr.Engine.dll,Softlight.FirefoxHost.exe,assets,firefox,Register-Firefox.ps1,LICENSE,README.md,FIREFOX.md -DestinationPath "dist/Softlight-$version-Portable-x64.zip" -Force
Compress-Archive -Path firefox/* -DestinationPath 'dist/Softlight-Firefox-Extension-0.1.1.zip' -Force
Get-ChildItem dist -File | Where-Object Extension -in '.exe','.zip' | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content dist/SHA256SUMS.txt -Encoding ASCII
