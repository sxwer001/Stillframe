param([ValidateSet('Debug','Release')][string]$Configuration='Release',[switch]$Publish)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$localSdk=Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$sdk=if(Test-Path -LiteralPath $localSdk){$localSdk}else{(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_HTTP2SUPPORT='false'
# Ensure an empty cache source exists on fresh checkouts.
New-Item -ItemType Directory -Path (Join-Path $projectRoot '.tools/localfeed') -Force | Out-Null
Push-Location $projectRoot
try {
    & $sdk restore src/Stillframe.App/Stillframe.App.csproj --locked-mode --disable-parallel
    if($LASTEXITCODE -ne 0){throw 'NuGet restore failed. Check network access to the configured Microsoft feed.'}
    if($Publish){& $sdk publish src/Stillframe.App/Stillframe.App.csproj -c $Configuration --no-restore -o artifacts/Stillframe-win-x64}
    else{& $sdk build src/Stillframe.App/Stillframe.App.csproj -c $Configuration --no-restore}
    if($LASTEXITCODE -ne 0){throw 'Build failed.'}
    if($Publish){& (Join-Path $PSScriptRoot 'collect-notices.ps1')}
}finally{Pop-Location}
