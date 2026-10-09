$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$published=Join-Path $projectRoot 'artifacts/Stillframe-win-x64/Stillframe.exe'
$debug=Join-Path $projectRoot 'src/Stillframe.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Stillframe.exe'
$release=Join-Path $projectRoot 'src/Stillframe.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Stillframe.exe'
$executable=@($published,$release,$debug) | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
if(!$executable){throw 'No compiled application. Run scripts/build.ps1 -Publish first.'}
Start-Process -FilePath $executable -WindowStyle Hidden
