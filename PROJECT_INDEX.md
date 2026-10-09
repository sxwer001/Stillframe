# 项目索引

定位：Windows 静态壁纸软件项目，已建立WinUI 3初版。用户确认的产品边界见 [已确认决策](docs/context/DECISIONS.md#已确认决策)，当前完成度见 [当前状态](docs/context/NOW.md#当前进展)。

## 条件路由

先遵守 [AGENTS.md](AGENTS.md)。只在任务需要定位时使用本索引；选中入口后继续定位相关标题/符号/关键词，不顺序读取全部链接。

| 当前任务 | 优先入口 |
| --- | --- |
| 接续工作、确定下一步与阻塞 | [NOW](docs/context/NOW.md) |
| 了解用途和基本使用 | [README](README.md) |
| 定位文件、版本、配置或部署对象 | [MAP](docs/context/MAP.md) |
| 执行文档/原型检查或寻找运行方法 | [RUNBOOK](docs/context/RUNBOOK.md)，先看对应操作的前置条件 |
| 打包便携ZIP或清理生成文件 | [打包/清理方法](docs/context/RUNBOOK.md#便携zip打包与项目清理)、[package.ps1](scripts/package.ps1)、[clean.ps1](scripts/clean.ps1)；交付位置见MAP，当前结果见NOW |
| 自签名MSIX、证书安装、公开发布 | [MSIX方法](docs/context/RUNBOOK.md#试用版msix与公开发布)、[打包](scripts/package-msix.ps1)、[签名与载荷校验](scripts/verify-msix.ps1)、[清单](packaging/AppxManifest.xml)；安装说明见README，实际发布状态见NOW |
| 核对哪些选择已获用户确认 | [DECISIONS](docs/context/DECISIONS.md) |
| 处理图源许可、多屏、验证或可移植性问题 | [RISKS](docs/context/RISKS.md)，只读对应条目 |
| 页面、交互与组件设计 | [开发方案 §2](docs/Windows壁纸软件-详细开发方案.html#ui)；流程见 [§3](docs/Windows壁纸软件-详细开发方案.html#flow) |
| 修改初版界面、启动与桌面接口 | [MainWindow](src/Stillframe.App/MainWindow.cs)（图库/管理/切页）、[ImmersiveView](src/Stillframe.App/ImmersiveView.cs)（首页/详情/图片准备）、[DetailToolbar](src/Stillframe.App/DetailToolbar.cs)（底部图标/更多）、[SettingsDrawer](src/Stillframe.App/SettingsDrawer.cs)（常驻侧栏/导航/设置）、[PictureCommands](src/Stillframe.App/PictureCommands.cs)（图片右键快捷操作）、[PresentationChecks](src/Stillframe.App/PresentationChecks.cs)（布局/导航回归）、[Program](src/Stillframe.App/Program.cs)、[DesktopService](src/Stillframe.App/Services/DesktopService.cs)，先定位对应函数 |
| 修改导入、下载、图源及仓储 | [LibraryService](src/Stillframe.Core/LibraryService.cs)、[SourceService](src/Stillframe.Core/SourceService.cs)、[LibraryRepository](src/Stillframe.Core/Storage/LibraryRepository.cs)，按用例选取 |
| 公共API、在线搜索/翻页、图源侧栏 | [BuiltInSources](src/Stillframe.Core/BuiltInSources.cs)、[OnlineSourcesView](src/Stillframe.App/OnlineSourcesView.cs)；条款及字段证据见 [接口核验](research/source-api-evidence-2026-10-09.json)，真实网络/WIC检查入口见 [OnlineWindowsChecks](src/Stillframe.App/OnlineWindowsChecks.cs) |
| Bing历史图库、手动锁屏、图库剩余高度 | 历史解析/缓存：[BuiltInSources](src/Stillframe.Core/BuiltInSources.cs)、[SourceService](src/Stillframe.Core/SourceService.cs)；手动入口：[PictureCommands](src/Stillframe.App/PictureCommands.cs)、[DetailToolbar](src/Stillframe.App/DetailToolbar.cs)；布局/执行：[MainWindow](src/Stillframe.App/MainWindow.cs)，只定位对应函数 |
| 首页Bing每日图、随机切源及区域加载提示 | [HomeImageService](src/Stillframe.Core/HomeImageService.cs)（选图/每日缓存）、[HomeLoadingView](src/Stillframe.App/HomeLoadingView.cs)（取消/最新请求/区域提示）；原生回归见 [PresentationChecks](src/Stillframe.App/PresentationChecks.cs) 的CheckHomeLoadingAsync |
| 发现页大图、滚轮前后浏览与侧栏展开选源 | [WallpaperBrowser](src/Stillframe.App/WallpaperBrowser.cs)（独立会话历史/滚轮）、[SettingsDrawer](src/Stillframe.App/SettingsDrawer.cs)（发现展开项）、[OnlineSourcesView](src/Stillframe.App/OnlineSourcesView.cs) 的DiscoverySourceChoices；回归见CheckWallpaperBrowserAsync |
| C#完整规划分层、服务接口 | [开发方案 §4](docs/Windows壁纸软件-详细开发方案.html#architecture)、[§5](docs/Windows壁纸软件-详细开发方案.html#contract)，不把全部规划当作当前工程 |
| 图源、自定义清单协议 | [开发方案 §6](docs/Windows壁纸软件-详细开发方案.html#source) |
| 修改运行数据模型 | [实际DDL](src/Stillframe.Core/Storage/schema.sql)与[仓储](src/Stillframe.Core/Storage/LibraryRepository.cs)，按表/迁移逻辑读取 |
| 数据模型完整规划 | [开发方案 §7](docs/Windows壁纸软件-详细开发方案.html#data) → [候选 DDL](docs/database-schema.sql)，与当前4表运行结构区分 |
| 下载、缓存与保存原图 | [开发方案 §8](docs/Windows壁纸软件-详细开发方案.html#download) |
| 桌面接口与多显示器 | [开发方案 §9](docs/Windows壁纸软件-详细开发方案.html#desktop) |
| 每日推荐、自动更换与通知 | 当前实现：[DailyAutomation](src/Stillframe.Core/DailyAutomation.cs)、[AutomationView](src/Stillframe.App/AutomationView.cs)、[PersonalizationService](src/Stillframe.App/Services/PersonalizationService.cs)；完整规划：[开发方案 §10](docs/Windows壁纸软件-详细开发方案.html#daily) |
| 性能、打包、排期、验收 | 开发方案 [§11](docs/Windows壁纸软件-详细开发方案.html#quality)、[§12](docs/Windows壁纸软件-详细开发方案.html#delivery)、[§13](docs/Windows壁纸软件-详细开发方案.html#schedule)、[§14](docs/Windows壁纸软件-详细开发方案.html#acceptance)，按问题选择 |
| GitHub类似项目及源码证据 | [可行性报告](Windows壁纸软件-可行性分析报告.html)相关章节；需要核对证据时限定查找 [研究记录](research/evidence-2026-10-08.json) 中的对应仓库/发现 |
| 追溯已替代方案或旧进展 | 先搜索 [历史索引](docs/context/history/README.md)，无匹配则不扩大读取 |

## 写入路由

长期规则写 AGENTS；当前状态写 NOW；文件/版本关系写 MAP；方法写 RUNBOOK；确认选择写 DECISIONS；风险证据写 RISKS。具体开发规格定点修改既有详细方案，DDL修改对应SQL；调研新证据更新对应研究条目，其他文档只保留摘要链接。新入口或职责变化时再修订本索引。

运行行为事实以源码和对应范围的当前测试为证据；实际DDL只在src中的schema维护，候选DDL继续保存完整设计，避免互相复制。行为变化时更新README和NOW相关摘要，不把完整实现复制到专题文档。
