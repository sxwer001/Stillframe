# 当前目标与状态

核验：2026-11-04；本次范围为两项个性化修复的合并、v0.1.1构建、试用包验证及公开发布。历史安装记录沿用原证据，没有执行真实桌面/锁屏更换。

## 当前目标

按用户本次“合并然后发布新版本”的授权，已公开交付[“拾景 · Stillframe”v0.1.1 x64试用版](https://github.com/sxwer001/Stillframe/releases/tag/v0.1.1)，沿用自签名MSIX、公开CER、安装脚本、便携ZIP和摘要。两项修复已合并主分支，包验证与远端摘要核对完成。变更范围见[版本说明](../releases/v0.1.1.md)，完整设计继续使用[详细开发方案](../Windows壁纸软件-详细开发方案.html)。

## 当前进展

锁屏副本修复[#2](https://github.com/sxwer001/Stillframe/pull/2)已合并：副本清单、提交意图、保守回收与启动核对已实现。只读探测file-uri=False时延期回收，实际锁屏替换与旧源文件释放仍待授权验收。方法/边界见[锁屏生命周期](LOCKSCREEN_LIFECYCLE.md)。

桌面恢复修复[#1](https://github.com/sxwer001/Stillframe/pull/1)已合并：跨重启快照、异常回滚与比较后恢复已实现。7项隔离Windows诊断（含只读COM状态）通过，没有真实桌面更换或自动恢复。方法/边界见[桌面持久恢复](DESKTOP_RECOVERY.md)。联合核心检查117项通过；本次发布证据位于独立发布工作区的artifacts/validation/v0.1.1/，不纳入源码。

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
| Release构建与试用包 | 已生成0.1.1 x64 ZIP及自签名MSIX；沿用证书和包身份，保留第三方许可/NOTICE及resources.pri；发布状态单列 | [MSIX方法](RUNBOOK.md#试用版msix与公开发布)、[交付映射](MAP.md#运行与部署位置) |
| 生成文件清理 | v0.1.0历史清理记录保留；本轮使用独立发布工作区，原工作区未提交功能和用户图库保留 | artifacts/validation/release-v01/cleanup.json；方法与保护见RUNBOOK |
| 基础上下文文档 | 已建立规则、索引、使用入口与专题分工 | [索引](../../PROJECT_INDEX.md)；当前检查状态见下表 |

## 验证状态

| 验证层次 | 状态 |
| --- | --- |
| 历史原型校验 | 2026-10-08前次任务输出记录：10组DOM交互检查通过；候选SQLite表数、完整性和关键约束检查通过。来源是前次任务工具结果；没有另存完整运行日志。本次未重跑，不作当前执行证据。脚本入口见 [RUNBOOK](RUNBOOK.md#已有原型检查) |
| 本次文档检查 | check-context.py通过：9份上下文文档、167个直接链接及14条读写规则。记录：artifacts/validation/v0.1.1/context-check.json；外部HTTPS不作可用性校验 |
| 核心库 | 2026-11-04合并后117项联合检查通过：基线64项、桌面/显示器32项及锁屏21项。真实系统效果仍待授权；范围见[桌面持久恢复](DESKTOP_RECOVERY.md)及[锁屏生命周期](LOCKSCREEN_LIFECYCLE.md) |
| WinUI编译/打包 | v0.1.1 Release发布输出生成成功；ZIP546文件逐项大小/SHA256通过；MSIX签名CMS、签名中的块映射摘要及551载荷文件/3851块通过，签名状态Valid。记录：artifacts/validation/v0.1.1/build.txt、zip.txt、msix.txt；摘要以交付目录为准 |
| Windows实测 | v0.1.1解压包7项诊断通过，退出码0：WIC解码、JPEG缩略图、坏图拒绝、导入去重、原字节导出、COM枚举及只读桌面快照；临时图库与用户数据隔离，没有系统写入。记录：artifacts/validation/v0.1.1/portable-*/windows-readchecks.txt |
| WinUI页面/解压包启动 | v0.1.1解压包48项原生UI检查通过，退出码0，EXE版本0.1.1.0；独立诊断数据、自动任务停止。记录：artifacts/validation/v0.1.1/portable-result.json及对应native-ui.txt。README截图沿用v0.1.0原生截图 |
| 真实公共API/图片 | 上一轮18条PASS，退出码0；本轮未重跑、不作新增界面联网验收。覆盖三源元数据、预览/WIC、原图/WIC与原字节导出、Wallhaven关键词/第二页，以及每日/随机单张选图。记录：artifacts/validation/home-v6/api-windows.txt。联调中Commons曾遇TLS连接中断，保护见RISKS R01 |
| Bing历史API/原图 | 上一轮4条PASS，退出码0；本轮未重新联网。历史分页/独立ID、较早日期原图真实下载与WIC解码、未看随机图及每日缓存隔离；接口证据以research/source-api-evidence-2026-10-09.json为准；报告artifacts/validation/layout-v8/bing-archive-api.txt，诊断图库已清理 |
| 锁屏/通知能力 | 本机IsSupported探测均true；未注册/展示通知，未实际更换锁屏，不等于系统效果通过 |
| 实际桌面设置、多屏热拔插、恢复 | 持久化/故障回滚/外部修改保护已通过替身回归，只读COM快照可用；没有真实修改、热拔插或回滚的通过证据，待授权验收 |
| 安装/升级/卸载验证 | v0.1.0历史安装/卸载和证书信任记录保留。本次未安装v0.1.1、未修改证书信任；跨版本升级、真实用户图库迁移与其他设备待核实。历史记录：artifacts/validation/release-v01/certificate-trust.txt、msix-ui.txt、msix-install.json |
| 用户验收 | 未确认；原型交互选择不等于正式布局验收 |
| 发布授权 / 实际发布 | #1、#2已合并；[v0.1.1](https://github.com/sxwer001/Stillframe/releases/tag/v0.1.1)于2026-11-04公开，非draft、标记prerelease。tag指向2ea164eff620f7842c5d3cc2326f049a90282293，五项资产大小/SHA256与本地一致；主分支随后仅同步发布记录。证据：artifacts/validation/v0.1.1/github-published.json；v0.1.0保留 |

## 阻塞与待核实

- 初版本地构建已无阻塞。环境原无.NET SDK，本次安装项目内SDK；依赖网络曾遇TLS错误与2.5附加包缺失，最终采用可构建的1.8版本，详见MAP。
- 中文“拾景”、英文工程Stillframe与公开试用渠道已确定；布局细节的最终验收、其他设备兼容性及跨版本升级待核实。
- 当前运行结构为4表简化schema v2，区别于13表设计候选；已有运行期间自动任务和Wallhaven，没有退出软件后的后台执行、完整应用历史或自动LRU。
- 发布初次复验发现PRI漏拷，已修复App项目发布目标并通过最终Release启动复验；必须保留完整输出目录，不能只复制exe。
- 用户图库闪退对应的详情重挂异常已修复并通过重复导航回归；此前只渲染详情一次的检查未覆盖该问题。真实用户图库仍需体验确认，保护与验证边界见RISKS R06。
- 本机三个公共图源及图片处理已通过真实网络检查；复杂自定义JSON清单、其他地区可用性、实际系统更换/通知与性能压力仍未完成端到端验收。截图中的私有聚合源尚未接通，Unsplash仅提供官网入口。
- 图源使用条件、真实API字段/限额/网络可用性、多屏行为及原型可移植性按 [RISKS](RISKS.md) 对应条目处理。

## 下一步

本次合并与发布已完成。实际桌面/锁屏/通知效果、其他设备兼容性、真实图库迁移和跨版本升级仍待验收；本次授权不包含系统壁纸写入测试。未提交功能保留在原开发工作区，未混入v0.1.1。

## 状态更新约定

发生实质变化时直接修订相关行，记录对象、范围、日期和证据入口；旧过程只有值得追溯时才归档到 [history](history/README.md)，不保留完整聊天和重复状态快照。
