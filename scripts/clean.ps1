[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='Medium')]
param()
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$projectPrefix=$projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
$targets=[Collections.Generic.List[string]]::new()
foreach($relative in @('src/Stillframe.App/bin','src/Stillframe.App/obj','src/Stillframe.Core/bin','src/Stillframe.Core/obj','tests/Stillframe.Core.Checks/bin','tests/Stillframe.Core.Checks/obj','artifacts/package-work','artifacts/package-verification')){
    $targets.Add((Join-Path $projectRoot $relative))
}
$validation=Join-Path $projectRoot 'artifacts/validation'
if(Test-Path -LiteralPath $validation){
    $parents=@($validation)+@(Get-ChildItem -LiteralPath $validation -Directory | Select-Object -ExpandProperty FullName)
    foreach($parent in $parents){
        foreach($name in @('ui-smoke-data','api-check-data','home-api-check-data','bing-archive-check-data')){$targets.Add((Join-Path $parent $name))}
    }
}
$removed=[Collections.Generic.List[object]]::new()
[long]$reclaimedBytes=0
foreach($target in $targets){
    if(!(Test-Path -LiteralPath $target)){continue}
    $absolute=[IO.Path]::GetFullPath((Resolve-Path -LiteralPath $target).Path)
    if(!$absolute.StartsWith($projectPrefix,[StringComparison]::OrdinalIgnoreCase)){throw "Refusing path outside workspace: $absolute"}
    $entry=Get-Item -LiteralPath $absolute -Force
    if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refusing a linked cleanup target.'}
    $children=@(Get-ChildItem -LiteralPath $absolute -Recurse -Force)
    if($children | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Refusing cleanup containing links.'}
    foreach($process in @(Get-Process Stillframe,Shijing -ErrorAction SilentlyContinue)){
        if($process.Path -and $process.Path.StartsWith($absolute.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Close the application using this cleanup target first.'}
    }
    $bytes=($children | Where-Object {!$_.PSIsContainer} | Measure-Object Length -Sum).Sum
    $relative=[IO.Path]::GetRelativePath($projectRoot,$absolute).Replace('\','/')
    if($PSCmdlet.ShouldProcess($relative,'Remove generated build/test data')){
        Remove-Item -LiteralPath $absolute -Recurse -Force
        $removed.Add([ordered]@{path=$relative;bytes=[long]$bytes})
        $reclaimedBytes+=[long]$bytes
    }
}
[ordered]@{removed=$removed.ToArray();reclaimedBytes=$reclaimedBytes;preserved=@('source/docs/tests','artifacts/releases','artifacts/Stillframe-win-x64','validation reports and screenshots','.tools SDK/feed','user application data and exports')} | ConvertTo-Json -Depth 5
