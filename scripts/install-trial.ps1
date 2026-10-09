# Generated release copy pins the public certificate and package filename.
# Execute manually in an elevated Windows PowerShell terminal after reviewing the release.
#requires -Version 5.1
#requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$package=Join-Path $PSScriptRoot '__PACKAGE_NAME__'
$certificate=Join-Path $PSScriptRoot 'Stillframe-Trial.cer'
if(!(Test-Path -LiteralPath $package)-or !(Test-Path -LiteralPath $certificate)){throw 'Download the MSIX and public CER into the same folder as this script.'}
$cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate)
$sha=[Security.Cryptography.SHA256]::Create()
try{$fingerprint=([BitConverter]::ToString($sha.ComputeHash($cert.RawData))).Replace('-','')}finally{$sha.Dispose()}
if($cert.Subject-ne 'CN=Stillframe' -or $fingerprint-ne '__CERT_SHA256__' -or $cert.HasPrivateKey){throw 'Public certificate does not match this release.'}
$signature=Get-AuthenticodeSignature -LiteralPath $package
if(!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint-ne $cert.Thumbprint -or $signature.Status-eq 'HashMismatch'){throw 'Package signature does not match the expected publisher.'}
Write-Host "Publisher: $($cert.Subject)"
Write-Host "Certificate SHA256: $fingerprint"
Write-Host 'This trial requires trusting this publisher on this computer. Trust is not removed automatically when uninstalling the app.'
if((Read-Host 'Type INSTALL to trust this certificate and install for the current user')-cne 'INSTALL'){Write-Host 'Cancelled';return}
Import-Certificate -FilePath $certificate -CertStoreLocation Cert:/LocalMachine/TrustedPeople | Out-Null
# Add-AppxPackage validates the complete signed payload against the trusted certificate.
Add-AppxPackage -Path $package
Write-Host 'Installed: 拾景 · Stillframe. Open it from the Start menu.'
