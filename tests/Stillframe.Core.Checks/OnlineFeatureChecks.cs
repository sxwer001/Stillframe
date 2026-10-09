using System.Text.Json;
using Microsoft.Data.Sqlite;
using Stillframe.Core;

internal static class OnlineFeatureChecks
{
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        var bing=BuiltInSources.Parse(BuiltInSources.Create("bing"),"""
            {"images":[{"url":"/th?id=sample_1920.jpg","urlbase":"/th?id=sample","title":"海边","copyright":"作者","wp":true},{"url":"/th?id=denied.jpg","wp":false}]}
            """);
        check(bing.Items.Count==1&&bing.Items[0].OriginalUrl!.EndsWith("_UHD.jpg")&&bing.Items[0].PreviewUrl!.EndsWith("_640x360.jpg"),"Bing wallpaper permission flag and original/preview separation");
        var unavailable=BuiltInSources.Parse(BuiltInSources.Create("bing"),"""{"images":[{"url":"/denied.jpg","wp":false},{"url":"/older.jpg","wp":true}]}""");
        check(bing.FeaturedItemId==bing.Items[0].Id&&unavailable.FeaturedItemId==null,"Bing daily identity never substitutes denied current image with random history");
        var archiveJson=JsonSerializer.Serialize(Enumerable.Range(0,50).Select(n=>new{date=new DateOnly(2024,1,1).AddDays(n).ToString("yyyy-MM-dd"),caption="历史风景",copyright="图片作者",url=$"https://bing.npanuhin.me/CN/zh/{n}.jpg",bing_url="https://bing.com/th?id=old_UHD.jpg"}));
        var archive=BuiltInSources.Parse(BuiltInSources.Create("bing-archive"),archiveJson);
        var archiveSecond=BuiltInSources.Parse(BuiltInSources.Create("bing-archive",2),archiveJson,2);
        check(archive.Items.Count==24&&archive.LastPage==3&&!archive.Items.Select(x=>x.Id).Intersect(archiveSecond.Items.Select(x=>x.Id)).Any(),"Bing archive pagination keeps historic dates distinct without repeats");
        var archiveAll=BuiltInSources.Parse(BuiltInSources.Create("bing-archive"),archiveJson,order:"archive");
        check(archiveAll.Items.Count==50&&archiveAll.Items.All(x=>x.OriginalUrl!.StartsWith("https://bing.npanuhin.me/")&&x.Author=="图片作者"&&x.Width==0),"random Bing archive selects across full history, uses stored originals and retains attribution");
        var wallhaven=BuiltInSources.Parse(BuiltInSources.Create("wallhaven"),"""
            {"data":[{"id":"a","purity":"sfw","path":"https://example.org/original.jpg","thumbs":{"large":"https://example.org/preview.jpg"},"url":"https://wallhaven.cc/w/a","dimension_x":3840,"dimension_y":2160},{"id":"b","purity":"nsfw"},{"id":"c","purity":"sfw","file_size":60000000}],"meta":{"last_page":5}}
            """);
        check(wallhaven.Items.Count==1&&wallhaven.LastPage==5&&wallhaven.Items[0].Width==0&&wallhaven.Items[0].Author==null,"Wallhaven SFW/size filters, pagination and unverified dimensions");
        var uri=BuiltInSources.Create("wallhaven",2,"city & sea").Address;
        check(uri.Contains("purity=100")&&uri.Contains("page=2")&&uri.Contains("city%20%26%20sea"),"API query encoding and fixed SFW policy");
        var commons=BuiltInSources.Parse(BuiltInSources.Create("commons"),"""
            {"query":{"pages":[{"pageid":12,"title":"File:sample.jpg","imageinfo":[{"url":"https://example.org/a.jpg","thumburl":"https://example.org/p.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:sample.jpg","size":42,"mime":"image/jpeg","extmetadata":{"Artist":{"value":"<a href='x'>Jane &amp; John</a>"},"LicenseShortName":{"value":"CC BY 4.0"},"LicenseUrl":{"value":"https://creativecommons.org/licenses/by/4.0/"}}}]}]}}
            """);
        check(commons.Items.Single().Author=="Jane & John"&&commons.Items[0].LicenseName=="CC BY 4.0","Commons attribution strips markup and retains license");
        try{BuiltInSources.Parse(BuiltInSources.Create("wallhaven"),"""{"data":[{"id":"bad","purity":"sfw","path":"https://127.0.0.1/a.jpg"}]}""");throw new Exception("Unsafe API image accepted");}
        catch(AppException){check(true,"API returned private-network image rejected");}

