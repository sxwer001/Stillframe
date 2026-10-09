using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Stillframe.Core;
using Windows.Storage;
using Windows.System.UserProfile;

namespace Stillframe.App.Services;

public sealed class PersonalizationService : IDisposable
{
    private bool registered;
    public event Action? NotificationClicked;
    public static bool LockScreenSupported{get{try{return UserProfilePersonalizationSettings.IsSupported();}catch{return false;}}}
    public static bool NotificationsSupported{get{try{return AppNotificationManager.IsSupported();}catch{return false;}}}
    public async Task SetLockScreenAsync(string original,string root,CancellationToken token)
    {
        if(!LockScreenSupported)throw new AppException("当前Windows不支持应用设置锁屏，请在系统个性化设置中选择图片。");
        var folder=Path.Combine(root,"lockscreen");Directory.CreateDirectory(folder);
        // Windows requires a fresh file name for subsequent lock-screen images.
        var target=Path.Combine(folder,Guid.NewGuid().ToString("N")+Path.GetExtension(original));File.Copy(original,target);
        try
        {
            token.ThrowIfCancellationRequested();var file=await StorageFile.GetFileFromPathAsync(target);token.ThrowIfCancellationRequested();
            if(!await UserProfilePersonalizationSettings.Current.TrySetLockScreenImageAsync(file))throw new AppException("Windows拒绝了锁屏设置，请检查个性化权限或组织策略。");
        }
        catch{File.Delete(target);throw;}
    }
    public void ShowDaily(WallpaperItem item)
    {
        if(!NotificationsSupported)throw new AppException("当前运行方式不支持系统通知，请以普通权限运行。");
        if(!registered)
        {
            AppNotificationManager.Default.NotificationInvoked+=OnInvoked;
            try{AppNotificationManager.Default.Register();registered=true;}
            catch{AppNotificationManager.Default.NotificationInvoked-=OnInvoked;throw new AppException("系统通知注册失败，请检查Windows通知服务。");}
        }
        if(AppNotificationManager.Default.Setting!=AppNotificationSetting.Enabled)throw new AppException("拾景通知被系统关闭，请在Windows通知设置中允许。");
        var notification=new AppNotificationBuilder().AddText("拾景 · 今日推荐").AddText(item.Title).AddArgument("action","today").BuildNotification();
        notification.Tag="daily";notification.Group="recommendations";AppNotificationManager.Default.Show(notification);
        if(notification.Id==0)throw new AppException("通知未被系统接收，请检查Windows通知设置。");
    }
    private void OnInvoked(AppNotificationManager sender,AppNotificationActivatedEventArgs args)=>NotificationClicked?.Invoke();
    public void Dispose(){if(registered){AppNotificationManager.Default.NotificationInvoked-=OnInvoked;AppNotificationManager.Default.Unregister();registered=false;}}
}
