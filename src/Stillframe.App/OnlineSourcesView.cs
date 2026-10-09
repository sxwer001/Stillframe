using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stillframe.Core;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    private string catalogSource="all",apiQuery="",apiOrder="weekly";
    private int apiPage=1,apiLastPage=1;
    private HashSet<string>? apiResultIds;
    private readonly Dictionary<string,DateTimeOffset> lastApiRequest=[];

    private async Task BrowseApiAsync(string id,int pageNumber=1,string search="",string order="weekly")
    {
        if(homeLoading){CancelHomeRequest();await homeLoadTask;}
        await RunAsync(async token=>
    {
        if(lastApiRequest.TryGetValue(id,out var last)&&DateTimeOffset.UtcNow-last<TimeSpan.FromSeconds(2))throw new AppException("请稍等两秒再刷新图源。");
        lastApiRequest[id]=DateTimeOffset.UtcNow;
        var batch=await library.Sources.LoadBuiltInAsync(id,pageNumber,search,order,token);
        catalogSource=id;apiPage=batch.Page;apiLastPage=batch.LastPage;apiQuery=search;apiOrder=order;apiResultIds=batch.Items.Select(x=>x.Id).ToHashSet();
        var previews=await library.CachePreviewsAsync(batch.Items,token);
        await RefreshAsync();route="catalog";selected=null;query="";favoritesOnly=false;CloseSettings();
        synchronizingNavigation=true;try{navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().First(x=>x.Tag as string=="library");}finally{synchronizingNavigation=false;}
        await RenderAsync();Notify($"已获取 {batch.Items.Count} 张图片，预览就绪 {previews.Ready} 张"+(previews.Failed>0?$"，{previews.Failed} 张预览失败，可重试。":"。点击图片查看高清原图。"),previews.Failed>0);
        });
    }
    private void AddOnlineGalleryControls()
    {
        var names=new[]{"全部已收录","Bing每日图","Bing历史图库","Wallhaven","Commons每日精选"};var ids=new[]{"all","bing","bing-archive","wallhaven","commons"};
        var source=new ComboBox{ItemsSource=names,SelectedIndex=Array.IndexOf(ids,catalogSource),MinWidth=160};AutomationProperties.SetName(source,"在线图源");
        source.SelectionChanged+=(_,_)=>{if(source.SelectedIndex<0)return;catalogSource=ids[source.SelectedIndex];apiPage=1;apiLastPage=1;apiResultIds=null;UpdateGallery(false);};
        var keywords=new TextBox{Text=apiQuery,PlaceholderText="Wallhaven 搜索词",MaxLength=100,MinWidth=180};AutomationProperties.SetName(keywords,"在线搜索词");
        var sorting=new ComboBox{ItemsSource=new[]{"本周热门","最新上传"},SelectedIndex=apiOrder=="latest"?1:0,MinWidth=120};AutomationProperties.SetName(sorting,"Wallhaven排序");
        Task Fetch(int number)=>catalogSource=="all"?Task.FromException(new AppException("请选择一个在线图源。")):BrowseApiAsync(catalogSource,number,keywords.Text,sorting.SelectedIndex==1?"latest":"weekly");
        var fetch=Action("获取 / 刷新",()=>catalogSource=="all"?BrowseApiAsync("bing"):Fetch(1),true);
        var previous=Action("上一页",()=>Fetch(Math.Max(1,apiPage-1)));previous.IsEnabled=catalogSource!="all"&&apiPage>1;
        var next=Action("下一页",()=>Fetch(apiPage+1));next.IsEnabled=catalogSource!="all"&&apiPage<apiLastPage;
        keywords.IsEnabled=sorting.IsEnabled=catalogSource=="wallhaven";
        var pageLabel=Text(catalogSource=="all"?"选择图源后获取图片。已同步内容可离线浏览；原图在点击时下载。":$"第 {apiPage} / {apiLastPage} 页 · 原图尺寸下载后核实。",12);
        source.SelectionChanged+=(_,_)=>{keywords.IsEnabled=sorting.IsEnabled=catalogSource=="wallhaven";previous.IsEnabled=next.IsEnabled=false;pageLabel.Text="已切换图源，点击获取 / 刷新浏览在线图片。";};
        page.Children.Add(Row(source,keywords,sorting,fetch,previous,next));
        page.Children.Add(pageLabel);
    }
    private StackPanel SourceCards()
    {
        var panel=new StackPanel{Spacing=12,RequestedTheme=ElementTheme.Light};
        foreach(var (id,name,description) in new[]{("bing","Bing 每日图","每天发现一个新地方，浏览高清摄影。"),("commons","Wikimedia Commons","每日精选摄影，保留作者与图片许可。"),("wallhaven","Wallhaven","高清壁纸 · 本周热门、搜索与分类 · 仅SFW。")})
            panel.Children.Add(SettingCard(name,description,new Expander{Header="浏览图片",HorizontalAlignment=HorizontalAlignment.Stretch,Content=Row(Action(id=="bing"?"显示每日图":"随机显示到首页",()=>RequestHomeSourceAsync(id),true),Action("浏览图库",()=>BrowseApiAsync(id)))}));
        panel.Children.Add(SettingCard("Bing 历史图库","社区保存的往期每日图 · 仅限个人壁纸使用。",new Expander{Header="浏览历史",HorizontalAlignment=HorizontalAlignment.Stretch,Content=Row(Action("随机显示到首页",()=>RequestHomeSourceAsync("bing-archive")),Action("浏览历史图库",()=>BrowseApiAsync("bing-archive")))}));
        var topics=new StackPanel{Spacing=8};foreach(var topic in new[]{("风景","landscape"),("动漫","anime"),("城市","city"),("太空","space")})topics.Children.Add(Action(topic.Item1,()=>RequestHomeSourceAsync("wallhaven",topic.Item2)));
        panel.Children.Add(SettingCard("主题与本周热门","由Wallhaven公开分类与热门结果提供。",new Expander{Header="主题精选",Content=topics,HorizontalAlignment=HorizontalAlignment.Stretch}));
        panel.Children.Add(DrawerCardExpander("Unsplash","官方API不适用于壁纸应用；提供官网入口。",new HyperlinkButton{Content="打开 Unsplash 官网",NavigateUri=new Uri("https://unsplash.com")}));
        panel.Children.Add(DrawerCardExpander("故宫博物院","官方资料入口；尚未接入公开壁纸API。",new HyperlinkButton{Content="打开故宫博物院官网",NavigateUri=new Uri("https://www.dpm.org.cn")}));
        panel.Children.Add(Text("Windows聚焦、拾光、彼岸图网、花猫壁纸、蔚蓝主页和Pixiv尚未接入；可导入你有权使用的本地图片、HTTPS直链或JSON清单。",12));
        return panel;
    }
    private StackPanel DiscoverySourceChoices()
    {
        var panel=new StackPanel{Spacing=8};
        Button AddChoice(string label,string key,Func<Task> action)
        {
            var button=new Button{Content=label,Tag=key,FontSize=13,MinHeight=42,Padding=new Thickness(12,8,12,8),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,VerticalContentAlignment=VerticalAlignment.Center,CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1)};
            AutomationProperties.SetName(button,"发现图源 "+label);ToolTipService.SetToolTip(button,label);
            button.Resources["ButtonBackgroundPointerOver"]=new SolidColorBrush(Windows.UI.Color.FromArgb(220,255,255,255));button.Resources["ButtonBorderBrushPointerOver"]=SidebarCardStroke();
            button.Resources["ButtonBackgroundPressed"]=new SolidColorBrush(Windows.UI.Color.FromArgb(235,235,235,235));button.Resources["ButtonBorderBrushPressed"]=SidebarCardStroke();
            button.Click+=async (_,_)=>{discoverySelectionKey=key;PaintDiscoveryChoices();await action();};panel.Children.Add(button);PaintDiscoveryChoice(button);return button;
        }
        foreach(var (id,label,order) in new[]{("bing","Bing 历史图库","weekly"),("wallhaven","Wallhaven 本周热门","weekly"),("wallhaven","Wallhaven 最新","latest"),("commons","Commons 每日精选","weekly"),("local","本地图库","weekly")})
        {
            AddChoice(label,$"{id}|{order}|",()=>RequestDiscoverySourceAsync(id,order:order));
        }
        foreach(var source in items.Where(x=>x.SourceId is not ("bing" or "bing-archive" or "wallhaven" or "commons" or "local")).GroupBy(x=>x.SourceId))
        {var id=source.Key;AddChoice(source.First().SourceName,$"{id}|weekly|",()=>RequestDiscoverySourceAsync(id));}
        var keywords=new TextBox{PlaceholderText="Wallhaven 搜索词",Text=discoverySearch,MaxLength=100,FontSize=13,MinHeight=42,Padding=new Thickness(12,8,12,8),Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(0)};AutomationProperties.SetName(keywords,"发现搜索词");
        panel.Children.Add(new Border{MinHeight=42,CornerRadius=new CornerRadius(10),Padding=new Thickness(2),BorderThickness=new Thickness(1),BorderBrush=SidebarCardStroke(),Background=SidebarCardBackground(),Child=keywords});
        AddChoice("搜索并显示",$"wallhaven|weekly|{discoverySearch}",()=>RequestDiscoverySourceAsync("wallhaven",keywords.Text));
        AddChoice("图库浏览与图源管理","manage",async ()=>{CloseSettings();await NavigateAsync("sources");});
        return panel;
    }
    private string discoverySelectionKey="wallhaven|weekly|";
    private void PaintDiscoveryChoice(Button button)
    {
        button.Background=SidebarCardBackground();
        button.BorderBrush=SidebarCardStroke();
        button.Foreground=new SolidColorBrush(Colors.Black);
    }
    private void PaintDiscoveryChoices()
    {if(discoveryExpander?.Content is StackPanel panel)foreach(var button in panel.Children.OfType<Button>())PaintDiscoveryChoice(button);}
}
