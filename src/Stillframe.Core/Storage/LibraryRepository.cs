using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Stillframe.Core.Storage;

public sealed class LibraryRepository(string root)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(root,"library.db"), Pooling = true }.ToString();
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;"; command.ExecuteNonQuery();
        return connection;
    }
    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string, object?)[] values)
    {
        var command=connection.CreateCommand();command.CommandText=sql;
        foreach(var (key,value) in values)command.Parameters.AddWithValue(key,value??DBNull.Value);
        return command;
    }
    public Task InitializeAsync() => Task.Run(() =>
    {
        Directory.CreateDirectory(root); using var connection=Open();
        using var check=Command(connection,"PRAGMA user_version;");
        var version=Convert.ToInt32(check.ExecuteScalar());
        if(version>2)throw new AppException("数据库来自更新版本，请使用更新的拾景程序。");
        // Upgrade early v0.1 development databases without deleting existing library data.
        using(var columns=Command(connection,"PRAGMA table_info(items)"))
        {
            using var rows=columns.ExecuteReader();var names=new HashSet<string>();while(rows.Read())names.Add(rows.GetString(1));rows.Close();
            if(names.Count>0)foreach(var name in new[]{"license_name","license_url","preview_url"})if(!names.Contains(name)){using var alter=Command(connection,$"ALTER TABLE items ADD COLUMN {name} TEXT");alter.ExecuteNonQuery();}
        }
        using var stream=typeof(LibraryRepository).Assembly.GetManifestResourceStream("Stillframe.Core.Storage.schema.sql")!;
        using var reader=new StreamReader(stream);using var command=Command(connection,reader.ReadToEnd());command.ExecuteNonQuery();
    });
    public Task<IReadOnlyList<WallpaperItem>> GetItemsAsync(bool activeOnly=false) => Task.Run<IReadOnlyList<WallpaperItem>>(() =>
    {
        using var connection=Open();using var command=Command(connection,"SELECT i.* FROM items i JOIN sources s ON s.id=i.source_id "+(activeOnly?"WHERE s.enabled=1 ":"")+"ORDER BY added_at DESC,id");
        using var reader=command.ExecuteReader();var items=new List<WallpaperItem>();
        string? Text(int i)=>reader.IsDBNull(i)?null:reader.GetString(i);
        while(reader.Read())items.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),Text(4),Text(5),Text(6),Text(7),reader.GetInt32(8),reader.GetInt32(9),Text(10),Text(11),reader.GetBoolean(12),DateTimeOffset.Parse(reader.GetString(13),CultureInfo.InvariantCulture),Text(14),Text(15),Text(16)));
        return items;
    });
    public Task AddItemAsync(WallpaperItem item) => ExecuteAsync("INSERT INTO items VALUES($id,$title,$source,$name,$url,$file,$thumb,$hash,$w,$h,$author,$page,$favorite,$at,$license,$licenseUrl,$preview) ON CONFLICT(id) DO UPDATE SET title=$title,original_url=$url,author=$author,source_page=$page,license_name=$license,license_url=$licenseUrl,preview_url=$preview ON CONFLICT(source_id,original_url) DO NOTHING",
        ("$id",item.Id),("$title",item.Title),("$source",item.SourceId),("$name",item.SourceName),("$url",item.OriginalUrl),("$file",item.OriginalFile),("$thumb",item.ThumbnailFile),("$hash",item.Sha256),("$w",item.Width),("$h",item.Height),("$author",item.Author),("$page",item.SourcePage),("$favorite",item.Favorite),("$at",item.AddedAt.ToString("O")),("$license",item.LicenseName),("$licenseUrl",item.LicenseUrl),("$preview",item.PreviewUrl));
    public Task UpdatePreviewAsync(string id,string file)=>ExecuteAsync("UPDATE items SET thumbnail_file=$file WHERE id=$id AND original_file IS NULL",("$file",file),("$id",id));
    public Task UpdateAssetAsync(string id,string file,string thumb,string hash,ImageInfo info) => ExecuteAsync("UPDATE items SET original_file=$f,thumbnail_file=$t,sha256=$s,width=$w,height=$h WHERE id=$id",("$f",file),("$t",thumb),("$s",hash),("$w",info.Width),("$h",info.Height),("$id",id));
    public Task SetFavoriteAsync(string id,bool value) => ExecuteAsync("UPDATE items SET favorite=$v WHERE id=$id",("$v",value),("$id",id));
    public Task<IReadOnlyList<SourceDefinition>> GetSourcesAsync() => Task.Run<IReadOnlyList<SourceDefinition>>(() =>
    {
        using var connection=Open();using var command=Command(connection,"SELECT * FROM sources ORDER BY name");using var reader=command.ExecuteReader();var result=new List<SourceDefinition>();
        while(reader.Read())result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetBoolean(4)));return result;
    });
    public Task SaveSourceAsync(SourceDefinition source)=>ExecuteAsync("INSERT INTO sources VALUES($id,$name,$kind,$address,$enabled) ON CONFLICT(id) DO UPDATE SET name=$name,address=$address,enabled=$enabled",("$id",source.Id),("$name",source.Name),("$kind",source.Kind),("$address",source.Address),("$enabled",source.Enabled));
    public Task<string?> GetSettingAsync(string key)=>ScalarAsync("SELECT value FROM settings WHERE key=$key",("$key",key));
    public Task SetSettingAsync(string key,string value)=>ExecuteAsync("INSERT INTO settings VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value",("$key",key),("$value",value));
    public Task<string?> GetDailyAsync(DateOnly date)=>ScalarAsync("SELECT item_id FROM daily WHERE local_date=$date",("$date",date.ToString("yyyy-MM-dd")));
    public Task SaveDailyAsync(DateOnly date,string id,bool replace)=>ExecuteAsync(replace?"INSERT INTO daily VALUES($date,$id) ON CONFLICT(local_date) DO UPDATE SET item_id=$id":"INSERT OR IGNORE INTO daily VALUES($date,$id)",("$date",date.ToString("yyyy-MM-dd")),("$id",id));
    private Task ExecuteAsync(string sql,params (string,object?)[] values)=>Task.Run(()=>{using var connection=Open();using var command=Command(connection,sql,values);command.ExecuteNonQuery();});
    private Task<string?> ScalarAsync(string sql,params (string,object?)[] values)=>Task.Run(()=>{using var connection=Open();using var command=Command(connection,sql,values);return command.ExecuteScalar() as string;});
}
