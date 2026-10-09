param([switch]$SkipBuild,[switch]$Replace,[string]$CertificateThumbprint)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$prefix=$root.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
if(!$SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1') -Publish}
$sdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$sdk=Get-ChildItem -LiteralPath $sdkRoot -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'x64/makeappx.exe')} | Sort-Object Name -Descending | Select-Object -First 1
if(!$sdk){throw 'Install Windows SDK with MakeAppx and SignTool first.'}
$makeappx=Join-Path $sdk.FullName 'x64/makeappx.exe'
$signtool=Join-Path $sdk.FullName 'x64/signtool.exe'
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src/Stillframe.App/Stillframe.App.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Unexpected application version.'}
$published=Join-Path $root 'artifacts/Stillframe-win-x64'
foreach($file in @('Stillframe.exe','Stillframe.pri','App.xbf','LICENSE','ThirdPartyNotices.txt')){
    if(!(Test-Path -LiteralPath (Join-Path $published $file))){throw "Missing publish input: $file"}
}
$releases=Join-Path $root 'artifacts/releases'
$name="Stillframe-$version-win-x64"
$package=Join-Path $releases "$name.msix"
if((Test-Path -LiteralPath $package)-and !$Replace){throw 'MSIX exists; use -Replace after verifying current source.'}
if($CertificateThumbprint){$cert=Get-Item -LiteralPath "Cert:/CurrentUser/My/$CertificateThumbprint"}
else{
    # Reuse the publisher certificate across upgrades; never export its private key.
    $cert=Get-ChildItem Cert:/CurrentUser/My | Where-Object {$_.Subject-eq 'CN=Stillframe' -and $_.HasPrivateKey -and $_.NotAfter-gt (Get-Date).AddDays(30)} | Sort-Object NotAfter -Descending | Select-Object -First 1
    if(!$cert){$cert=New-SelfSignedCertificate -Type Custom -Subject 'CN=Stillframe' -FriendlyName 'Stillframe MSIX trial signing' -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -CertStoreLocation Cert:/CurrentUser/My -NotAfter (Get-Date).AddYears(2) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')}
}
if($cert.Subject-ne 'CN=Stillframe' -or !$cert.HasPrivateKey){throw 'Publisher certificate must be CN=Stillframe with a local private key.'}
$work=Join-Path $root ('artifacts/package-work/'+[Guid]::NewGuid().ToString('N'))
$stage=Join-Path $work 'msix'
New-Item -ItemType Directory -Path $stage,$releases -Force | Out-Null
try{
    foreach($file in Get-ChildItem -LiteralPath $published -Recurse -File){
        if($file.Extension-eq '.pdb'){continue}
        if($file.Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Linked input is not allowed.'}
        $destination=Join-Path $stage ([IO.Path]::GetRelativePath($published,$file.FullName))
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $root 'packaging/Assets') -Destination $stage -Recurse
    [xml]$manifest=Get-Content -LiteralPath (Join-Path $root 'packaging/AppxManifest.xml') -Raw
    $manifest.Package.Identity.Version="$version.0"
    $manifest.Save((Join-Path $stage 'AppxManifest.xml'))
    # Packaged MRT looks up resources.pri, while the portable host uses Stillframe.pri.
    Copy-Item -LiteralPath (Join-Path $stage 'Stillframe.pri') -Destination (Join-Path $stage 'resources.pri')
    $temporary=Join-Path $work "$name.msix"
    & $makeappx pack /d $stage /p $temporary /o
    if($LASTEXITCODE-ne 0){throw 'MakeAppx validation/packing failed.'}
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint /s My $temporary
    if($LASTEXITCODE-ne 0){throw 'MSIX signing failed.'}
    $signature=Get-AuthenticodeSignature -LiteralPath $temporary
    # Windows trust is intentionally not modified here. A self-signed chain is untrusted until the installer trusts the public certificate.
    if(!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint-ne $cert.Thumbprint){throw ('MSIX publisher verification failed: '+$signature.Status)}
    & (Join-Path $PSScriptRoot 'verify-msix.ps1') -Path $temporary -CertificateThumbprint $cert.Thumbprint
    [IO.File]::Move($temporary,$package,$true)
    $cer=Join-Path $releases 'Stillframe-Trial.cer'
    Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
    $publicCert=[Security.Cryptography.X509Certificates.X509Certificate2]::new($cer)
    if($publicCert.HasPrivateKey){throw 'Public release certificate unexpectedly contains a private key.'}
    $fingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($publicCert.RawData))
    $install=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'install-trial.ps1')).Replace('__CERT_SHA256__',$fingerprint).Replace('__PACKAGE_NAME__',"$name.msix")
    [IO.File]::WriteAllText((Join-Path $releases 'Install-Stillframe.ps1'),$install,[Text.UTF8Encoding]::new($true))
    $hash=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name.msix" | Set-Content -LiteralPath "$package.sha256" -Encoding ascii
    [ordered]@{package="artifacts/releases/$name.msix";bytes=(Get-Item $package).Length;sha256=$hash;certificateSha256=$fingerprint;certificateExpires=$cert.NotAfter.ToString('yyyy-MM-dd');signatureStatus=[string]$signature.Status;privateKeyExported=$false;installed=$false}|ConvertTo-Json
}finally{
    $checked=[IO.Path]::GetFullPath($work)
    if(!$checked.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup path outside workspace.'}
    if(Test-Path -LiteralPath $checked){
        if(Get-ChildItem -LiteralPath $checked -Recurse -Force | Where-Object {$_.Attributes-band [IO.FileAttributes]::ReparsePoint}){throw 'Linked staging cannot be cleaned.'}
        Remove-Item -LiteralPath $checked -Recurse -Force
    }
}
