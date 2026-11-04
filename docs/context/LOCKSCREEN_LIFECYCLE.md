# 锁屏副本生命周期

核验：2026-11-04；范围为独立修复分支、隔离文件测试、编译及只读API探测。没有修改真实锁屏，也没有测量用户锁屏目录的实际占用。

## Windows契约与回收边界

微软的[TrySetLockScreenImageAsync](https://learn.microsoft.com/en-us/uwp/api/windows.system.userprofile.userprofilepersonalizationsettings.trysetlockscreenimageasync)规定后续更换必须使用不同文件名，返回true表示成功。文档没有承诺成功后源文件可立即删除。[OriginalImageFile](https://learn.microsoft.com/en-us/uwp/api/windows.system.userprofile.lockscreen.originalimagefile)提供当前文件URI，但流设置等情况可能无法读取。不能把系统缓存路径、非文件URI、读取异常或IsSupported=true当作释放源文件的证据。

实现采用更保守的应用策略：源副本不可变；系统读回必须精确匹配本应用登记的副本，才允许回收旧的已完成副本。保留当前文件和上一张已完成副本，并给其余副本至少7天宽限期；每次删除前重新读回，变化时停止。该宽限期是保护措施，不是Windows提供的释放保证。没有公开原子“查引用并删除”接口，外部程序同时修改状态的竞态无法完全消除，仍需测试账户/VM验收。

## 状态与文件保护

- `lockscreen/owned-v1.json`只登记本版本创建的`stillframe-<GUID>.<ext>`文件；删除前核对SHA256，内容变化则保留；原子替换前Flush(true)，根目录、清单及删除候选拒绝重解析链接。不遍历图库、不递归删除目录、不写其他设置。
- 先持久化所有权、复制原图，再持久化Submitted意图，最后调用系统；进入系统调用后等待其结果，不用取消令牌中断等待。
- 提交前取消或API明确返回false：清理本次未生效候选；若系统读回反而指向候选则保留。删除/写盘失败优先留文件。
- 提交后异常或确认清单写入失败：保留可能已生效的文件与未确认意图。启动时和成功更换后尝试核对；只有系统精确读回该文件才确认，未知结果不回收。
- 无清单的旧GUID副本、未知文件和损坏清单全部保留。最多登记1000项，无法确认回收时拒绝继续积累，不以删除不明文件腾出空间。

`LibraryService.InitializeAsync`继续只管理下载暂存；WinRT查询与回收入口在`PersonalizationService.InitializeLockScreenAsync`，由窗口初始化调用。图库核心不引入Windows依赖。手动设置与自动任务均经同一串行入口。

## 验证与接续

核心检查通过85项（原64项+21项副本回归），使用独立临时目录和系统接口替身（另验证已登记路径被用户替换内容时不删除）：成功、拒绝、早/晚取消、异常后可能生效、重复更换、重新打开存储、未知/缓存读回、宽限期、清单损坏/路径穿越、写盘失败及并发。App Release publish通过。

`Stillframe.exe --lockscreen-readcheck <报告路径>`只读取URI类型，不写锁屏、清单或用户图库，不记录源路径。本机结果file-uri=False，验证了回收必须延期的条件，不证明真实替换或文件释放。完整日志保存在本地`artifacts/validation/personalization-20261104/`，不随源码上传。

后续实际验证须由用户另行授权测试账户/VM：设置A→B→C、重启、取消/拒绝、锁定/解锁和系统手动更换；确认系统读回与源路径关系，再验证符合策略的旧副本删除不影响显示。没有授权时不执行实际系统更换或清空既有副本。
