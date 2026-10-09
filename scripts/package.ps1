param([switch]$SkipBuild,[switch]$Replace)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$projectPrefix=$projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
function Assert-WorkspacePath([string]$Path) {
    $absolute=[IO.Path]::GetFullPath($Path)
    if(!$absolute.StartsWith($projectPrefix,[StringComparison]::OrdinalIgnoreCase)){throw "Path outside workspace: $absolute"}
    return $absolute
}
if(!$SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1') -Publish}
[xml]$project=Get-Content -LiteralPath (Join-Path $projectRoot 'src/Stillframe.App/Stillframe.App.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Unexpected application version.'}
$name="Stillframe-$version-win-x64"
$published=Join-Path $projectRoot 'artifacts/Stillframe-win-x64'
$releases=Join-Path $projectRoot 'artifacts/releases'
$archive=Assert-WorkspacePath (Join-Path $releases "$name.zip")
if((Test-Path -LiteralPath $archive) -and !$Replace){throw 'Package already exists. Use -Replace to replace it after verification.'}
foreach($required in @('Stillframe.exe','Stillframe.dll','Stillframe.pri','App.xbf','Stillframe.runtimeconfig.json','LICENSE','ThirdPartyNotices.txt')){
    if(!(Test-Path -LiteralPath (Join-Path $published $required))){throw "Missing runtime/package file: $required"}
}
$work=Assert-WorkspacePath (Join-Path $projectRoot ('artifacts/package-work/'+[Guid]::NewGuid().ToString('N')))
$stage=Join-Path $work $name
$temporaryArchive=Join-Path $work "$name.zip"
New-Item -ItemType Directory -Path $stage,$releases -Force | Out-Null
try {
    foreach($file in Get-ChildItem -LiteralPath $published -Recurse -File){
        if($file.Extension -eq '.pdb'){continue}
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refusing a linked package input.'}
        $relative=[IO.Path]::GetRelativePath($published,$file.FullName)
        $destination=Assert-WorkspacePath (Join-Path $stage $relative)
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    @"
拾景 $version · Windows x64 便携版

解压整个文件夹，双击 Stillframe.exe。不要只复制 exe。
支持 Windows 10 build 19041 或更高版本的 x64 系统。
已包含 .NET 和 Windows App SDK 运行依赖；联网图源需要网络。
应用数据保存在 %LOCALAPPDATA%\Shijing，更新程序时不会删除图库。

首页显示 Bing 每日图；侧栏选择历史图/其他图源；滚轮前后浏览。
右键图片可收藏、保存原图、设为桌面或锁屏；详情更多菜单也可设置锁屏。
自动更换和通知默认关闭，仅在软件运行时按用户设置执行。
软件采用 MIT 许可，图片及依赖各自保留原许可。见 LICENSE 与 ThirdPartyNotices.txt。

此包为本地试用初版，无安装器和自动更新；未代码签名。
在当前开发机验证启动与界面，其他设备兼容性、系统壁纸/锁屏实际效果待验收。
"@ | Set-Content -LiteralPath (Join-Path $stage '使用说明.txt') -Encoding utf8
    $files=@(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
    [ordered]@{product='Stillframe';version=$version;runtime='win-x64';files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'MANIFEST.json') -Encoding utf8
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$temporaryArchive,[IO.Compression.CompressionLevel]::Optimal,$true)
    $zip=[IO.Compression.ZipFile]::OpenRead($temporaryArchive)
    try {
        if($zip.Entries.Count -ne $files.Count+1){throw 'Archive file count does not match manifest.'}
        foreach($file in $files){
            $entry=$zip.GetEntry("$name/$($file.path)")
            if(!$entry -or $entry.Length -ne $file.bytes){throw "Archive entry is missing/truncated: $($file.path)"}
            $stream=$entry.Open();$hasher=[Security.Cryptography.SHA256]::Create()
            try{$hash=[Convert]::ToHexString($hasher.ComputeHash($stream)).ToLowerInvariant()}finally{$stream.Dispose();$hasher.Dispose()}
            if($hash -ne $file.sha256){throw "Archive checksum mismatch: $($file.path)"}
        }
    }finally{$zip.Dispose()}
    [IO.File]::Move($temporaryArchive,$archive,$true)
    $archiveHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$archiveHash  $name.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
    [ordered]@{package="artifacts/releases/$name.zip";version=$version;files=$files.Count+1;bytes=(Get-Item -LiteralPath $archive).Length;sha256=$archiveHash;archiveEntriesVerified=$true;signed=$false} | ConvertTo-Json
} finally {
    $checkedWork=Assert-WorkspacePath $work
    if(Test-Path -LiteralPath $checkedWork){
        if(Get-ChildItem -LiteralPath $checkedWork -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Refusing to clean linked package staging.'}
        Remove-Item -LiteralPath $checkedWork -Recurse -Force
    }
}
