namespace Stillframe.Core;

public sealed record WallpaperItem(string Id, string Title, string SourceId, string SourceName,
    string? OriginalUrl, string? OriginalFile, string? ThumbnailFile, string? Sha256,
    int Width, int Height, string? Author, string? SourcePage, bool Favorite, DateTimeOffset AddedAt,
    string? LicenseName=null,string? LicenseUrl=null,string? PreviewUrl=null)
{
    public string Dimensions => Width > 0 ? $"{Width} × {Height}" : "尺寸待下载核实";
}
public sealed record SourceDefinition(string Id, string Name, string Kind, string Address, bool Enabled);
public sealed record ImageInfo(int Width, int Height, string Extension);
public sealed record TransferProgress(long Received, long? Total, string Stage)
{
    public double? Percentage => Total > 0 ? Math.Min(99, Received * 100d / Total.Value) : null;
}
public interface IImageProcessor
{
    Task<ImageInfo> ValidateAsync(string path, CancellationToken token);
    Task CreateThumbnailAsync(string original, string destination, CancellationToken token);
}
public sealed class AppException(string message) : Exception(message);
