using Stillframe.Core;

internal static class LockScreenCopyChecks
{
    public static async Task RunAsync(string root, Action<bool,string> check)
    {
        var data=Path.Combine(root,"lock-lifecycle");Directory.CreateDirectory(data);
        var original=Path.Combine(data,"source.jpg");await File.WriteAllBytesAsync(original,[1,2,3,4]);
        var now=DateTimeOffset.UtcNow;string? current=null;var submitted=new List<string>();
        var mode="success";CancellationTokenSource? during=null;
        async Task<bool> Set(string path)
        {
            submitted.Add(path);await Task.Yield();
            if(mode=="false")return false;
            current=path;
            if(mode=="throw")throw new IOException("unknown system outcome");
            during?.Cancel();return true;
        }
        Task<string?> Read()=>Task.FromResult(current);
        LockScreenCopies Store()=>new(data,Set,Read,()=>now);
        string[] Files()=>Directory.GetFiles(Path.Combine(data,"lockscreen"),"stillframe-*");
        var store=Store();
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();try{await store.SetAsync(original,cancel.Token);}catch(OperationCanceledException){}
            check(submitted.Count==0&&!Directory.Exists(Path.Combine(data,"lockscreen")),"lock cancellation before submission creates no copy");
        }
        await store.SetAsync(original,CancellationToken.None);var first=current!;
        check(File.Exists(first)&&File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(original)),"lock success retains immutable current copy");
        var unknown=Path.Combine(data,"lockscreen",Guid.NewGuid().ToString("N")+".jpg");await File.WriteAllTextAsync(unknown,"legacy keep");
        await File.WriteAllTextAsync(Path.Combine(data,"settings.txt"),"keep settings");
        now=now.AddDays(8);await store.SetAsync(original,CancellationToken.None);var second=current!;
        check(first!=second&&File.Exists(first),"lock unique names and previous successful copy retained");
        now=now.AddDays(8);await Store().SetAsync(original,CancellationToken.None);
        check(!File.Exists(first)&&File.Exists(second)&&File.Exists(current)&&Files().Length==2,"lock restart reclaims only old confirmed owned copies");
        mode="false";var prior=current;try{await Store().SetAsync(original,CancellationToken.None);}catch(AppException){}
        check(current==prior&&Files().Length==2,"lock false result removes rejected candidate and preserves current");
        mode="throw";try{await Store().SetAsync(original,CancellationToken.None);}catch(IOException){}
        var ambiguous=current!;
        check(File.Exists(ambiguous)&&Files().Length==3,"lock exception after submission retains possibly referenced copy");
        await Store().ReconcileAsync();
        check(File.Exists(ambiguous),"lock restart readback reconciles ambiguous submission without deleting current");
        mode="success";using(during=new CancellationTokenSource())
        {await Store().SetAsync(original,during.Token);check(during.IsCancellationRequested&&File.Exists(current),"lock late cancellation waits for successful OS outcome and retains file");}
        during=null;
        var all=Files();current=null;now=now.AddDays(100);await Store().ReconcileAsync();
        check(all.All(File.Exists),"lock unknown readback never authorizes cleanup");
        current=Path.Combine(data,"system-cache.jpg");await Store().ReconcileAsync();
        check(all.All(File.Exists),"lock external or cached system path is not release evidence");
        current=all.Last();
        for(int i=0;i<12;i++){now=now.AddDays(8);await Store().SetAsync(original,CancellationToken.None);}
        check(Files().Length==2&&File.Exists(current),"lock repeated replacements keep current and previous after grace period");
        check(File.Exists(unknown)&&File.ReadAllText(Path.Combine(data,"settings.txt"))=="keep settings"&&File.ReadAllBytes(original).SequenceEqual(new byte[]{1,2,3,4}),"lock cleanup preserves legacy copies, settings and user source");
        var before=Files();var manifest=Path.Combine(data,"lockscreen","owned-v1.json");var saved=await File.ReadAllTextAsync(manifest);
        await File.WriteAllTextAsync(manifest,"{bad");await Store().ReconcileAsync();
        try{await Store().SetAsync(original,CancellationToken.None);}catch(AppException){}
        check(before.All(File.Exists)&&Files().Length==before.Length,"lock corrupt journal fails closed without deleting or setting");
        await File.WriteAllTextAsync(manifest,saved.Replace("stillframe-","../stillframe-"));await Store().ReconcileAsync();
        check(before.All(File.Exists),"lock journal traversal is rejected");
        await File.WriteAllTextAsync(manifest,saved);
        var young=current!;now=now.AddMinutes(1);await Store().SetAsync(original,CancellationToken.None);await Store().SetAsync(original,CancellationToken.None);
        check(File.Exists(young),"lock grace period protects recently replaced copies");
        var failData=Path.Combine(root,"lock-write-failure");Directory.CreateDirectory(Path.Combine(failData,"lockscreen","owned-v1.json"));var called=false;
        var blocked=new LockScreenCopies(failData,_=>{called=true;return Task.FromResult(true);},Read);
        try{await blocked.SetAsync(original,CancellationToken.None);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        check(!called&&Directory.GetFiles(Path.Combine(failData,"lockscreen"),"stillframe-*").Length==0,"lock journal write failure prevents OS mutation and cleans unsubmitted copy");
        // Two callers must never run the OS effect concurrently.
        int active=0,maxActive=0;
        var serial=new LockScreenCopies(Path.Combine(root,"lock-serial"),async path=>{maxActive=Math.Max(maxActive,Interlocked.Increment(ref active));await Task.Delay(10);Interlocked.Decrement(ref active);return true;},()=>Task.FromResult<string?>(null));
        await Task.WhenAll(serial.SetAsync(original,CancellationToken.None),serial.SetAsync(original,CancellationToken.None));
        check(maxActive==1,"lock manual and automatic calls are serialized");
        using(var preDispatch=new CancellationTokenSource())
        {
            var canceledRoot=Path.Combine(root,"lock-cancel-copy");bool effect=false;
            var cancelStore=new LockScreenCopies(canceledRoot,_=>{effect=true;return Task.FromResult(true);},Read,()=>{preDispatch.Cancel();return now;});
            try{await cancelStore.SetAsync(original,preDispatch.Token);}catch(OperationCanceledException){}
            check(!effect&&Directory.GetFiles(Path.Combine(canceledRoot,"lockscreen"),"stillframe-*").Length==0,"lock cancellation after durable copy but before dispatch cleans candidate");
        }
        var commitRoot=Path.Combine(root,"lock-commit-failure");FileStream? held=null;string? commitCurrent=null;
        var commitStore=new LockScreenCopies(commitRoot,path=>{commitCurrent=path;held=new FileStream(Path.Combine(commitRoot,"lockscreen","owned-v1.json"),FileMode.Open,FileAccess.Read,FileShare.Read);return Task.FromResult(true);},()=>Task.FromResult(commitCurrent));
        try{await commitStore.SetAsync(original,CancellationToken.None);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}finally{held?.Dispose();}
        check(File.Exists(commitCurrent),"lock post-success journal failure preserves system candidate");
        await commitStore.ReconcileAsync();check(File.Exists(commitCurrent),"lock restart can confirm durable intent after post-success journal failure");
        var editedRoot=Path.Combine(root,"lock-replaced-copy");string? editedCurrent=null;var copies=new List<string>();
        var editedStore=new LockScreenCopies(editedRoot,path=>{editedCurrent=path;copies.Add(path);return Task.FromResult(true);},()=>Task.FromResult(editedCurrent),()=>now);
        await editedStore.SetAsync(original,CancellationToken.None);await File.WriteAllTextAsync(copies[0],"user");
        now=now.AddDays(8);await editedStore.SetAsync(original,CancellationToken.None);now=now.AddDays(8);await editedStore.SetAsync(original,CancellationToken.None);
        check(File.ReadAllText(copies[0])=="user","lock cleanup never deletes same-length content changed at a previously owned path");
    }
}
