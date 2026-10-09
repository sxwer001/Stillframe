using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Markup;
using Stillframe.App.Services;
using Stillframe.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.Storage.Pickers;
using Windows.Graphics;

namespace Stillframe.App;

public sealed partial class MainWindow : Window
{
    private readonly LibraryService library;
    private readonly DesktopService desktop=new();
    private readonly NavigationView navigation=new(){FontSize=13,IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed,IsPaneVisible=false,IsSettingsVisible=true};
    private readonly StackPanel page=new(){Spacing=18,Margin=new Thickness(28)};
    private readonly Grid pageViewport=new();
    private readonly ScrollViewer pageScroll=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private readonly InfoBar notice=new(){IsOpen=false,IsClosable=true};
    private readonly ProgressBar progress=new(){Visibility=Visibility.Collapsed,IsIndeterminate=true};
    private readonly Button cancel=new(){Content="取消操作",Visibility=Visibility.Collapsed};
    private IReadOnlyList<MonitorTarget> monitorTargets=[new("","所有显示器")];
    private string monitorId="";
    private WallpaperPosition fit=WallpaperPosition.Fill;
    private readonly Grid immersive=new();
    private readonly Grid shell=new();
    // Reserve the left button lane outside the native draggable region.
    private readonly Grid titlebar=new(){Height=44,Margin=new Thickness(64,0,160,0),Padding=new Thickness(8,0,0,0)};
    private readonly TextBlock brand=new(){Text="拾景",VerticalAlignment=VerticalAlignment.Center,FontSize=12};
    private Button sidebarToggle=null!;
    private string? displayedView;
    private bool animateViewChrome;
    private Task navigationTask=Task.CompletedTask;
    private bool synchronizingNavigation;
    private IReadOnlyList<WallpaperItem> items=[];
    private WallpaperItem? daily;
    private WallpaperItem? selected;
    private DesktopSnapshot? snapshot;
    private CancellationTokenSource? operation;
    private string route="today";
    private string query="";
    private bool favoritesOnly;
    private GridView? gallery;
    private bool initialized;
    private string? smokeReport;

