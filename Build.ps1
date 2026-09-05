param([string]$BuildDirectory = (Join-Path $env:TEMP 'NocnyFiltr-build'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
New-Item -ItemType Directory -Force -Path $BuildDirectory | Out-Null
$BuildDirectory = (Resolve-Path -LiteralPath $BuildDirectory).Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Zainstaluj Visual Studio Build Tools z narzędziami C++ oraz Windows SDK.' }
$msvc = (Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdkVersion = (Get-ChildItem -LiteralPath (Join-Path $sdk 'Include') -Directory | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:INCLUDE = "$msvc\include;$sdk\Include\$sdkVersion\ucrt;$sdk\Include\$sdkVersion\shared;$sdk\Include\$sdkVersion\um;$sdk\Include\$sdkVersion\winrt"
$env:LIB = "$msvc\lib\x64;$sdk\Lib\$sdkVersion\ucrt\x64;$sdk\Lib\$sdkVersion\um\x64"
$fxc = "$sdk\bin\$sdkVersion\x64\fxc.exe"
& $fxc /nologo /T vs_4_1 /E VS /O3 /Fh "$BuildDirectory\VertexShader.h" /Vn vertexShader "$root\src\Filter.hlsl"
if ($LASTEXITCODE) { throw 'Błąd kompilacji vertex shadera.' }
& $fxc /nologo /T ps_4_1 /E PS /O3 /Fh "$BuildDirectory\PixelShader.h" /Vn pixelShader "$root\src\Filter.hlsl"
if ($LASTEXITCODE) { throw 'Błąd kompilacji pixel shadera.' }
& "$msvc\bin\Hostx64\x64\cl.exe" /nologo /std:c++17 /O2 /MT /EHsc /W4 /utf-8 /DUNICODE /D_UNICODE /LD /I $BuildDirectory "/Fo$BuildDirectory\Engine.obj" "$root\src\Engine.cpp" /link "/OUT:$root\NocnyFiltr.Engine.dll" "/IMPLIB:$BuildDirectory\NocnyFiltr.Engine.lib" d3d11.lib dxgi.lib dcomp.lib dwmapi.lib gdi32.lib user32.lib ole32.lib
if ($LASTEXITCODE) { throw 'Błąd kompilacji silnika.' }
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$sources = (Get-ChildItem -LiteralPath "$root\src" -Filter '*.cs').FullName
& $csc /nologo /target:winexe "/win32icon:$root\assets\Softlight.ico" /platform:x64 /optimize+ /utf8output "/win32manifest:$root\src\app.manifest" "/out:$root\Softlight.exe" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $sources
if ($LASTEXITCODE) { throw 'Błąd kompilacji interfejsu.' }
Write-Output "Gotowe: $root\Softlight.exe"

if (!(Test-Path "$root\Softlight.FirefoxHost.exe") -or (Get-Item "$root\bridge\Host.cs").LastWriteTimeUtc -gt (Get-Item "$root\Softlight.FirefoxHost.exe").LastWriteTimeUtc) {
& $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$root\Softlight.FirefoxHost.exe" /r:System.dll /r:System.Core.dll "$root\bridge\Host.cs"
if ($LASTEXITCODE) { throw "Firefox host compilation failed" }
}