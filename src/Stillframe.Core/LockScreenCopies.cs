using System.Text.Json;
using System.Security.Cryptography;

namespace Stillframe.Core;

// Only our journal is ownership evidence. Never sweep legacy GUIDs or the user library.
public sealed class LockScreenCopies(string root, Func<string, Task<bool>> setImage,
    Func<Task<string?>> readCurrent, Func<DateTimeOffset>? clock = null)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string folder = Path.Combine(Path.GetFullPath(root), "lockscreen");
    private DateTimeOffset Now => clock?.Invoke() ?? DateTimeOffset.UtcNow;
    private sealed record Copy(string Name, DateTimeOffset Created, bool Confirmed, bool Submitted = false, string? Sha256 = null, long Length = 0);
    private sealed record Journal(int Version, List<Copy> Copies);
    private string JournalPath => Path.Combine(folder, "owned-v1.json");

    public async Task SetAsync(string original, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            var journal = Read();
            if(journal.Copies.Count >= 1000)throw new AppException("锁屏副本尚无法安全回收，请先检查锁屏状态；未修改锁屏。");
            var extension = Path.GetExtension(original).ToLowerInvariant();
            if(extension is not (".jpg" or ".jpeg" or ".png" or ".bmp"))throw new AppException("锁屏仅支持JPEG、PNG或BMP图片。");
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folder); CheckPaths();
            var copy = new Copy("stillframe-" + Guid.NewGuid().ToString("N") + extension, Now, false);
            var target = Path.Combine(folder, copy.Name);
            bool dispatched = false, created = false;
            try
            {
                journal.Copies.Add(copy); Write(journal); // ownership before creating a copy
                await using(var input=File.OpenRead(original))
                await using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                {created=true;await input.CopyToAsync(output,token);}
                token.ThrowIfCancellationRequested();
                var index=journal.Copies.IndexOf(copy);
                string hash;
                await using(var stream=File.OpenRead(target))hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
                journal.Copies[index]=copy=copy with { Submitted=true, Sha256=hash, Length=new FileInfo(target).Length };
                Write(journal); // durable intent before handing the file to Windows
                dispatched = true;
                // Do not cancel an in-flight OS call: its outcome must settle before cleanup.
                if(!await setImage(target))
                {
                    dispatched = Same(target,await CurrentAsync());
                    throw new AppException("Windows拒绝了锁屏设置，请检查个性化权限或组织策略。");
                }
                journal.Copies[journal.Copies.IndexOf(copy)] = copy with { Confirmed = true };
                Write(journal); // failure leaves the durable unconfirmed intent and the file intact
            }
            catch
            {
                if(!dispatched)
                {
                    if(created)TryDelete(target);
                    journal.Copies.Remove(copy);
                    // A failed journal update can leave a missing entry; reconciliation handles it.
                    try { Write(journal); } catch(IOException) { } catch(UnauthorizedAccessException) { }
                }
                throw;
            }
            try { await ReconcileCoreAsync(journal); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or AppException) { /* setting succeeded; keep files on cleanup failure */ }
        }
        finally { Gate.Release(); }
    }

    public async Task ReconcileAsync()
    {
        await Gate.WaitAsync();
        try { await ReconcileCoreAsync(Read()); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or AppException) { /* cleanup is optional */ }
        finally { Gate.Release(); }
    }

    private async Task<string?> CurrentAsync()
    {
        try { var path = await readCurrent(); return path == null ? null : Path.GetFullPath(path); }
        catch { return null; } // unsupported / stream-based / policy-controlled: not evidence of release
    }
    private async Task ReconcileCoreAsync(Journal journal)
    {
        if(journal.Copies.Count == 0)return;
        var current = await CurrentAsync();
        if(current!=null)
        {
            bool changed=false;
            foreach(var copy in journal.Copies.Where(x=>!x.Submitted).ToArray())
            {
                var path=Path.Combine(folder,copy.Name);
                CheckPaths();
                if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                if(!Same(path,current)&&await MatchesHashAsync(copy,path)&&TryDelete(path)){journal.Copies.Remove(copy);changed=true;}
            }
            if(changed)Write(journal);
        }
        var currentCopy = journal.Copies.FirstOrDefault(x => Same(Path.Combine(folder, x.Name), current));
        if(currentCopy == null || !File.Exists(current))return;
        if(!currentCopy.Confirmed)
        {
            journal.Copies[journal.Copies.IndexOf(currentCopy)] = currentCopy = currentCopy with { Confirmed = true, Submitted = true };
            Write(journal);
        }
        var previous = journal.Copies.LastOrDefault(x => x.Confirmed && x.Name != currentCopy.Name);
        foreach(var copy in journal.Copies.ToArray())
        {
            if(!copy.Confirmed || copy.Name == currentCopy.Name || copy.Name == previous?.Name || Now - copy.Created < TimeSpan.FromDays(7))continue;
            if(!Same(current, await CurrentAsync()))break;
            CheckPaths(); var path = Path.Combine(folder, copy.Name);
            if(File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)continue;
            if(await MatchesHashAsync(copy,path)&&TryDelete(path))journal.Copies.Remove(copy);
        }
        Write(journal);
    }
    private static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static async Task<bool> MatchesHashAsync(Copy copy,string path)
    {
        if(!File.Exists(path))return true;
        if(copy.Sha256==null||new FileInfo(path).Length!=copy.Length)return false;
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream))==copy.Sha256;
    }
    private static bool OwnedName(string name) => !string.IsNullOrEmpty(name) && name == Path.GetFileName(name) && name.StartsWith("stillframe-", StringComparison.Ordinal)
        && Guid.TryParseExact(Path.GetFileNameWithoutExtension(name)["stillframe-".Length..], "N", out _)
        && Path.GetExtension(name) is ".jpg" or ".jpeg" or ".png" or ".bmp";
    private void CheckPaths()
    {
        foreach(var path in new[] { Path.GetFullPath(root), folder, JournalPath })
            if((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new AppException("锁屏副本目录包含链接，已停止写入和清理。");
    }
    private Journal Read()
    {
        CheckPaths();
        if(!File.Exists(JournalPath))return new(1, []);
        try
        {
            if(new FileInfo(JournalPath).Length > 256 * 1024)throw new JsonException();
            var value = JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath));
            if(value is not { Version: 1 } || value.Copies == null || value.Copies.Count > 1000
                || value.Copies.Any(x => x == null || !OwnedName(x.Name) || x.Sha256!=null&&(x.Sha256.Length!=64||!x.Sha256.All(Uri.IsHexDigit))) || value.Copies.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Copies.Count)throw new JsonException();
            return value;
        }
        catch(JsonException) { throw new AppException("锁屏副本记录无法识别，文件已保留；未修改锁屏。"); }
    }
    private void Write(Journal value)
    {
        CheckPaths(); Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using(var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, value); output.Flush(true); }
            File.Move(temp, JournalPath, true);
        }
        finally { TryDelete(temp); }
    }
    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch(IOException) { return false; } catch(UnauthorizedAccessException) { return false; }
    }
}
