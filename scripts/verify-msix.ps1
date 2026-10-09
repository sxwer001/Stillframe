param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$CertificateThumbprint)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
$signature=Get-AuthenticodeSignature -LiteralPath $Path
if($signature.Status-eq 'HashMismatch' -or !$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint-ne $CertificateThumbprint){throw 'MSIX publisher/hash mismatch.'}
$zip=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Path))
function Read-Entry([string]$Name){
    $entry=$zip.GetEntry($Name)
    if(!$entry){throw "Missing package entry: $Name"}
    $stream=$entry.Open();$memory=[IO.MemoryStream]::new()
    try{$stream.CopyTo($memory);return ,$memory.ToArray()}finally{$stream.Dispose();$memory.Dispose()}
}
try{
    $encoded=Read-Entry 'AppxSignature.p7x'
    if([Text.Encoding]::ASCII.GetString($encoded,0,4)-ne 'PKCX'){throw 'Invalid MSIX signature header.'}
    $cms=[Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]$encoded[4..($encoded.Length-1)])
    $cms.CheckSignature($true) # Cryptographic integrity, independent of machine certificate trust.
    if($cms.SignerInfos.Count-ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint-ne $CertificateThumbprint){throw 'CMS signer mismatch.'}
    $blockmapBytes=Read-Entry 'AppxBlockMap.xml'
    # The signed indirect data contains the AXBM digest, binding the map to the publisher signature.
    $signed=$cms.ContentInfo.Content
    $marker=[Text.Encoding]::ASCII.GetBytes('AXBM');$markerIndex=-1
    for($i=0;$i-le $signed.Length-36;$i++){
        if($signed[$i]-eq $marker[0]-and $signed[$i+1]-eq $marker[1]-and $signed[$i+2]-eq $marker[2]-and $signed[$i+3]-eq $marker[3]){$markerIndex=$i;break}
    }
    if($markerIndex-lt 0){throw 'Signed block map digest missing.'}
    $mapHash=[Security.Cryptography.SHA256]::HashData($blockmapBytes)
    if([Convert]::ToHexString([byte[]]$signed[($markerIndex+4)..($markerIndex+35)])-ne [Convert]::ToHexString($mapHash)){throw 'Block map digest differs from the signed digest.'}
    [xml]$map=[Text.Encoding]::UTF8.GetString($blockmapBytes)
    if($map.BlockMap.HashMethod-ne 'http://www.w3.org/2001/04/xmlenc#sha256'){throw 'Unsupported MSIX block hash algorithm.'}
    $files=0;$blocks=0
    foreach($file in $map.BlockMap.File){
        $entry=$zip.GetEntry($file.Name.Replace('\','/'))
        if(!$entry-or $entry.Length-ne [long]$file.Size){throw "Missing/truncated payload: $($file.Name)"}
        $stream=$entry.Open();$buffer=[byte[]]::new(65536)
        try{
            foreach($block in $file.Block){
                $count=0
                while($count-lt $buffer.Length){$read=$stream.Read($buffer,$count,$buffer.Length-$count);if($read-eq 0){break};$count+=$read}
                $hasher=[Security.Cryptography.SHA256]::Create()
                try{$hash=[Convert]::ToBase64String($hasher.ComputeHash($buffer,0,$count))}finally{$hasher.Dispose()}
                if($hash-ne $block.Hash){throw "Payload block hash mismatch: $($file.Name)"};$blocks++
            }
            if($stream.ReadByte()-ne -1){throw 'Unexpected payload data after block map.'}
        }finally{$stream.Dispose()};$files++
    }
    [ordered]@{cmsSignatureVerified=$true;signedBlockMapVerified=$true;payloadFilesVerified=$files;payloadBlocksVerified=$blocks;machineTrustStatus=[string]$signature.Status}|ConvertTo-Json
}finally{$zip.Dispose()}
