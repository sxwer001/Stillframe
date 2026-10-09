using Stillframe.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace Stillframe.App.Services;

public sealed class WicImageProcessor : IImageProcessor
{
    public async Task<ImageInfo> ValidateAsync(string path,CancellationToken token)
    {
        try
        {
            var file=await StorageFile.GetFileFromPathAsync(path);using var stream=await file.OpenReadAsync();
            var decoder=await BitmapDecoder.CreateAsync(stream);token.ThrowIfCancellationRequested();
            var format=decoder.DecoderInformation.CodecId;
            var extension=format==BitmapDecoder.JpegDecoderId?".jpg":format==BitmapDecoder.PngDecoderId?".png":format==BitmapDecoder.BmpDecoderId?".bmp":null;
            if(extension==null)throw new AppException("初版仅支持JPEG、PNG和BMP静态图片。");
            if(decoder.FrameCount!=1)throw new AppException("不支持多帧或动态图片。");
            if(decoder.PixelWidth==0||decoder.PixelHeight==0||(long)decoder.PixelWidth*decoder.PixelHeight>60_000_000)throw new AppException("图片尺寸为空或超过6000万像素。");
            // Decode the entire frame to reject truncated or malformed originals.
            _=await decoder.GetPixelDataAsync();token.ThrowIfCancellationRequested();
            return new((int)decoder.OrientedPixelWidth,(int)decoder.OrientedPixelHeight,extension);
        }
        catch(COMException){throw new AppException("图片无法解码或文件已损坏。");}
    }
    public async Task CreateThumbnailAsync(string original,string destination,CancellationToken token)
    {
        var temporary=destination+".part";
        try
        {
            var file=await StorageFile.GetFileFromPathAsync(original);using var stream=await file.OpenReadAsync();var decoder=await BitmapDecoder.CreateAsync(stream);
            var ratio=Math.Min(1d,640d/Math.Max(decoder.OrientedPixelWidth,decoder.OrientedPixelHeight));
            var width=Math.Max(1,(uint)(decoder.OrientedPixelWidth*ratio));var height=Math.Max(1,(uint)(decoder.OrientedPixelHeight*ratio));
            var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new BitmapTransform{ScaledWidth=width,ScaledHeight=height},ExifOrientationMode.RespectExifOrientation,ColorManagementMode.ColorManageToSRgb);
            token.ThrowIfCancellationRequested();using var output=await FileRandomAccessStream.OpenAsync(temporary,FileAccessMode.ReadWrite,StorageOpenOptions.None,FileOpenDisposition.CreateAlways);
            var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,output);encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,width,height,96,96,pixels.DetachPixelData());await encoder.FlushAsync();output.Dispose();
            token.ThrowIfCancellationRequested();File.Move(temporary,destination,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
