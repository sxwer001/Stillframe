# 目录与运行对象映射

核验：2026-10-09；本轮范围为新增API/每日任务模块、SQLite迁移、设置及本地输出关系；其余配置沿用此前证据。下表是条件定位关系，不是必读清单。

## 现有目录

| 位置 | 对象 / 职责 |
| --- | --- |
| 项目根目录 | AGENTS、PROJECT_INDEX、README，以及既有可行性HTML报告 |
| docs/Windows壁纸软件-详细开发方案.html | 开发设计规格；新工程目录只是方案示例 |
| docs/database-schema.sql | 候选SQLite v1结构，不是已部署数据库 |
| docs/context/ | 接续与治理专题；分工见 [AGENTS](../../AGENTS.md#唯一职责与更新时机) |
| docs/context/history/ | 历史索引；当前无必要归档 |
| research/evidence-2026-10-08.json | 前期调研记录；顶层包含checkedOn、timezone、method、repositories、criticalFindings，按对象检索 |
| research/ui-reference-2026-10-08.json | D09侧栏、ContextFlyout及原生过渡的官方实现参考；保存来源URL、命中符号、应用范围和核验边界 |
| research/source-api-evidence-2026-10-09.json | 本轮API、图片授权限制、锁屏及通知官方接口的核验来源与边界 |
| research/preview-validation/ | 原型/DDL校验脚本与对应依赖声明、lockfile |
| research/preview-validation/node_modules/ | 校验工具依赖目录；不默认读取，不能当作产品工程 |
| Stillframe.slnx | Core、WinUI App与核心检查控制台的解决方案入口 |
| src/Stillframe.Core/ | 不依赖UI的模型、SQLite仓储、图源与原图服务；运行DDL以Storage/schema.sql为准 |
| src/Stillframe.App/ | App.xaml；MainWindow.cs管理页/异步切页/检查入口；ImmersiveView.cs首页/详情及单张原图解码复用；DetailToolbar.cs底部图标栏/更多；SettingsDrawer.cs常驻SplitView导航/设置及外观保存；PictureCommands.cs原生图片ContextFlyout；PresentationChecks.cs独立布局/导航回归；Services包含WIC与桌面COM |
| tests/Stillframe.Core.Checks/ | 确定性服务检查；临时数据独立于用户图库 |
| src/Stillframe.Core/BuiltInSources.cs、DailyAutomation.cs | 公共API适配及解析；每日调度、成功去重与失败重试 |
| src/Stillframe.Core/SourceService.cs | Bing社区历史存档元数据会话缓存与图源入库；LibraryService持有同一服务实例以复用缓存 |
| src/Stillframe.Core/HomeImageService.cs、src/Stillframe.App/HomeLoadingView.cs | 首页Bing每日图缓存、单张随机选图；区域加载状态、取消、最新请求与原图替换 |
| src/Stillframe.App/WallpaperBrowser.cs | 首页/发现独立大图会话、滚轮累计/节流、会话内ID浏览历史及前后原图复用；不写入持久历史表 |
| src/Stillframe.App/OnlineSourcesView.cs、AutomationView.cs、Services/PersonalizationService.cs | 在线检索与图源卡片；运行期间定时器和设置；Windows锁屏/通知接口 |
| src/Stillframe.App/OnlineWindowsChecks.cs | 独立真实API/原图/WIC检查，不调用系统更换接口 |
| scripts/ | build.ps1、run.ps1、check.ps1；package.ps1生成带清单/校验的便携ZIP；clean.ps1仅清理限定生成目录，支持WhatIf；执行条件见RUNBOOK |
| artifacts/Stillframe-win-x64/ | 本地便携Release输出，直接运行Stillframe.exe；非安装包 |
| artifacts/releases/ | Stillframe-0.1.0-win-x64.zip、.msix及SHA256；公开CER与安装脚本，ZIP含使用说明/文件清单，均含第三方许可 |
| packaging/ | MSIX身份/能力清单，原创图标及可重生成脚本；Publisher为CN=Stillframe，Identity为Stillframe.Desktop |
| docs/screenshots/ | 原生WinUI截图，来自独立诊断图库与原创演示图 |
| artifacts/validation/ | 当前构建检查记录与UI自身渲染图；不默认读取或作为源码 |
| .tools/ | 本次项目内.NET SDK、官方下载脚本与临时包缓存；不默认读取，不属于产品分发 |

Git状态（2026-10-09）：按D16初始化main分支，公开仓库为[Stillframe](https://github.com/sxwer001/Stillframe)，origin使用该仓库HTTPS地址。当前上传/Release结果以NOW为准；公开提交使用GitHub noreply地址。

## 版本与配置

| 对象 | 已有信息 | 状态边界 |
| --- | --- | --- |
| 初版工程 | C#，SDK10.0.401；Windows App SDK1.8.260921001；Microsoft.Data.Sqlite10.0.12 | global.json、csproj及packages.lock.json锁定；最低目标Windows10 build19041，其他机器兼容性待验证 |
| SQLite候选结构 | SQL设置user_version=1 | 尚未建立运行数据库；不得每次启动重跑建表脚本 |
| 实际SQLite结构 | src/Stillframe.Core/Storage/schema.sql：sources/items/daily/settings；user_version=2，items增加preview_url | 仓储从旧版本定点迁移，保留原图引用和收藏；与13表设计稿用途不同，不互相覆盖 |
| 原型验证依赖 | package.json声明linkedom `^0.18.12`，已有package-lock.json | 声明不等于当前安装完整/当前执行成功；需要时核对对应lock条目 |
| 设置与数据 | 外观与automation JSON在settings表；automation.last.desktop/lockscreen/notification保存成功日期；home.bing.id/date保存最近成功的Bing每日图与本地检查日 | 默认自动动作关闭、09:00检查，自动任务图源all；首页会话默认bing，切源不写入自动任务配置。没有API key、云账号或部署服务器 |
| NuGet | NuGet.Config使用Microsoft dotnet-public公共源 | nuget.org在本机TLS失败；2.5缺Search依赖，故当前选择1.8；新机器需实际网络还原验证 |

## 原型与验证工具

- UI预览存于此前会话的可视化区域，项目内没有对应HTML源文件。它不适合作为跨设备接续入口；不在项目文档复制个人设备绝对路径。
- [check-preview.cjs](../../research/preview-validation/check-preview.cjs) 目前硬编码线程外的原型绝对路径；[check-deliverables.py](../../research/preview-validation/check-deliverables.py) 目前硬编码项目绝对路径。当前只能作为既有验证方法参考，移植前需确认输入路径并定点修正。
- [check-context.py](../../research/preview-validation/check-context.py) 是本次新增的文档结构/直接链接检查器，根目录从脚本位置推导，不依赖个人设备路径。
- 导出原型或修改旧校验脚本需要相应任务授权；没有项目内原型时，不声称他人clone后可重跑UI验证。

## 运行与部署位置

初版数据位于 `%LOCALAPPDATA%/Shijing/`：library.db、originals、thumbnails、staging；启用锁屏设置后另有lockscreen专用副本。不自动删除原图。便携输出包含.NET与Windows App SDK本地运行依赖，整个目录一起保留，不只复制exe。

Debug输出为 `src/Stillframe.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/`；正式本地试用提供便携ZIP与自签名MSIX。共享图库目录保留旧名称；MSIX关闭文件系统写入虚拟化，并在Windows11精确排除LocalAppData/Shijing，避免包内副本与便携图库分叉。ARM64、自动更新与商店分发未实现。原型输入限制仍见上一节。

本轮打包后删除了限定bin/obj、解压验证副本和诊断图库；正式Release目录、ZIP、SDK与检查报告/截图保留。src中的bin/obj是可重建位置，不表示当前存在。清理范围与当前结果见RUNBOOK/NOW，不影响用户数据位置。
