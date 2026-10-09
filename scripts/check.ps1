$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$localSdk=Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$sdk=if(Test-Path -LiteralPath $localSdk){$localSdk}else{(Get-Command dotnet -ErrorAction Stop).Source}
Push-Location $projectRoot
try {
    & $sdk run --project tests/Stillframe.Core.Checks/Stillframe.Core.Checks.csproj
    if($LASTEXITCODE -ne 0){throw 'Core checks failed.'}
}finally{Pop-Location}