    public MainWindow(string? dataRoot=null)
    {
        Title="拾景 · 静态壁纸";
        library=new(dataRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Shijing"),new WicImageProcessor());
        homeImages=new(library);
        try{SystemBackdrop=new MicaBackdrop();}catch{}
        AppWindow.Resize(new SizeInt32(1200,850));
        shell.RowDefinitions.Add(new(){Height=GridLength.Auto});shell.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        titlebar.Children.Add(brand);
        ExtendsContentIntoTitleBar=true;SetTitleBar(titlebar);
        navigation.MenuItems.Add(Nav("今日推荐","today",Symbol.Home));navigation.MenuItems.Add(Nav("发现壁纸","discover",Symbol.Find));navigation.MenuItems.Add(Nav("我的图库","library",Symbol.Pictures));navigation.MenuItems.Add(Nav("图源管理","sources",Symbol.World));
        var content=new Grid();content.RowDefinitions.Add(new(){Height=GridLength.Auto});content.RowDefinitions.Add(new(){Height=GridLength.Auto});content.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        notice.Margin=new Thickness(20,56,20,0);notice.VerticalAlignment=VerticalAlignment.Top;notice.MaxWidth=760;
        InitializeOperationStatus();
        pageScroll.Content=page;pageViewport.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});pageViewport.Children.Add(pageScroll);
        Grid.SetRow(pageViewport,2);content.Children.Add(pageViewport);
        navigation.Content=content;Grid.SetRow(navigation,1);shell.Children.Add(navigation);
        Grid.SetRowSpan(immersive,2);shell.Children.Add(immersive);shell.Children.Add(titlebar);
        sidebarToggle=Menu();shell.Children.Add(sidebarToggle);
        InitializeSettingsDrawer();
        Grid.SetRowSpan(notice,2);shell.Children.Add(notice);Content=shell;
        immersive.SizeChanged+=HomeSizeChanged;
        immersive.PointerWheelChanged+=async (_,e)=>
        {
            if(selected!=null||route is not ("today" or "discover")||settingsDrawer.IsPaneOpen)return;
            var point=e.GetCurrentPoint(immersive);if(point.Properties.IsHorizontalMouseWheel)return;
            e.Handled=true;await HandleWallpaperWheelAsync(point.Properties.MouseWheelDelta);
        };
        cancel.Click+=(_,_)=>operation?.Cancel();
        navigation.SelectionChanged+=(_,e)=>
        {if(!initialized||synchronizingNavigation)return;navigationTask=NavigateAsync(e.IsSettingsSelected?"settings":(e.SelectedItem as NavigationViewItem)?.Tag as string??"today");};
        shell.ActualThemeChanged+=(_,_)=>SetChrome(immersive.Visibility==Visibility.Visible);
        shell.KeyDown+=async (_,e)=>
        {
            if(e.Key!=Windows.System.VirtualKey.Escape)return;
            if(settingsDrawer.IsPaneOpen){CloseSettings();e.Handled=true;}
            else if(fullScreen){fullScreen=false;AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);e.Handled=true;}
            else if(selected!=null){e.Handled=true;await NavigateAsync(route);}
        };
        shell.Loaded+=async (_,_)=>
        {
            if(initialized)return;
            var ready=false;
            await RunAsync(async _=>{await library.InitializeAsync();await LoadAutomationSettingsAsync();await RefreshAsync();await LoadMonitorsAsync();await LoadAppearanceAsync();var theme=await library.Repository.GetSettingAsync("theme");shell.RequestedTheme=theme=="dark"?ElementTheme.Dark:theme=="light"?ElementTheme.Light:ElementTheme.Default;ready=true;});
            if(!ready){if(smokeReport!=null){await File.WriteAllTextAsync(smokeReport,"FAIL initialization: "+notice.Message);Close();}return;}
            initialized=true;navigation.SelectedItem=navigation.MenuItems[0];await navigationTask;
            if(smokeReport!=null)
            {
                if(screenshotMode)await ScreenshotsAsync(smokeReport);else await SmokeAsync(smokeReport);
            }
            else StartAutomation();
        };
        Closed+=(_,_)=>{operation?.Cancel();CancelHomeRequest();StopAutomation();};
    }
    public void BeginSmoke(string report)=>smokeReport=report;
    private async Task SmokeAsync(string report)
    {
        try
        {
            var fixture=Path.Combine(library.Root,"preview-smoke.bmp");WritePreviewFixture(fixture);
            await library.ImportAsync([fixture],CancellationToken.None);await RefreshAsync();
            var lines=new List<string>();
            foreach(var target in new[]{"today","discover","library","sources","settings"})
            {
                await SmokeNavigateAsync(target);await Task.Delay(200);
                lines.Add(target=="settings"?"PASS settings drawer opened without route replacement":"PASS WinUI page rendered: "+target);
                if(target=="today"||target=="discover")await CaptureAsync(Path.Combine(Path.GetDirectoryName(report)!,"ui-"+target+".png"));
            }
            for(var cycle=0;cycle<6;cycle++)
            {
                await SmokeNavigateAsync("library");
                if(gallery==null||!((WallpaperCard[])gallery.ItemsSource).Any())throw new Exception("Gallery did not bind imported fixture");
                await OpenDetailAsync(items.First());await Task.Delay(120);
                var controls=((StackPanel)((Flyout)detailTargets!.Flyout!).Content).Children.OfType<ComboBox>().ToArray();
                if(controls[1].SelectedIndex!=(cycle==0?0:1))throw new Exception("Fit state lost across detail navigation");
                controls[1].SelectedIndex=1;
                await FavoriteAsync(selected!);
                if(fit!=WallpaperPosition.Fit)throw new Exception("Fit state lost after favorite rerender");
                if(notice.IsOpen&&notice.Severity==InfoBarSeverity.Error)throw new Exception("Favorite failed: "+notice.Message);
                lines.Add("PASS gallery → detail → favorite rerender, cycle "+(cycle+1));
            }
            await CaptureAsync(Path.Combine(Path.GetDirectoryName(report)!,"ui-detail.png"));
            await NavigateAsync("today");await Task.Delay(200);await CaptureAsync(Path.Combine(Path.GetDirectoryName(report)!,"ui-today.png"));
            var before=AppWindow.Size;AppWindow.Resize(new SizeInt32(900,680));await Task.Delay(200);
            await CaptureAsync(Path.Combine(Path.GetDirectoryName(report)!,"ui-today-small.png"));
            await OpenDetailAsync(items.First());await Task.Delay(200);await CaptureAsync(Path.Combine(Path.GetDirectoryName(report)!,"ui-detail-small.png"));AppWindow.Resize(before);
            lines.Add("PASS immersive home/detail content renders at normal and small window sizes");
            await CheckPresentationAsync(report,lines);
            lines.Add("No wallpaper mutation or user image access.");await File.WriteAllLinesAsync(report,lines);
        }
        catch(Exception ex){await File.WriteAllTextAsync(report,"FAIL UI smoke: "+ex);Environment.ExitCode=1;}
        Close();
    }
    private async Task SmokeNavigateAsync(string target)
    {
        if(target=="settings")
        {
            var previousRoute=route;var previousSelection=selected;
            await NavigateAsync(target);
            if(!settingsDrawer.IsPaneOpen||route!=previousRoute||selected!=previousSelection)throw new Exception("Settings replaced the background route");
            await Task.Delay(260);await CaptureAsync(Path.Combine(Path.GetDirectoryName(smokeReport!)!,"ui-settings.png"));
            CloseSettings();await appearanceSaveTask;await Task.Delay(260);return;
        }
        var item=target=="settings"?navigation.SettingsItem:navigation.MenuItems.OfType<NavigationViewItem>().First(x=>x.Tag as string==target);
        if(ReferenceEquals(navigation.SelectedItem,item))await NavigateAsync(target);
        else{navigation.SelectedItem=item;await navigationTask;}
        if(route!=target||selected!=null)throw new Exception("Navigation did not complete: "+target);
    }
    private static IEnumerable<Button> FindVisualButtons(DependencyObject root)
    {
        if(root is Button button)yield return button;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var child in FindVisualButtons(VisualTreeHelper.GetChild(root,index)))yield return child;
    }
    private async Task CaptureAsync(string destination)
    {
        if(!await pictureReady.WaitAsync(TimeSpan.FromSeconds(5)))throw new Exception("Image failed to decode before capture");
        ((FrameworkElement)Content).UpdateLayout();await Task.Delay(300);
        var raster=new RenderTargetBitmap();await raster.RenderAsync((UIElement)Content);
        var buffer=await raster.GetPixelsAsync();using var reader=Windows.Storage.Streams.DataReader.FromBuffer(buffer);var pixels=new byte[buffer.Length];reader.ReadBytes(pixels);
        using var output=await Windows.Storage.Streams.FileRandomAccessStream.OpenAsync(destination,Windows.Storage.FileAccessMode.ReadWrite,Windows.Storage.StorageOpenOptions.None,Windows.Storage.Streams.FileOpenDisposition.CreateAlways);
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,output);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)raster.PixelWidth,(uint)raster.PixelHeight,96,96,pixels);await encoder.FlushAsync();
    }
    private static NavigationViewItem Nav(string text,string tag,Symbol symbol)=>new(){Content=text,Tag=tag,Icon=Glyph(symbol)};
    private static TextBlock Text(string text,double size=13)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap};
    private static Button Action(string text,Func<Task> handler,bool primary=false)
    {var b=new Button{Content=text,FontSize=13,MinHeight=36,Padding=new Thickness(12,7,12,7)};if(primary)b.Style=(Style)Application.Current.Resources["AccentButtonStyle"];b.Click+=async (_,_)=>await handler();return b;}
    private static WrapPanel Row(params UIElement[] children){var row=new WrapPanel();foreach(var c in children)row.Children.Add(c);return row;}
    private void Heading(string title,string description){page.Children.Add(Text(title,28));page.Children.Add(Text(description));}
    private async Task RefreshAsync()
    {
        items=await library.GetItemsAsync();
        if(daily!=null)daily=items.FirstOrDefault(x=>x.Id==daily.Id);
        if(discoveryPicture!=null)discoveryPicture=items.FirstOrDefault(x=>x.Id==discoveryPicture.Id);
        if(daily==null)
        {
            if(smokeReport!=null){homeSourceId="local";daily=await library.GetDailyAsync(DateOnly.FromDateTime(DateTime.Now));}
            else daily=await homeImages.CachedBingAsync();
        }
    }
    private async Task LoadMonitorsAsync()
    {
        try{monitorTargets=await desktop.EnumerateAsync();}
        catch{monitorTargets=[new("","所有显示器")];}
        if(!monitorTargets.Any(x=>x.Id==monitorId))monitorId="";
    }
    private async Task RunAsync(Func<CancellationToken,Task> work,string label="正在处理…")
    {
        if(operation!=null)return;
        operation=new();operationLabel.Text=label;operationStatus.Visibility=Visibility.Visible;progress.IsIndeterminate=true;progress.Visibility=Visibility.Visible;cancel.Visibility=Visibility.Visible;AttachOperationStatus();settingsDrawer.IsEnabled=homeLoading;
        try{await work(operation.Token);}
        catch(OperationCanceledException){if(!homeLoading)Notify("操作已取消；已完成的步骤会保留。",false);}
        catch(AppException ex){Notify(ex.Message,true);}
        catch(HttpRequestException){Notify("图源网络连接失败，请重试或检查网络。",true);}
        catch(System.Text.Json.JsonException){Notify("JSON清单格式无效，请检查结构。",true);}
        catch(Exception ex){Notify("操作失败（"+ex.GetType().Name+"）。请检查文件权限、网络或图片格式后重试。",true);}
        finally{operation.Dispose();operation=null;operationStatus.Visibility=Visibility.Collapsed;AttachOperationStatus();progress.Visibility=Visibility.Collapsed;cancel.Visibility=Visibility.Collapsed;navigation.IsEnabled=true;page.IsHitTestVisible=true;immersive.IsHitTestVisible=true;settingsDrawer.IsEnabled=true;}
    }
    private void Notify(string message,bool error=false){notice.Message=message;notice.Severity=error?InfoBarSeverity.Error:InfoBarSeverity.Success;notice.IsOpen=true;}
    private async Task RenderAsync()
    {
        var revision=++renderRevision;
        var currentRoute=route;var currentSelection=selected;var photo=currentSelection??BrowsePicture(currentRoute);
        IReadOnlyList<SourceDefinition>? sources=null;
        // Prepare asynchronous dependencies while the old screen remains visible.
        if(photo!=null)await PreparePictureAsync(photo,revision);
        if(currentSelection==null&&currentRoute=="sources")sources=await library.Repository.GetSourcesAsync();
        if(revision!=renderRevision)return; // A newer navigation owns the screen.
        var viewKey=currentSelection!=null?"detail:"+currentSelection.Id:currentRoute+":"+photo?.Id;
        // Animate only controls. Fading an entire new picture after removing its
        // predecessor exposes the window backdrop and looks like a blank flash.
        animateViewChrome=displayedView!=viewKey;
        displayedView=viewKey;
        page.Children.Clear();gallery=null;pageViewport.Children.Clear();pageViewport.RowDefinitions.Clear();pageViewport.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});pageViewport.Children.Add(pageScroll);immersive.Children.Clear();homeCard=null;detailImage=null;detailFitMenuItem=null;detailTargets=null;
        var isImmersive=photo!=null||currentRoute is "today" or "discover";
        immersive.Visibility=isImmersive?Visibility.Visible:Visibility.Collapsed;navigation.Visibility=isImmersive?Visibility.Collapsed:Visibility.Visible;
        sidebarToggle.Visibility=currentSelection==null?Visibility.Visible:Visibility.Collapsed;
        sidebarToggle.Foreground=new SolidColorBrush(photo!=null?Colors.White:isImmersive?Colors.Black:shell.ActualTheme==ElementTheme.Dark?Colors.White:Colors.Black);
        foreach(var key in new[]{"ButtonForegroundPointerOver","ButtonForegroundPressed"})sidebarToggle.Resources[key]=sidebarToggle.Foreground;
        SetChrome(photo!=null);
        if(currentSelection!=null){RenderDetail(currentSelection);AttachOperationStatus();return;}
        switch(currentRoute)
        {
            case "today":RenderToday();break;
            case "discover":RenderDiscovery();break;
            case "catalog":RenderGallery(false);break;
            case "library":RenderGallery(true);break;
            case "sources":RenderSources(sources!);break;
            default:RenderToday();break;
        }
        AttachOperationStatus();
    }
    private void Empty(string text)
    {
        var box=new StackPanel{Spacing=16,Margin=new Thickness(0,36,0,20)};box.Children.Add(Text(text,20));box.Children.Add(Text("导入自己的图片，或添加 HTTPS 图片直链 / JSON 清单。"));box.Children.Add(Row(Action("导入图片",ImportFilesAsync,true),Action("导入文件夹",ImportFolderAsync)));page.Children.Add(box);
    }
    private void RenderToday()
    {
        if(daily==null)
        {
            immersive.Children.Add(new Border{Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,233,238,241))});
            if(homeLoading)return;
            var empty=new StackPanel{Spacing=10,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Children={Text("Bing 每日图",24),Text("连接后在这里显示今日高清壁纸。",13),Action("重新加载每日图",()=>RequestHomeSourceAsync("bing",refresh:true),true)}};
            immersive.Children.Add(empty);return;
        }
        RenderImmersiveHome(daily);
    }
    private void RenderGallery(bool local)
    {
        Heading(local?"我的图库":"在线图库",local?"收藏和已保存的原图，留在你自己的电脑里。":"搜索已同步图库；在线图片按需下载，尺寸下载后核实。");
        if(!local)AddOnlineGalleryControls();
        var search=new TextBox{PlaceholderText="搜索标题或图源",Text=query,MinWidth=220};search.TextChanged+=(_,_)=>{query=search.Text;UpdateGallery(local);};
        var favorite=new ToggleSwitch{Header="仅收藏",IsOn=favoritesOnly};favorite.Toggled+=(_,_)=>{favoritesOnly=favorite.IsOn;UpdateGallery(local);};
        page.Children.Add(Row(search,Action("导入图片",ImportFilesAsync,true),Action("导入文件夹",ImportFolderAsync),favorite));
        if(items.Count==0){Empty("图库还没有图片");return;}
        pageViewport.RowDefinitions[0].Height=GridLength.Auto;pageViewport.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        gallery=new GridView{IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.None,Margin=new Thickness(28,0,28,0),Padding=new Thickness(0,0,0,12)};
        // GridView owns scrolling, preserving item/container virtualization.
        gallery.ItemTemplate=(DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Border Width="252" Margin="0,0,12,12" CornerRadius="12" BorderThickness="1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"><StackPanel><Grid Height="150" Background="{ThemeResource LayerFillColorDefaultBrush}"><FontIcon Glyph="&#xE8B9;" FontSize="28"/><Image Source="{Binding Preview}" Stretch="UniformToFill"/></Grid><StackPanel Padding="14" Spacing="5"><TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/><TextBlock Text="{Binding Subtitle}" FontSize="12" TextWrapping="Wrap"/><TextBlock Text="{Binding State}" FontSize="12"/></StackPanel></StackPanel></Border></DataTemplate>
            """);
        gallery.ItemClick+=async (_,e)=>await OpenDetailAsync(((WallpaperCard)e.ClickedItem).Item);Grid.SetRow(gallery,1);pageViewport.Children.Add(gallery);UpdateGallery(local);
        gallery.ContainerContentChanging+=(_,e)=>
        {
            // Container recycling must never retain another wallpaper's command target.
            e.ItemContainer.ContextFlyout=e.InRecycleQueue?null:PictureCommands(((WallpaperCard)e.Item).Item);
        };
    }
    private void UpdateGallery(bool local)
    {
        if(gallery==null)return;
        gallery.ItemsSource=items.Where(x=>(local||catalogSource=="all"||x.SourceId==catalogSource)&&(local||apiResultIds==null||apiResultIds.Contains(x.Id))&&(!local||library.HasOriginal(x)||x.Favorite)&&(!favoritesOnly||x.Favorite)&&(query.Length==0||x.Title.Contains(query,StringComparison.OrdinalIgnoreCase)||x.SourceName.Contains(query,StringComparison.OrdinalIgnoreCase))).Select(x=>new WallpaperCard(x,library.ThumbnailPath(x),library.HasOriginal(x))).ToArray();
    }
    private void RenderDetail(WallpaperItem item)
    {
        RenderImmersiveDetail(item);
    }
    private async Task ImportFilesAsync()
    {
        var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));picker.FileTypeFilter.Add(".jpg");picker.FileTypeFilter.Add(".jpeg");picker.FileTypeFilter.Add(".png");picker.FileTypeFilter.Add(".bmp");
        var files=await picker.PickMultipleFilesAsync();if(files.Count==0)return;
        await ImportAsync(files.Select(f=>f.Path).ToArray());
    }
    private async Task ImportFolderAsync()
    {
        var picker=new FolderPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));picker.FileTypeFilter.Add("*");var folder=await picker.PickSingleFolderAsync();if(folder==null)return;
        await RunAsync(async token=>
        {var files=await Task.Run(()=>Directory.EnumerateFiles(folder.Path).Where(p=>new[]{".jpg",".jpeg",".png",".bmp"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Take(1000).ToArray(),token);await ImportWorkAsync(files,token);});
    }
    private Task ImportAsync(string[] files)=>RunAsync(token=>ImportWorkAsync(files,token));
    private async Task ImportWorkAsync(string[] files,CancellationToken token)
    {
        var result=await library.ImportAsync(files,token);await RefreshAsync();await RenderAsync();Notify($"导入 {result.Imported} 张，重复 {result.Duplicate} 张，失败 {result.Failed} 张。文件夹仅导入当前层，最多1000张。",result.Failed>0);
    }
    private IProgress<TransferProgress> Transfer()
    {var owner=operation;return new Progress<TransferProgress>(p=>{if(owner!=operation||owner?.IsCancellationRequested==true)return;progress.IsIndeterminate=!p.Percentage.HasValue;if(p.Percentage.HasValue)progress.Value=p.Percentage.Value;operationLabel.Text=p.Stage+(p.Percentage.HasValue?$" {p.Percentage:0}%":"…");});}
    private Task FavoriteAsync(WallpaperItem item)=>RunAsync(async _=>{await library.Repository.SetFavoriteAsync(item.Id,!item.Favorite);await RefreshAsync();if(selected!=null)selected=items.First(x=>x.Id==item.Id);await RenderAsync();});
    private Task ExportAsync(WallpaperItem item)=>RunAsync(async token=>
    {
        var ready=await library.AcquireAsync(item.Id,Transfer(),token);var picker=new FileSavePicker{SuggestedFileName=ready.Title};WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));picker.FileTypeChoices.Add("原图",new List<string>{Path.GetExtension(ready.OriginalFile!)});
        var file=await picker.PickSaveFileAsync();if(file!=null){await library.ExportAsync(ready,file.Path,token);Notify("原图副本已保存；内容与原始文件一致。");}else Notify("已取消导出，已获取的原图仍保留在图库。");await RefreshAsync();if(selected!=null)selected=items.First(x=>x.Id==ready.Id);await RenderAsync();
    });
    private Task ApplyLockScreenAsync(WallpaperItem item)=>RunAsync(async token=>
    {
        var ready=await library.AcquireAsync(item.Id,Transfer(),token);token.ThrowIfCancellationRequested();
        await personalization.SetLockScreenAsync(library.OriginalPath(ready)!,library.Root,token);
        await RefreshAsync();if(selected!=null)selected=items.First(x=>x.Id==ready.Id);await RenderAsync();Notify("已设为锁屏壁纸。");
    },"正在设置锁屏壁纸…");
    private Task ApplyAsync(WallpaperItem item)=>RunAsync(async token=>
    {
        var ready=await library.AcquireAsync(item.Id,Transfer(),token);token.ThrowIfCancellationRequested();
        var result=await desktop.ApplyAsync(library.OriginalPath(ready)!,monitorId,fit);snapshot=result.Before;
        Notify(result.Failed.Count==0?$"已设置 {result.Succeeded} 个显示器。可在设置中恢复本次更换前的桌面。":$"成功 {result.Succeeded} 个显示器，失败 {result.Failed.Count} 个。请尝试恢复或重新选择显示器。",result.Failed.Count>0);
        await RefreshAsync();if(selected!=null)selected=items.First(x=>x.Id==ready.Id);await RenderAsync();
    });
    private void RenderSources(IReadOnlyList<SourceDefinition> sources)
    {
        Heading("图源管理","浏览每日摄影、公开壁纸图库和主题；已同步内容可离线使用。");
        page.Children.Add(SourceCards());
        page.Children.Add(Text("自定义图源与推荐范围",20));
        page.Children.Add(Row(Action("添加在线图源",AddSourceAsync,true),Action("导入文件夹",ImportFolderAsync)));
        foreach(var source in sources)
        {
            var toggle=new ToggleSwitch{Header=source.Name,IsOn=source.Enabled};toggle.Toggled+=async (_,_)=>await RunAsync(async _=>{await library.Repository.SaveSourceAsync(source with{Enabled=toggle.IsOn});await RefreshAsync();Notify("图源状态已保存。已有原图仍保留。");});
            var row=new StackPanel{Spacing=8,Padding=new Thickness(16),Background=(Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]};row.Children.Add(toggle);row.Children.Add(Text(source.Kind=="local"?"通过导入复制原图，源文件不会被改动。":source.Address,12));
            row.Children.Add(Action(source.Kind=="bing"?"显示每日图":"随机显示到首页",()=>RequestHomeSourceAsync(source.Id)));
            if(source.Kind!="local")row.Children.Add(Action("刷新清单",()=>RunAsync(async token=>{var count=await library.Sources.RefreshAsync(source,token);await RefreshAsync();Notify($"清单提供 {count} 项；原图按需获取。");})));page.Children.Add(row);
        }
        page.Children.Add(Text("JSON清单：schemaVersion=1，items中包含id、title、originalUrl，可选author与sourcePage。图片大小≤50MB；清单≤2MB。",12));
    }
    private async Task AddSourceAsync()
    {
        var name=new TextBox{Header="名称",MaxLength=60};var address=new TextBox{Header="HTTPS地址",PlaceholderText="https://…"};var kind=new ComboBox{Header="类型",ItemsSource=new[]{"图片直链","JSON清单"},SelectedIndex=0};
        var form=new StackPanel{Spacing=14};form.Children.Add(name);form.Children.Add(kind);form.Children.Add(address);
        var dialog=new ContentDialog{Title="添加在线图源",Content=form,PrimaryButtonText="添加",CloseButtonText="取消",XamlRoot=page.XamlRoot,DefaultButton=ContentDialogButton.Primary};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        await RunAsync(async token=>{var count=await library.Sources.AddAsync(name.Text,kind.SelectedIndex==0?"direct":"manifest",address.Text,token);await RefreshAsync();await RenderAsync();Notify($"图源已添加，包含 {count} 项。原图将在保存或设置桌面时获取。");});
    }
    private async Task LoadBingAsync()
    {
        var dialog=new ContentDialog{Title="使用 Bing 实验图源",Content="该社区接口可能变动。图片著作权属于原作者，请核对图片使用条件。仅在你点击后连接 Bing；下载后才核实尺寸。",PrimaryButtonText="获取",CloseButtonText="取消",XamlRoot=page.XamlRoot};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        await RunAsync(async token=>{var count=await library.Sources.LoadBingAsync(token);await RefreshAsync();await RenderAsync();Notify($"已获取 {count} 项Bing图片信息，在发现页选择图片下载。");});
    }
}

public sealed class WallpaperCard
{
    public WallpaperItem Item{get;}
    public string Title=>Item.Title;
    public string Subtitle=>Item.SourceName+" · "+Item.Dimensions;
    public string State{get;}
    public string? Preview{get;}
    public WallpaperCard(WallpaperItem item,string? path,bool available)
    {Item=item;State=(item.Favorite?"♥ 已收藏 · ":"")+(available?"原图可离线使用":"原图待下载");if(path!=null&&File.Exists(path))Preview=new Uri(path).AbsoluteUri;}
}
