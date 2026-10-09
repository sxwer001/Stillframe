$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$published=Join-Path $root 'artifacts/Stillframe-win-x64'
$assets=Get-Content -LiteralPath (Join-Path $root 'src/Stillframe.App/obj/project.assets.json') -Raw | ConvertFrom-Json
$cacheRoots=@($assets.packageFolders.PSObject.Properties.Name)
$destination=Join-Path $published 'ThirdPartyLicenses'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'packaging/ThirdPartyLicenses/SQLitePCLRaw-LICENSE.txt') -Destination $destination
Copy-Item -LiteralPath (Join-Path $root 'packaging/ThirdPartyLicenses/Microsoft.Data.Sqlite-LICENSE.txt') -Destination $destination
$index=[Collections.Generic.List[object]]::new()
foreach($dependency in $assets.libraries.PSObject.Properties){
    if($dependency.Value.type-ne 'package'){continue}
    $folder=$cacheRoots | ForEach-Object {Join-Path $_ $dependency.Value.path} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
    if(!$folder){throw "Missing resolved dependency: $($dependency.Name)"}
    $legal=@($dependency.Value.files | Where-Object {$_-match '(?i)(licen[cs]e|notice|copying)'})
    $slug=$dependency.Name.Replace('/','-')
    $copied=@()
    foreach($file in $legal){
        $name=$slug+'-'+$file.Replace('/','-')
        Copy-Item -LiteralPath (Join-Path $folder $file) -Destination (Join-Path $destination $name)
        $copied+=$name
    }
    $nuspec=Get-ChildItem -LiteralPath $folder -File -Filter '*.nuspec' | Select-Object -First 1
    [xml]$metadata=Get-Content -LiteralPath $nuspec.FullName -Raw
    $index.Add([ordered]@{package=$dependency.Name;license=[string]$metadata.package.metadata.license.InnerText;licenseUrl=[string]$metadata.package.metadata.licenseUrl;notices=$copied})
}
# Retain the exact self-contained .NET runtime's included notices.
$runtime=(Get-Content -LiteralPath (Join-Path $published 'Stillframe.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks | Where-Object {$_.name-eq 'Microsoft.NETCore.App'}
$runtimeRoot=$cacheRoots | ForEach-Object {Join-Path $_ ('microsoft.netcore.app.runtime.win-x64/'+$runtime.version)} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
if(!$runtimeRoot){throw 'Resolved self-contained .NET runtime license input is missing.'}
foreach($file in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){
    if(!(Test-Path -LiteralPath (Join-Path $runtimeRoot $file))){throw "Missing .NET notice: $file"}
    Copy-Item -LiteralPath (Join-Path $runtimeRoot $file) -Destination (Join-Path $destination ('NET-'+$runtime.version+'-'+$file))
}
$index | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'dependencies.json') -Encoding utf8
Write-Output ('Dependency notices collected: '+(Get-ChildItem -LiteralPath $destination -File).Count)