        using(var filtered=new LibraryService(Path.Combine(root,"daily-filter"),new TestImages()))
        {
            await filtered.InitializeAsync();await filtered.Repository.SaveSourceAsync(BuiltInSources.Create("bing"));await filtered.Repository.SaveSourceAsync(BuiltInSources.Create("wallhaven"));
            await filtered.Repository.AddItemAsync(bing.Items[0]);await filtered.Repository.AddItemAsync(wallhaven.Items[0]);
            var date=new DateOnly(2026,10,9);await filtered.GetDailyAsync(date,sourceId:"bing");
            check((await filtered.GetDailyAsync(date,true,"wallhaven"))?.SourceId=="wallhaven","recommendation replacement honors selected source");
            await filtered.Repository.SaveSourceAsync(BuiltInSources.Create("wallhaven") with{Enabled=false});
            check(await filtered.GetDailyAsync(date,sourceId:"wallhaven")==null,"selected disabled source cannot produce a daily recommendation");
        }

        var folder=Path.Combine(root,"automation");Directory.CreateDirectory(folder);var repository=new Stillframe.Core.Storage.LibraryRepository(folder);await repository.InitializeAsync();
        var schedule=new DailyAutomation(repository);int effects=0;var now=new DateTimeOffset(2026,10,9,10,0,0,TimeSpan.FromHours(8));
        var item=new WallpaperItem("fixture","test","local","本地",null,null,null,null,1,1,null,null,false,now);
        Task<WallpaperItem?> Select(CancellationToken token)=>Task.FromResult<WallpaperItem?>(item);
        Task Perform(string kind,WallpaperItem value,CancellationToken token){effects++;return Task.CompletedTask;}
        check((await schedule.TickAsync(now,Select,Perform,CancellationToken.None)).Count==0&&effects==0,"automation defaults off without effects");
        await new AutomationSettings(true,true,true).SaveAsync(repository);
        check((await schedule.TickAsync(now.AddHours(-2),Select,Perform,CancellationToken.None)).Count==0,"automation waits until local scheduled hour");
        check((await schedule.TickAsync(now,Select,Perform,CancellationToken.None)).Count==3&&effects==3,"all daily actions run through substituted effect handlers");
        var restarted=new DailyAutomation(repository);await restarted.TickAsync(now.AddHours(1),Select,Perform,CancellationToken.None);check(effects==3,"automation success survives restart and does not duplicate same day");
        await restarted.TickAsync(now.AddDays(1),Select,Perform,CancellationToken.None);check(effects==6,"automation advances after local date rollover");
        int failures=0;Task Fail(string kind,WallpaperItem value,CancellationToken token){failures++;throw new AppException("fixture failure");}
        await new AutomationSettings(true).SaveAsync(repository);var failing=new DailyAutomation(repository);var day3=now.AddDays(2);
        await failing.TickAsync(day3,Select,Fail,CancellationToken.None);await failing.TickAsync(day3.AddMinutes(1),Select,Fail,CancellationToken.None);
        check(failures==1&&await repository.GetSettingAsync("automation.last.desktop")!=day3.ToString("yyyy-MM-dd"),"failed action is not marked completed and backs off");
        await failing.TickAsync(day3.AddMinutes(16),Select,Perform,CancellationToken.None);check(effects==7,"failed action retries after backoff");
        await new AutomationSettings(true).SaveAsync(repository);
        await new DailyAutomation(repository).TickAsync(day3.AddDays(1),async token=>{await new AutomationSettings().SaveAsync(repository);return item;},Perform,CancellationToken.None);
        check(effects==7,"action disabled during selection does not execute");

        // Build an actual older database, then upgrade in place and retain asset/favorite metadata.
        var legacy=Path.Combine(root,"legacy");Directory.CreateDirectory(legacy);
        using(var connection=new SqliteConnection("Data Source="+Path.Combine(legacy,"library.db")))
        {
            connection.Open();using var command=connection.CreateCommand();command.CommandText="""
                CREATE TABLE sources(id TEXT PRIMARY KEY,name TEXT,kind TEXT,address TEXT,enabled INTEGER);
                INSERT INTO sources VALUES('local','本地图库','local','',1);
                CREATE TABLE items(id TEXT PRIMARY KEY,title TEXT,source_id TEXT,source_name TEXT,original_url TEXT,original_file TEXT,thumbnail_file TEXT,sha256 TEXT,width INTEGER,height INTEGER,author TEXT,source_page TEXT,favorite INTEGER,added_at TEXT,license_name TEXT,license_url TEXT,UNIQUE(source_id,original_url));
                INSERT INTO items VALUES('old','保留','local','本地图库',NULL,'keep.jpg','keep-thumb.jpg','hash',1920,1080,NULL,NULL,1,'2026-10-08T00:00:00+00:00',NULL,NULL);
                PRAGMA user_version=1;
                """;command.ExecuteNonQuery();
        }
        var upgraded=new Stillframe.Core.Storage.LibraryRepository(legacy);await upgraded.InitializeAsync();var preserved=(await upgraded.GetItemsAsync()).Single();
        check(preserved.Favorite&&preserved.OriginalFile=="keep.jpg"&&preserved.PreviewUrl==null,"schema v1 to v2 migration preserves original and favorite");
        await upgraded.AddItemAsync(preserved with{PreviewUrl="https://example.org/preview.jpg"});
        check((await upgraded.GetItemsAsync()).Single().OriginalFile=="keep.jpg","API metadata refresh preserves managed original");
    }
}
