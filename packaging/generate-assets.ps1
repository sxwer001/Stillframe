$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$destination=Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
function Rounded([Drawing.Graphics]$Graphics,[Drawing.Brush]$Brush,[single]$X,[single]$Y,[single]$Width,[single]$Height,[single]$Radius){
    $path=[Drawing.Drawing2D.GraphicsPath]::new()
    try{
        $d=$Radius*2
        $path.AddArc($X,$Y,$d,$d,180,90);$path.AddArc($X+$Width-$d,$Y,$d,$d,270,90)
        $path.AddArc($X+$Width-$d,$Y+$Height-$d,$d,$d,0,90);$path.AddArc($X,$Y+$Height-$d,$d,$d,90,90)
        $path.CloseFigure();$Graphics.FillPath($Brush,$path)
    }finally{$path.Dispose()}
}
# Original album-cover artwork. Supersampling preserves rounded shapes at 44 px.
$master=[Drawing.Bitmap]::new(1024,1024)
$g=[Drawing.Graphics]::FromImage($master)
try{
    $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)
    $tile=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(70,70,884,884),[Drawing.Color]::FromArgb(47,76,81),[Drawing.Color]::FromArgb(19,39,48),[single]90)
    Rounded $g $tile 70 70 884 884 190;$tile.Dispose()
    $shadow=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(65,0,0,0))
    Rounded $g $shadow 193 208 662 662 48;$shadow.Dispose()
    $back=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(157,183,177))
    $state=$g.Save();$g.TranslateTransform(512,512);$g.RotateTransform(-9);Rounded $g $back -307 -287 632 632 34;$g.Restore($state);$back.Dispose()
    $paper=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(250,243,224))
    $state=$g.Save();$g.TranslateTransform(512,512);$g.RotateTransform(5);Rounded $g $paper -309 -318 630 646 34;$g.Restore($state)
    Rounded $g $paper 210 190 630 654 36;$paper.Dispose()
    $sky=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(246,226,558,508),[Drawing.Color]::FromArgb(85,128,145),[Drawing.Color]::FromArgb(211,182,132),[single]90)
    Rounded $g $sky 246 226 558 508 16;$sky.Dispose()
    $sun=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,229,164));$g.FillEllipse($sun,610,304,92,92);$sun.Dispose()
    $far=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(109,145,139))
    $g.FillPolygon($far,[Drawing.PointF[]]@([Drawing.PointF]::new(246,636),[Drawing.PointF]::new(388,428),[Drawing.PointF]::new(553,637),[Drawing.PointF]::new(672,498),[Drawing.PointF]::new(804,637),[Drawing.PointF]::new(804,718),[Drawing.PointF]::new(246,718)));$far.Dispose()
    $near=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(44,88,93))
    $g.FillPolygon($near,[Drawing.PointF[]]@([Drawing.PointF]::new(246,657),[Drawing.PointF]::new(365,559),[Drawing.PointF]::new(523,695),[Drawing.PointF]::new(655,594),[Drawing.PointF]::new(804,682),[Drawing.PointF]::new(804,732),[Drawing.PointF]::new(246,732)));$near.Dispose()
    $caption=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(198,163,99))
    Rounded $g $caption 430 780 190 12 6;$caption.Dispose()
    $spine=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(85,129,128));Rounded $g $spine 116 279 18 466 9;$spine.Dispose()
    $master.Save((Join-Path $destination 'AppIcon.png'),[Drawing.Imaging.ImageFormat]::Png)
}finally{$g.Dispose()}
try{
    foreach($spec in @(@('StoreLogo.png',50),@('Square44x44Logo.png',44),@('Square150x150Logo.png',150))){
        $size=[int]$spec[1];$bitmap=[Drawing.Bitmap]::new($size,$size);$g=[Drawing.Graphics]::FromImage($bitmap)
        try{$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality;$g.DrawImage($master,[Drawing.Rectangle]::new(0,0,$size,$size));$bitmap.Save((Join-Path $destination $spec[0]),[Drawing.Imaging.ImageFormat]::Png)}finally{$g.Dispose();$bitmap.Dispose()}
    }
    $bitmap=[Drawing.Bitmap]::new(256,256);$g=[Drawing.Graphics]::FromImage($bitmap)
    try{$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$g.DrawImage($master,[Drawing.Rectangle]::new(0,0,256,256));$buffer=[IO.MemoryStream]::new();$bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png);$png=$buffer.ToArray();$buffer.Dispose()}finally{$g.Dispose();$bitmap.Dispose()}
    $stream=[IO.File]::Create((Join-Path $destination 'App.ico'));$writer=[IO.BinaryWriter]::new($stream)
    try{$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]1);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$png.Length);$writer.Write([uint32]22);$writer.Write($png)}finally{$writer.Dispose()}
}finally{$master.Dispose()}
