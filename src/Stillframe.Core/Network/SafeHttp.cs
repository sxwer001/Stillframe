using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Stillframe.Core.Network;

public sealed class SafeHttp : IDisposable
{
    private readonly HttpClient client;
    public SafeHttp()
    {
        var handler=new SocketsHttpHandler { AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(20),UseProxy=false };
        handler.ConnectCallback=async (context,token)=>
        {
            var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,token);
            if(addresses.Length==0||addresses.Any(a=>!IsPublicAddress(a)))throw new AppException("在线图源不能访问本机或私有网络地址。");
            foreach(var address in addresses)
            {
                var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);
                try {await socket.ConnectAsync(new IPEndPoint(address,context.DnsEndPoint.Port),token);return new NetworkStream(socket,true);}
                catch {socket.Dispose();if(token.IsCancellationRequested)throw;}
            }
            throw new AppException("无法连接图源，请检查网络。");
        };
        client=new(handler){Timeout=TimeSpan.FromMinutes(3)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Stillframe/0.1 (Windows static wallpaper)");
    }
    public static bool IsPublicAddress(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
        if(IPAddress.IsLoopback(address)||address.Equals(IPAddress.Any)||address.Equals(IPAddress.IPv6Any))return false;
        var bytes=address.GetAddressBytes();
        if(bytes.Length==4)return bytes[0] is not (0 or 10 or 127) && !(bytes[0]==169&&bytes[1]==254) && !(bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31) && !(bytes[0]==192&&bytes[1]==168) && !(bytes[0]==100&&bytes[1]>=64&&bytes[1]<=127) && bytes[0]<224;
        return !address.IsIPv6LinkLocal&&!address.IsIPv6Multicast&&!address.IsIPv6SiteLocal&&(bytes[0]&0xfe)!=0xfc;
    }
    public static Uri ValidateUri(string input)
    {
        if(!Uri.TryCreate(input,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||!string.IsNullOrEmpty(uri.UserInfo)||uri.Port!=443)throw new AppException("请输入不含账号密码的标准 HTTPS 地址（端口443）。");
        if(uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||(IPAddress.TryParse(uri.Host,out var ip)&&!IsPublicAddress(ip)))throw new AppException("在线图源不能访问本机或私有网络地址。");
        return uri;
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri,CancellationToken token)
    {
        for(var i=0;i<6;i++)
        {
            ValidateUri(uri.AbsoluteUri);HttpResponseMessage response;
            try{response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);}
            catch(HttpRequestException) when(!token.IsCancellationRequested)
            {
                // One bounded retry for a failed idempotent GET, retaining TLS/DNS validation.
                await Task.Delay(350,token);response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
            }
            if((int)response.StatusCode is >=300 and <400)
            {
                var location=response.Headers.Location;response.Dispose();if(location==null)throw new AppException("图源重定向缺少目标地址。");uri=new Uri(uri,location);continue;
            }
            if(response.StatusCode==HttpStatusCode.TooManyRequests){response.Dispose();throw new AppException("图源请求过于频繁，请稍后重试。");}
            if(!response.IsSuccessStatusCode){var status=(int)response.StatusCode;response.Dispose();throw new AppException($"图源返回 HTTP {status}，请检查地址或稍后重试。");}
            return response;
        }
        throw new AppException("图源重定向过多。");
    }
    public async Task<string> ReadTextAsync(string uri,CancellationToken token)
    {
        using var response=await GetAsync(ValidateUri(uri),token);using var result=new MemoryStream();
        await CopyLimitedAsync(response,result,2*1024*1024,null,token);return Encoding.UTF8.GetString(result.ToArray());
    }
    public async Task DownloadAsync(string uri,string destination,IProgress<TransferProgress>? progress,CancellationToken token,long limit=50L*1024*1024)
    {
        using var response=await GetAsync(ValidateUri(uri),token);
        var type=response.Content.Headers.ContentType?.MediaType;
        if(type!=null&&!type.StartsWith("image/",StringComparison.OrdinalIgnoreCase)&&type!="application/octet-stream")throw new AppException("地址返回的不是图片。");
        await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
        await CopyLimitedAsync(response,output,limit,progress,token);
    }
    private static async Task CopyLimitedAsync(HttpResponseMessage response,Stream output,long limit,IProgress<TransferProgress>? progress,CancellationToken token)
    {
        var total=response.Content.Headers.ContentLength;if(total>limit)throw new AppException("文件超过允许大小。");
        await using var input=await response.Content.ReadAsStreamAsync(token);var buffer=new byte[81920];long received=0;
        while(true)
        {
            var read=await input.ReadAsync(buffer,token);if(read==0)break;received+=read;
            if(received>limit)throw new AppException("文件超过允许大小。");await output.WriteAsync(buffer.AsMemory(0,read),token);progress?.Report(new(received,total,"正在下载原图"));
        }
        if(received==0||total.HasValue&&received!=total)throw new AppException("文件为空或下载不完整。");
    }
    public void Dispose()=>client.Dispose();
}
