# 操作手册

方法核验日期：2026-10-09；打包/清理方法已执行，结果统一见NOW；其余步骤仍须区分配置与执行证据。操作前确认当前任务授权；本文件不授权安装、联网、系统桌面修改或发布。

## 查看设计资料

前置条件：能访问项目文件及浏览器/HTML查看器。

按问题打开 [开发方案](../Windows壁纸软件-详细开发方案.html) 对应章节，或 [可行性报告](../../Windows壁纸软件-可行性分析报告.html) 对应项目/来源章节。两者是设计与调研资料，不能用来证明功能已实现。

## 本次上下文文档检查

前置条件：在项目根目录；Python 3.9或更高版本可用；脚本及本次文档存在。检查只读取本次文档和它们直接链接的目标/锚点，不扫描依赖目录或整项目。

Windows Python Launcher可用时：

```powershell
py -3 research/preview-validation/check-context.py
```

其他环境可以使用已核实的Python 3解释器执行同一脚本，不假定命令名已安装。

预期：返回退出码0并列出文档数量、直接链接检查结果和规则覆盖结果；失败时定点修复对应目标，不遍历无关文件。该检查不验证外部网页可用性、生产功能或发布状态。实际执行状态只记在 [NOW](NOW.md#验证状态)，不在此追加运行日志。

## 已有原型检查

### 原型DOM交互

入口：[check-preview.cjs](../../research/preview-validation/check-preview.cjs)。前置条件：Node.js、对应linkedom依赖可用，且脚本中的原型路径实际存在。路径不可移植问题见 [MAP](MAP.md#原型与验证工具)。不能因有package.json就认定依赖已安装。

```powershell
node research/preview-validation/check-preview.cjs
```

此处记录已有方法，本次未执行。检查覆盖模拟状态和DOM交互，不包含像素渲染、网络图源联调或系统壁纸操作。若缺少原型输入，先报告缺口，不自动恢复私人路径或下载材料。

### 开发方案锚点与候选DDL

入口：[check-deliverables.py](../../research/preview-validation/check-deliverables.py)。前置条件：Python 3可用、输入项目路径核实、既有HTML与SQL存在。脚本使用内存SQLite，不部署实际数据库。

```powershell
py -3 research/preview-validation/check-deliverables.py
```

此处记录已有方法，本次未执行。脚本包含16节、13表等设计快照断言；规格实质变化时须同步审查断言，不为得到“通过”盲目修改数字。结果不等于产品迁移或原图/桌面功能通过。

## 便携ZIP打包与项目清理

前置条件：当前用户授权本地打包/清理，已满足下节构建条件；使用Windows PowerShell 7的.NET压缩/路径接口。关闭使用构建或解压验证目录的程序后执行。

```powershell
./scripts/package.ps1
```

脚本先锁定还原并生成Release，从应用csproj读取版本，只将发布目录中的运行文件、许可和使用说明打入ZIP，排除PDB。逐项验证ZIP内文件大小和SHA256后才提交正式包，生成对应.zip.sha256；输出位置见MAP。已有同名包默认拒绝替换，明确需要替换时加`-Replace`；`-SkipBuild`只在确认发布目录对应当前源码时使用。临时打包目录校验在工作区内且无链接后清除。ZIP不自动签名或上传；MSIX方法见下节。

打包验收：解压到独立目录，使用解压后的Stillframe.exe执行既有`--smoke`并等待退出码和新报告；不能仅检查ZIP可打开。测试图库与报告放在artifacts/validation，避免访问用户图库。文件清单/ZIP校验不能代替其他机器或真实系统效果验收。

```powershell
./scripts/clean.ps1 -WhatIf
./scripts/clean.ps1
```

清理仅限App/Core/核心检查工程的bin/obj、package-work/package-verification，以及validation目录下明确命名的诊断图库。删除前核实绝对路径在本工作区内，拒绝符号链接/重解析点和正在运行的目标。保留源码、文档、SDK/feed、Release/ZIP、报告与截图，不读取或清理用户应用数据、源图片和导出文件。清理后源码构建会重新生成中间产物；历史报告仍保留，历史诊断数据库不再保留。

## 试用版MSIX与公开发布

前置条件：当前任务明确授权；本地Release已验证，Windows SDK的MakeAppx/SignTool及PowerShell 7可用。先确认中文名与英文工程名、公开源码和素材范围，不从本节推断未来上传授权。

```powershell
./packaging/generate-assets.ps1
./scripts/package.ps1
./scripts/package-msix.ps1 -SkipBuild
```

首次MSIX打包生成`CN=Stillframe`自签名代码签名证书（CurrentUser/My、非导出私钥、两年有效）；后续复用，或显式提供`-CertificateThumbprint`。MakeAppx校验清单，SignTool签名；verify-msix.ps1核对CMS签名、签名中AXBM摘要及全部载荷块。未信任时Windows链验证会返回不信任，不能当作安装成功。公开发行只提供CER，绝不导出/提交私钥。打包脚本不信任证书、不安装、不上传。

发布安装脚本固定证书SHA256及包名，需管理员终端且输入INSTALL才导入LocalMachine/TrustedPeople，再调用Add-AppxPackage。使用完整CER即可安装，不需要PFX。图源/桌面/锁屏效果与安装身份分别验证，不通过更改真实壁纸验证安装。安装说明以[README](../../README.md#下载与安装)为入口。

验证：便携ZIP解压后执行`--smoke`；已安装MSIX通过Get-AppxPackage定位安装目录，再运行该目录`Stillframe.exe --smoke <独立报告路径>`，等待报告/退出并通过GetPackageFullName确认进程包身份。PowerShell7兼容代理调用Invoke-CommandInDesktopPackage曾阻塞，当前不作为默认启动方式。MSIX须额外保留名为resources.pri的已合并WinUI资源索引；只保留Stillframe.pri会导致包内主题字典加载失败。新截图用`--screenshots <独立报告路径>`生成，访问独立诊断图库、不联网、不读取用户图片。测试前后只清理本包及明确诊断数据，不删除共享Shijing图库。当前执行结果只记NOW。

GitHub发布：先检查拟提交文件和直接关联链接，排除.tools/artifacts、bin/obj、秘密、签名私钥和私人素材/设备路径；本地提交使用GitHub noreply地址。核实目标账号/仓库后公开源码；创建带版本标签的prerelease，上传MSIX、ZIP、CER、生成的安装脚本和SHA256SUMS。上传完成后核对远端提交、公开可见性、Release状态和资产大小/摘要。操作方法不等于本次已经发布。

## 产品构建、运行与安装

前置条件：Windows x64；global.json指定的.NET SDK可用，或使用已准备的项目内SDK；网络可访问NuGet.Config中的Microsoft源。脚本优先使用项目内SDK，否则使用PATH中的dotnet。MSIX方法见下节；没有退出软件后的后台任务。

在根目录构建/发布便携版：

```powershell
./scripts/build.ps1 -Publish
```

本次已执行成功；具体结果只记录在NOW。输出位置见 [MAP](MAP.md#运行与部署位置)。脚本锁定依赖还原，下载失败须处理真实网络问题，不能忽略退出码后声称成功。

启动已构建应用：

```powershell
./scripts/run.ps1
```

脚本优先选便携输出，其次Release/Debug。启动首页获取Bing每日图，同一本地日已有成功原图则复用；网络失败保留缓存并提示。侧栏点击“发现壁纸”展开选源，随机一张原图显示发现大图。两页图片上滚轮下滑向前、上滑返回；同一会话排除已看图，当前图源页耗尽后才请求后续元数据页，不缓存整页预览。侧栏/详情滚动不切图；图源管理保留独立在线图库浏览/搜索/分页按钮。可导入自己的JPEG/PNG/BMP；文件夹仅导入当前层、最多1000项。加载时可取消或切源，原图解码成功再替换旧图。默认自动动作全部关闭；用户开启后运行期间按设置执行，退出即停止。手动选源与自动任务图源配置独立。原图导出使用保存对话框。

核心检查：

```powershell
./scripts/check.ps1
```

发布版Windows诊断与页面启动检查（使用自生成图片，不更换桌面；输出目录需事先存在）：

```powershell
./artifacts/Stillframe-win-x64/Stillframe.exe --diagnostics artifacts/validation/windows-checks.txt
./artifacts/Stillframe-win-x64/Stillframe.exe --smoke artifacts/validation/ui-smoke.txt
```

程序为WinExe，自动化应等待进程结束并检查退出码和报告，不能仅凭命令返回或旧报告文件判定通过。smoke使用独立图库和两张自生成渐变图，覆盖四页及侧栏、六轮图库→详情→收藏重绘、长标题边界、顶部对齐、透明度保存、铺满/完整预览、重复开关设置和缩小窗口后的复开。额外通过AutomationPeer调用真实横杠按钮、侧栏五个列表项和原生菜单换图命令，检查各入口的实际页面/面板结果、导航固定宽度及设置宽度、五轮快速开关反向、持久面板/图像源复用、重叠异步导航及图库容器ContextFlyout。侧栏入口验证不得仅用NavigateAsync或设置选中项代替实际ItemClick链路。菜单通过原生ShowAt检查，不冒充实际物理鼠标右击的端到端验收。捕获前等待图片准备、布局及短暂过渡完成，生成首页、详情、侧栏、设置及small图片；不是完整桌面截图，也不是帧率测量。正常启动实例运行时先正常退出再做smoke。不要将smoke数据当作用户图库。

桌面设置/恢复、双屏热拔插及安装兼容性需后续独立验收；不自动在用户桌面执行。便携版恢复快照仅在会话内保存，退出后丢失。没有实际安装包，不填写安装/卸载成功记录。

### 图源与每日任务验证

真实API检查需要网络访问Bing、Wallhaven、Commons及它们返回的公网HTTPS图片；诊断数据写入报告旁的api-check-data与home-api-check-data，下载不超过50MiB的原图，每个预览不超过3MiB。提供报告前确保输出目录存在，等待进程退出并检查报告的新内容与退出码：

```powershell
./artifacts/Stillframe-win-x64/Stillframe.exe --api-checks artifacts/validation/api-windows.txt
```

只验证新增Bing历史接口时可使用以下窄范围检查；报告旁建立独立诊断图库，读取分页和全部候选元数据，仅获取两张历史原图并使用WIC解码，不执行桌面/锁屏更换：

```powershell
./artifacts/Stillframe-win-x64/Stillframe.exe --bing-archive-checks artifacts/validation/bing-archive-api.txt
```

本轮已执行，结果见NOW；运行前准备报告目录，使用新目录可验证全新下载，不删除用户图库。Bing浏览改为社区历史存档，首页当天图仍使用Bing原接口；来源和版权边界见 [接口核验](../../research/source-api-evidence-2026-10-09.json)。手动锁屏从图片右键或详情更多菜单点击，真实系统效果不包含在上述诊断中。

2026-10-09已执行当前接口检查，证据与实际范围见 [NOW](NOW.md#验证状态)。检查真实元数据、预览/WIC、原图/WIC、导出字节、Wallhaven关键词和第二页，以及三源首页选图/每次仅一张原图。不开用户图库，不注册/展示通知或更换桌面/锁屏。再次执行时缓存的原图可能复用；需要验证全新下载时使用新的报告目录，不删除用户数据。

每日调度的默认关闭、执行小时、成功去重、失败重试和等待期间关闭动作由核心检查使用替身动作验证。smoke额外检查原生自动任务保存、发现Expander及选源按钮、大图与独立历史、滚轮累计/节流/前后复用、侧栏/详情阻断、离页取消；首页/发现用自生成原图和可控API替身验证，不实际联网，不启动定时器。滚轮状态检查调用与事件共用处理方法，不能代替物理鼠标端到端验收。锁屏/通知的IsSupported只表示能力探测，不证明系统效果成功。实际系统验收需在用户授权范围内另行执行；本轮未执行。
