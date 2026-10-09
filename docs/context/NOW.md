# 当前目标与状态

核验：2026-10-09；本次范围为英文工程改名、原创相册封面图标、README截图、自签名MSIX/便携ZIP与公开GitHub试用发布。实际发布结果以本文件对应行和远端证据为准；没有执行真实系统壁纸/锁屏更换测试。

## 当前目标

按D16交付“拾景 · Stillframe”0.1.0 x64试用版：中文名与原图库目录保留，工程/命名空间使用Stillframe；提供自签名MSIX、公开CER、固定证书的安装脚本、便携ZIP和摘要。README已精简并加入真实原生截图，原创相册图标已用于EXE与MSIX。最终包复验通过，公开GitHub仓库已创建，源码和Release上传仍在执行，完成后定点更新下表。完整设计继续使用 [详细开发方案](../Windows壁纸软件-详细开发方案.html)，当前完成度以本文件为准。

## 当前进展

| 对象 | 当前状态 | 证据入口 / 范围 |
| --- | --- | --- |
| GitHub调研与可行性报告 | 已有资料；本次沿用，未重新核实外部仓库/接口 | [报告](../../Windows壁纸软件-可行性分析报告.html)、[调研证据](../../research/evidence-2026-10-08.json) |
| 开发方案 | 已有16节设计，属于计划/建议 | [方案](../Windows壁纸软件-详细开发方案.html)，按对应章节读取 |
| 候选数据库结构 | 已有13张表的DDL；尚未接入应用 | [SQL](../database-schema.sql) |
| UI原型 | 上次任务交付了五页、两种布局的交互预览；操作为模拟 | [位置与访问边界](MAP.md#原型与验证工具) |
| WinUI初版代码 | 保留本地闭环、桌面COM及外观；新增Bing、Wallhaven、Commons公开图源，搜索/翻页/主题、预览缓存与按需原图，不等于完整P0 | [工程映射](MAP.md)、[使用入口](../../README.md)、[接口证据](../../research/source-api-evidence-2026-10-09.json) |
| 首页/详情与侧栏 | 横杠开紧凑导航侧栏，设置表单适当加宽；使用数据项路由修复点击转换异常。首页/详情/图库的右键快捷操作、常驻面板、原图复用及异步切页保护保留 | [界面](../../src/Stillframe.App/ImmersiveView.cs)、[侧栏](../../src/Stillframe.App/SettingsDrawer.cs)、[快捷菜单](../../src/Stillframe.App/PictureCommands.cs) |
| 侧栏新增与样式统一 | 导航项、图源折叠卡和设置统一圆角/描边/半透明表面，减少嵌套框；240 DIP导航宽度保留 | [侧栏](../../src/Stillframe.App/SettingsDrawer.cs)、[图源UI](../../src/Stillframe.App/OnlineSourcesView.cs) |
| 本次布局与来源修订 | 图库图片列表使用剩余高度、独立滚动；导航和发现选源去掉常驻蓝色选中效果；Bing浏览随机选择社区历史图并排除已看ID，每日缓存独立 | [图库布局](../../src/Stillframe.App/MainWindow.cs)、[图源UI](../../src/Stillframe.App/OnlineSourcesView.cs)、[随机筛选](../../src/Stillframe.Core/HomeImageService.cs) |
| 首页/发现选图与加载 | 首页启动Bing每日图；发现初次Wallhaven，侧栏展开选择图源/搜索。区域进度可取消，最新请求生效，失败保留旧图；显式在线列表移到图源管理的浏览按钮，图库管理保留 | [选图服务](../../src/Stillframe.Core/HomeImageService.cs)、[加载协调](../../src/Stillframe.App/HomeLoadingView.cs) |
| 滚轮浏览 | 两页分别记录本次运行看过的图片ID，下滑向前、上滑返回，前进历史原图复用；随机选择排除已看图，其他分页来源需要时读取后续页。累计/节流，加载/侧栏/详情期间不切图；离开发现取消下载。Bing历史图浏览不覆盖每日缓存 | [浏览状态与滚轮](../../src/Stillframe.App/WallpaperBrowser.cs)、[去重选择](../../src/Stillframe.Core/HomeImageService.cs)；物理鼠标验收待确认 |
| 手动锁屏 | 图片右键和详情更多菜单均可按需获取原图并调用既有Windows锁屏服务；当前只验证入口和编译，未修改用户锁屏 | [图片命令](../../src/Stillframe.App/PictureCommands.cs)、[Windows调用](../../src/Stillframe.App/Services/PersonalizationService.cs) |
| 每日任务 | 默认关闭；运行中按执行小时检查，成功日期去重，失败15分钟重试。桌面/锁屏/通知分别启用，Windows效果未实测 | [调度](../../src/Stillframe.Core/DailyAutomation.cs)、[原生设置](../../src/Stillframe.App/AutomationView.cs)、[Windows调用](../../src/Stillframe.App/Services/PersonalizationService.cs) |
| Release构建与试用包 | 已生成英文工程的0.1.0 x64 ZIP及自签名MSIX；保留第三方许可/NOTICE。MSIX额外携带resources.pri，修复安装后的主题字典加载问题；发布状态单列 | [MSIX方法](RUNBOOK.md#试用版msix与公开发布)、[交付映射](MAP.md#运行与部署位置) |
| 生成文件清理 | 已删除21个限定目录，文件大小合计1088.7 MiB；源码、文档、SDK、Release/ZIP、报告/截图保留，用户图库不在清理范围内 | artifacts/validation/package-v01/cleanup.json；方法与保护见RUNBOOK |
| 基础上下文文档 | 已建立规则、索引、使用入口与专题分工 | [索引](../../PROJECT_INDEX.md)；当前检查状态见下表 |

## 验证状态

| 验证层次 | 状态 |
| --- | --- |
| 历史原型校验 | 2026-10-08前次任务输出记录：10组DOM交互检查通过；候选SQLite表数、完整性和关键约束检查通过。来源是前次任务工具结果；没有另存完整运行日志。本次未重跑，不作当前执行证据。脚本入口见 [RUNBOOK](RUNBOOK.md#已有原型检查) |
| 本次文档检查 | check-context.py对9份上下文文档、直接文件/锚点链接和14条读写规则通过；外部HTTPS不作可用性校验。记录：artifacts/validation/release-v01/context-check.json；只检查本次改动与直接链接 |
| 核心库 | 英文工程改名后64项确定性检查通过，包含历史存档、去重和既有服务；解码器/系统效果仍用替身。记录：artifacts/validation/release-v01/core-checks.txt |
| WinUI编译/打包 | 原创新图标的Release重新发布成功；ZIP546文件逐项大小/SHA256通过；MSIX签名CMS、签名中的块映射摘要及551载荷文件/3851块通过，Windows信任后签名状态Valid。记录：artifacts/validation/release-v01/build.txt、zip-build.txt、msix-build.txt；摘要以交付目录为准 |
| Windows实测 | 初版任务6项诊断通过：真实WIC解码、JPEG缩略图、坏图拒绝、导入去重、原字节导出、COM枚举1屏；本轮未改这些服务、未重跑。历史记录：artifacts/validation/release-windows.txt |
| WinUI页面/解压包启动 | 原创新图标最终ZIP解压后48条原生UI检查通过，退出码0。README首页/侧栏/详情真实截图通过独立模式生成且人工查看。记录：artifacts/validation/release-v01/portable-final-ui.txt、screenshots.txt。未测物理鼠标或其他机器 |
| 真实公共API/图片 | 上一轮18条PASS，退出码0；本轮未重跑、不作新增界面联网验收。覆盖三源元数据、预览/WIC、原图/WIC与原字节导出、Wallhaven关键词/第二页，以及每日/随机单张选图。记录：artifacts/validation/home-v6/api-windows.txt。联调中Commons曾遇TLS连接中断，保护见RISKS R01 |
| Bing历史API/原图 | 上一轮4条PASS，退出码0；本轮未重新联网。历史分页/独立ID、较早日期原图真实下载与WIC解码、未看随机图及每日缓存隔离；接口证据以research/source-api-evidence-2026-10-09.json为准；报告artifacts/validation/layout-v8/bing-archive-api.txt，诊断图库已清理 |
| 锁屏/通知能力 | 本机IsSupported探测均true；未注册/展示通知，未实际更换锁屏，不等于系统效果通过 |
| 实际桌面设置、多屏热拔插、恢复 | 已编写调用代码；没有实际修改/回滚桌面的通过证据，待手动验收 |
| 安装/升级/卸载验证 | 已授权并通过UAC导入公开CER到LocalMachine/TrustedPeople，签名Valid。原创新图标最终MSIX卸载后重装成功；安装后的48项UI退出0，GetPackageFullName确认安装进程包身份。保留已安装试用版和用户授权的证书信任；真正跨版本升级、真实用户图库迁移与其他设备待核实。记录：artifacts/validation/release-v01/certificate-trust.txt、msix-ui.txt、msix-install.json |
| 用户验收 | 未确认；原型交互选择不等于正式布局验收 |
| 发布授权 / 实际发布 | D16已明确授权公开GitHub、自签名MSIX与便携ZIP；账号sxwer001已核实，[公开仓库](https://github.com/sxwer001/Stillframe)已创建，源码/Release上传尚在执行，不能视为已上传 |

## 阻塞与待核实

- 初版本地构建已无阻塞。环境原无.NET SDK，本次安装项目内SDK；依赖网络曾遇TLS错误与2.5附加包缺失，最终采用可构建的1.8版本，详见MAP。
- 中文“拾景”、英文工程Stillframe与公开试用渠道已确定；布局细节的最终验收、其他设备兼容性及跨版本升级待核实。
- 当前运行结构为4表简化schema v2，区别于13表设计候选；已有运行期间自动任务和Wallhaven，没有退出软件后的后台执行、完整应用历史或自动LRU。
- 发布初次复验发现PRI漏拷，已修复App项目发布目标并通过最终Release启动复验；必须保留完整输出目录，不能只复制exe。
- 用户图库闪退对应的详情重挂异常已修复并通过重复导航回归；此前只渲染详情一次的检查未覆盖该问题。真实用户图库仍需体验确认，保护与验证边界见RISKS R06。
- 本机三个公共图源及图片处理已通过真实网络检查；复杂自定义JSON清单、其他地区可用性、实际系统更换/通知与性能压力仍未完成端到端验收。截图中的私有聚合源尚未接通，Unsplash仅提供官网入口。
- 图源使用条件、真实API字段/限额/网络可用性、多屏行为及原型可移植性按 [RISKS](RISKS.md) 对应条目处理。

## 下一步

完成授权的公开仓库/Release上传，核对公开状态、提交及全部资产摘要。随后按反馈试用：实际桌面/锁屏效果、其他设备兼容性、跨版本升级及最终布局仍待验收；退出后的后台服务未扩展。

## 状态更新约定

发生实质变化时直接修订相关行，记录对象、范围、日期和证据入口；旧过程只有值得追溯时才归档到 [history](history/README.md)，不保留完整聊天和重复状态快照。
