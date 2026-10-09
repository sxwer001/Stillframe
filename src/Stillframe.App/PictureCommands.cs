using Microsoft.UI.Xaml.Controls;
using Stillframe.Core;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    // Native ContextFlyout, as in WinUI Gallery's ListView sample. No custom popup/animation.
    private MenuFlyout PictureCommands(WallpaperItem item)
    {
        var menu=new MenuFlyout();
        MenuFlyoutItem Command(string label,Symbol symbol,Func<Task> action)
        {
            var command=new MenuFlyoutItem{Text=label,Icon=Glyph(symbol)};
            command.Click+=async (_,_)=>await action();menu.Items.Add(command);return command;
        }
        Command(route=="discover"?"下一张壁纸":homeSourceId=="bing"?"重新加载每日图":"换一张推荐",Symbol.Refresh,NextRecommendationAsync);
        if(selected==null&&route is "today" or "discover")
        {
            var previous=Command("上一张壁纸",Symbol.Back,()=>StepWallpaperAsync(-1));previous.IsEnabled=HistoryFor(route).Position>0;
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Command("查看图片介绍",Symbol.Caption,()=>OpenDetailAsync(items.FirstOrDefault(x=>x.Id==item.Id)??item));
        var favorite=Command("收藏",Symbol.Favorite,()=>FavoriteAsync(items.FirstOrDefault(x=>x.Id==item.Id)??item));
        Command("设为壁纸",Symbol.Setting,()=>ApplyAsync(item));
        var lockScreen=Command("设为锁屏壁纸",Symbol.ProtectedDocument,()=>ApplyLockScreenAsync(item));lockScreen.IsEnabled=Services.PersonalizationService.LockScreenSupported;
        Command("保存原图",Symbol.Download,()=>ExportAsync(item));
        menu.Items.Add(new MenuFlyoutSeparator());
        Command("设置",Symbol.Setting,OpenSettingsAsync);
        menu.Opening+=(_,_)=>favorite.Text=(items.FirstOrDefault(x=>x.Id==item.Id)??item).Favorite?"取消收藏":"收藏";
        return menu;
    }

    private Task NextRecommendationAsync()=>route=="discover"?StepWallpaperAsync(1):RequestHomeSourceAsync(homeSourceId,homeSearch,homeOrder,refresh:true);
}
