# 应用使用时长 · 本机使用追踪与热力图

统计这台电脑上**每个软件的使用时长**，用 GitHub 贡献热力图的方式展示每日活跃度，
并生成一份可读的**每日使用汇报**。

**原生 Windows 程序（单文件 EXE），不依赖浏览器、不联网（除你主动开启的 AI / 截图存档）、无账号。**

![主界面](docs/shot-native.png)

---

## 快速开始

| 想做什么 | 怎么做 |
|---|---|
| 打开主界面 | 双击 `dist\AppUsage.exe`（已在运行的话，会把**已有实例**的窗口叫出来） |
| 后台常驻（不占屏幕） | `AppUsage.exe --tray`（开机自启用的就是这个） |
| 看每日汇报 | 主界面顶栏「📋 日报」按钮，或点热力图任意格子 |
| 按别的口径看应用 | 主界面「应用明细」表**点列头排序**（再点一次换方向，▲ 升 / ▼ 降） |
| 控制台菜单 | 双击 `AppUsage.cmd` |
| 改设置 | 日报页右上角 ⚙（DeepSeek / GitHub 私有仓 / 截图 / 站点关键词 / 备份数据） |

> **开机自启**走托盘模式（`--tray`）。程序会往「启动」文件夹丢一个快捷方式，
> 但**会核对快捷方式指向的程序还在不在**——程序被挪过位置的话，界面会直接告诉你
> 「自启没生效」，而不是继续显示 ✓ 让你以为一切正常。

> **单实例**：同一个数据目录只允许跑一个实例。两个实例同时写同一个日志，
> 同一段时间会被记两遍、截图额度也会被吃两次——数据直接被污染。
> 所以第二个实例会**自动退出**，同时通过命名管道通知已在运行的那个把主窗口显示出来
> （本机开机自启走托盘，所以双击 EXE 时托盘实例一定已经在跑，这条路径必须通）。

---

## 每日使用汇报

顶栏「📋 日报」打开独立窗口，可以看**今天或任意历史日**（‹ 前一天 / 后一天 › / 今天）。

![日报页](docs/shot-report.png)

六块内容：

| 区块 | 内容 |
|---|---|
| 概览 | 总使用时长、较前一天、近 7 日均、首次–末次活跃 |
| AI 总结 | 用 DeepSeek 把当天数据写成 3~5 句人话（需自备 key） |
| 使用构成 | 按应用分类的占比（编程 / 办公 / 沟通 / 娱乐 / 浏览 / 学习 / 其他） |
| 今天看了什么 | **从窗口标题还原出的活动页 / 文档**，带站点徽标；右侧是站点分布 |
| 专注度 | 最长连续使用、应用切换次数、碎片率、24 小时活跃条 |
| 随机使用截图 | 当天使用画面缩略图，点击在 GitHub 打开原图 |

> 看「今天」时日报页是**实时更新**的：追踪器每 20 秒落一次盘，
> 主界面发现数据变了就会把开着的日报页一起刷新（不是只在你翻页时才更新）。

### 网页名是怎么记下来的

浏览器的窗口标题是 `粉笔课堂 和另外 2 个页面 - 个人 2 - Microsoft Edge` 这种脏格式。
程序在**读取时**（不是采集时，所以零开销、旧数据立刻可用）做三步：

1. **清洗** —— 去零宽 / 不可见字符，折叠空白
2. **剥后缀** —— 依次剥掉 `和另外 N 个页面`、`- 个人 N`、`- Microsoft Edge` 等
   ⇒ 剩下的就是**活动标签页的真实标题**（`粉笔课堂`、`判断-模块检测`…）
3. **认站点** —— 用「标题关键词 → 域名」表匹配
   ⇒ `粉笔` / `专项智能练习` / `单题批改` → `fenbi.com`

**只记站点域名，不记完整 URL**，隐私暴露面不比之前更大（窗口标题本来就在记）。
匹配不到就显示「未识别站点」，**绝不瞎猜**。

> ⚠️ **站点识别要在「剥后缀」之前做**。Edge 的窗口标题常常是「页面标题 - 站点名」
> （`xxx - 知乎`、`xxx - 搜索`），而站点名恰好在被剥掉的那一段里——
> 先剥再认的话，这些站点就永远认不出来，只能显示「未识别站点」。

### 自己加站点关键词

内置词表只覆盖常见站点。剩下没认出来的（日报里显示「未识别站点」的），
在 **设置 → 站点关键词…** 里自己补，每行一条：

```
关键词=域名[=中文显示名]
例：小红书=xiaohongshu.com=小红书
```

关键词出现在窗口标题里，就会被记成对应域名。**只记域名，不记完整网址。**
保存后程序会自动**重算**历史数据（站点是读取日志时算出来的），不用重启。

> 实测本机：浏览器时间约 454 分，改之前 30% 认不出来；
> 补了几条词之后，未识别的部分基本被覆盖。

### 专注度是这么算的

日志每 20 秒落盘一行，所以**同一段长时间使用会被切成很多 20 秒的块**。
程序先把「同应用 + 间隔 ≤ 60 秒」的相邻块**合并成逻辑连续段**，再算：

- 最长连续使用 = 最长的合并段
- **应用切换次数 = 相邻两段的应用确实不同的次数**
  ——「人离开了一会儿（空闲 90 秒以上）又回到同一个应用」是**暂停**，不是切换，
  单独统计并在卡片副标题上标出来。以前把这种也算成切换，数字会虚高
  （实测某天多算了 17～21 次）。
- 碎片率 = 1 − 最长连续 / 总时长

> 直接拿日志行数当切换次数是错的（会把 20 秒落盘算成切换）。

---

## 两套皮肤

顶栏右上角 **☀ 浅色 / ☾ 深色** 一键切换，选择记在 `data\ui.json`，下次启动自动恢复。**默认浅色。**

- **浅色 —— Claude 风格**：暖米白底 `#FAF9F5`、陶土橘强调色 `#D97757`、圆角柔和、无辉光
- **深色 —— 女神异闻录 V 风格**：深午夜蓝底、电光蓝 `#2E6FE0` + 青色 `#45B8E0`、
  切角面板、斜切平行四边形按钮、霓虹辉光

> 改配色只需要动 `native\Ui\Theme.cs` 顶部的常量。

---

## AI 总结（可选）

日报页 ⚙ → **DeepSeek** 段填 API Key，保存后就能用。

- 端点 `https://api.deepseek.com`（OpenAI 兼容），**模型默认 `deepseek-v4-pro`**（当前最新）
  ；`deepseek-v4-flash` 是省钱档
  （⚠️ 旧的 `deepseek-chat` / `deepseek-reasoner` 已于 2026-07-24 弃用，**不能用**）
- **默认不联网**：不填 key 就完全没有网络行为
- 请求体里只有**归纳后的统计数字**（总量、分类占比、TOP 应用名、站点名、专注度），
  **不发** exe 路径、完整 URL、截图
- `include_topics` 打开时会额外发**最多 8 条**清洗后的页面标题（可在设置里关掉）
- key 存本地 `config.json`，**不写进任何日志**
- 失败（401 / 429 / 超时）会显示明确原因 + 重试按钮，**不影响报告的其它部分**

> **两个踩过的坑（已修）**
>
> 1. **思考模式下推理 token 也算进 `max_tokens`**。原来固定 900，结果推理就把预算吃光，
>    模型来不及输出正文——实测 `finish_reason=length`、正文长度 **0**，
>    **AI 总结一个字都出不来**，界面只显示「返回内容为空」。
>    现在开思考模式给 8000（关掉给 1200），而且被截断时**自动加倍重试一次**。
> 2. DeepSeek 回的 `Content-Type` 只有 `application/json`、**不带 charset**。
>    现在响应正文自己按 **UTF-8 显式解码**，不赌运行时的默认值（不然中文总结会变乱码）。

---

## 随机使用截图 + 私有 GitHub 存档（可选，默认关闭）

日报页 ⚙ → **随机使用截图** 段开启。

- 每 20 分钟抓一张**当前前台窗口**（不是全屏）
- 抓图链路：`PrintWindow` →（Chromium 窗口常返回黑图）→ 黑图检测 → 降级 `BitBlt` 屏幕区域
- **不会拍自己**：前台是 AppUsage 本身时直接跳过（否则开着主界面就一直在拍自己的窗口）
- 全图（1280 宽，JPEG q60，约 150 KB）**base64 上传到你的 GitHub 私有仓库**：
  `PUT /repos/{owner}/{repo}/contents/shots/YYYY/MM/DD/HH-mm-ss_<app>.jpg`
- 上传成功 → 本地全图立即删除，**只留 320 宽的缩略图**（约 20 KB）供日报即时显示
- 上传失败 → 全图进 `data\shots\outbox\` 队列，**运行期间每 10 分钟自动补传**（失败越多等越久）
- 每次上传是一个 commit ⇒ **git 历史天然永久保留**，本地不需要任何清理逻辑

**限额与保护**：每天最多 8 张；outbox 累计超过 200 MB 丢最旧的；
**磁盘剩余不足 1 GB 自动停拍**（当前 D 盘只剩 5.3 GB，这条很关键）。

### 命名约定（全链路统一）

| 东西 | 格式 | 例 |
|---|---|---|
| 缩略图名 / 原始名 | `{HH-mm-ss}_{app}` | `12-29-31_msedge` |
| GitHub 相对路径 | `shots/{yyyy}/{MM}/{dd}/{原始名}.jpg` | `shots/2026/10/23/12-29-31_msedge.jpg` |
| outbox 文件名 | `{yyyy-MM-dd}_{原始名}.jpg` | `2026-10-23_12-29-31_msedge.jpg` |
| 上传清单 `index.tsv` | `{原始名}\t{GitHub 相对路径}` | 见上 |

> 清单的**键必须是原始名**——日报是按缩略图文件名去清单里找链接的。
> 早期版本 outbox 补传时把日期前缀写进了键名和路径，结果就是：
> 图明明已经传上去了，日报里却显示「未上传」。
> 现在补传会先还原原始名，程序启动时还会自动迁移历史清单。
>
> ⚠️ 另外注意 .NET 自定义日期格式里**必须用小写 `dd`**，大写 `DD` 会被原样输出成字面量 `DD`。

**隐私（务必看清）**：
截图可能包含屏幕上的聊天 / 邮件等敏感内容，而且**会上传到 GitHub 私有仓库**——
私有仓库只有你能看到，但**这仍然是上传到云端**。所以默认关闭，且可按应用排除
（默认已排除 `wechat/qq`）。**截图永远不会发给 DeepSeek。**

> 凭据建议用 **fine-grained PAT**（只授权那一个私有仓库的 Contents 读写），
> 比 classic PAT 的 `repo` scope 权限小得多。

### GitHub PAT 怎么拿

> ⚠️ **最容易踩的坑**：fine-grained PAT 有**仓库白名单**。
> 如果 `appusage-shots` 不在白名单里，GitHub 对这个私有仓会返回 **404（而不是 403）**，
> 程序只会显示「Not Found」，很容易以为是仓库不存在。
> **建 token 时一定要在 Repository access 里勾上 `appusage-shots`**
> （仓库建好之后再去加也行，但改完权限 token 字符串会变，要重新复制一次）。

1. 打开 <https://github.com/settings/personal-access-tokens/new>（或
   Settings → Developer settings → Personal access tokens → **Fine-grained tokens** → Generate new token）
2. **Token name** 填 `AppUsage`，Expiration 选最长（1 年）
3. **Resource owner**：选你的账号（`iamfromchangsha`）
4. **Repository access**：选 **Only select repositories** → 勾选 `appusage-shots`
   （私有仓已建好；若勾不到，先在 <https://github.com/new> 建一个同名私有仓）
5. **Repository permissions → Contents → Read and write**（**只开这一项**，别的都不用）
6. Generate token → **立刻复制** `github_pat_...`（只显示一次）

然后在程序里：**日报页 ⚙ → GitHub 段**

| 字段 | 填什么 |
|---|---|
| Token | 刚复制的 `github_pat_...` |
| 拥有者 | `iamfromchangsha`（已预填） |
| 仓库名 | `appusage-shots`（已预填，私有仓已建好） |
| 仓库不存在时自动创建 | 勾上（万一删了仓库会自动重建） |

点「**测试 / 创建仓库**」→ 出现绿色的「✓ 仓库 iamfromchangsha/appusage-shots 可用」就成了，
再点「保存」。

**报 404 怎么办**：点「**载入可见仓库**」，程序会列出这个 token 实际能访问的仓库（私有/公开都标出来），
从下拉里选一个填进去即可。
——但注意：**选公开仓库 = 你的截图所有人可见**，所以更推荐把 `appusage-shots`
加进 PAT 的白名单（见上面第 4 步）。

> **Token 存在哪**：`config.json`（本机明文）。这个文件**已加入 .gitignore 且不再被 git 跟踪**，
> 所以不会被提交到仓库；仓库里只有不含密钥的 `config.example.json` 模板。
> 随时可以去 GitHub 设置里吊销，程序会提示 token 无效并暂停上传。
>
> **粘贴时会自动只取 token 本身**（正则匹配），不会因为你顺手把另一个 key 也粘进来就串味；
> 万一串了，保存时也会自动纠正。



---

## 资源占用（2026-10-23 实测，本机 150% 缩放）

| 场景 | 内存（私有 / 工作集） | CPU | 线程 |
|---|---|---|---|
| 托盘常驻 | **63 MB / 137 MB** | **≈ 0 %** | 13 |
| 主界面打开 | 约 98 MB | 约 0.2 % | 20 |

> Python 网页版虽然自身只要 10 MB，但**必须开浏览器**——一个 Edge / Chrome 窗口额外占
> 100~300 MB。原生 EXE 没有任何浏览器进程，**总占用反而更低**。
>
> 注意内存会**随历史数据线性增长**（每 1 MB 日志约 0.9 MB 内存），
> 跑满一年大概多占 50 MB——见上文「工作原理」里的实测表。

**一条踩过的坑**：曾用「透明容器 + 实时重绘渐变背景」做深色底，CPU 直接从 0.16% 飙到
**2.8%**——透明控件每次重绘都会让父层重画背景区域，渐变位图还要整块缩放。
正确做法是**顶层容器用不透明背景**，渐变改成预生成的小位图。

---

## 工作原理

`native\Core\Tracker.cs` 每秒做三件事：

1. `GetForegroundWindow()` 取当前前台窗口 → 进程 PID → `QueryFullProcessImageNameW` 取 exe 路径
2. `GetLastInputInfo()` 判断你多久没碰键鼠了——**超过 `idle_seconds`（默认 90 秒）就不计任何时长**，
   避免人走开后把时长算到某个程序头上
3. 前台应用没变就继续计时；变了就把上一段追加写进日志。跨零点的片段**按自然日拆开**

数据落在 `dist\data\logs\YYYY-MM-DD.log`（纯文本 TSV，追加写，**一天一个文件**）。
仪表盘**只读新增行**，并压成两层聚合：

```
(天 → 总量 / 各应用 / 各小时 / 主题 / 站点 / 段序列)  +  (应用 → 累计)
```

内存不是常数：**Store 为了算「最长连续 / 切换次数 / 碎片率」，
会把每一段原始使用记录都留在内存里**，所以内存跟着日志线性涨。
实测（`--memcheck`，一天 1800 段 ≈ 10 小时活跃）：

| 数据量 | 日志大小 | 启动读取 | 额外内存 |
|---|---|---|---|
| 30 天 | 4.7 MB | 0.16 秒 | 4.2 MB |
| **365 天** | **57 MB** | **1.1 秒** | **50.6 MB** |

一年下来大概多占 50 MB、启动多花 1 秒——**完全可以接受**，
所以没有做「只保留最近 N 天明细」那种会牺牲历史日报功能的取舍。
想自己验一下：`AppUsage.exe --memcheck 365`。

> **为什么按天分文件**：单个 `usage.log` 无限增长，既不好备份也不方便按天清理，
> 而且一年后单文件会大到每次启动都要扫一遍。现在每天一个文件，
> 老版本遗留的 `usage.log` 会自动迁移成 `logs\0000-legacy.log`（历史不丢）。

### 应用归类

- 主键是 **exe 文件名**（`chrome.exe` → `chrome`），同一软件的多个窗口会合并统计
- 显示名：`config.json` → `app_names`
- 分类：`config.json` → `app_categories`（未归类的进「其他」，占比合计恒为 100%）

---

## 配置

`config.json`（EXE 会依次找 `dist\config.json`、项目根 `config.json`、`data\config.json`）：

| 键 | 默认 | 说明 |
|---|---|---|
| `poll_interval` | `1.0` | 采样间隔（秒） |
| `idle_seconds` | `90` | 空闲多久算「人不在」，不计时长 |
| `min_span_seconds` | `2.0` | 短于这个长度的片段直接丢弃 |
| `flush_interval` | `20.0` | 当前片段最长多久落一次盘 |
| `excluded_exes` | `[]` | 不统计的 exe 名 |
| `app_names` | `{}` | exe 名 → 显示名 |
| `app_categories` | `{}` | exe 名 → 分类 |
| `site_hints` | `{}` | 标题关键词 → 站点域名（设置页可编辑） |
| `site_names` | `{}` | 域名 → 中文显示名 |
| `ai` | 见上文 | DeepSeek 配置 |
| `shots` | 见上文 | 截图 + GitHub 存档配置 |

---

## 命令行

```
AppUsage.exe              打开主界面
AppUsage.exe --tray       只在托盘常驻（开机自启用这个）
AppUsage.exe --data <目录>  指定数据目录
AppUsage.exe --selftest   跑内置自测（137 个用例），结果同时写到 data\selftest.txt
AppUsage.exe --memcheck [天数]  造合成日志量内存占用（默认 30 天，可传 365 看一年）
AppUsage.exe --test-ai    测 DeepSeek 连接（模型回复会打印出来，用来验证中文没乱码）
AppUsage.exe --ai-summary [yyyy-MM-dd]  生成某天的 AI 总结并打印（默认今天）
AppUsage.exe --backup [目标目录]   把使用记录打成 ZIP（默认放桌面，密钥会被抹掉）
AppUsage.exe --dump <yyyy-MM-dd>  把某天的日报导出成 data\report-dump.txt（调试用）
AppUsage.exe --test-shot  抓一张当前前台窗口的图（自测截图链路用）
```

> 本程序是 `WinExe`（双击不出黑框），所以命令行用法启动时会把进程**挂到当前终端的控制台**上，
> 输出直接看得见。`--selftest` / `--dump` / `--test-shot` 是**纯查询**，
> **不会**启动后台追踪，也**不会**触发随机截图——一条调试命令不该顺手截了你的屏。
>
> `--test-shot` 会往 `data\test-shot.jpg` 写一张**全尺寸**截图（跟平时「只留缩略图」
> 的规矩不一样）。**下次正常启动会自动把它删掉**，所以最多只存在一份临时全图。

---

## 开发与自测

```powershell
# 跑内置自测（97 个用例：标题解析 / 站点识别 / 凭据清洗 / 段合并 / 跨零点 / 日志解析 / 截图命名 / AI 响应解析 / 配置往返）
cd D:\minimax\coder\appusage\dist
Start-Process -FilePath .\AppUsage.exe -ArgumentList '--selftest' -NoNewWindow -Wait -PassThru

# 内存体检：造 365 天的合成日志，看启动要多久、吃多少内存
Start-Process -FilePath .\AppUsage.exe -ArgumentList '--memcheck','365' -NoNewWindow -Wait -PassThru

# 测 DeepSeek：能顺便确认中文没乱码
Start-Process -FilePath .\AppUsage.exe -ArgumentList '--test-ai' -NoNewWindow -Wait -PassThru

# 看运行状态与统计（今日 / 累计 / TOP5）
powershell -File scripts\status.ps1

# 导出某天的日报到文本
.\AppUsage.exe --dump 2026-10-22
```

自测是**纯逻辑**的（不联网、不碰真实数据），所以随时可以跑。
新增功能时顺手在 `native\Core\SelfTest.cs` 里加一条用例，比手动点界面可靠得多。

> **发布顺序铁律**：先 `Stop-Process AppUsage` 再 `dotnet publish`。
> exe 被占用时 publish 会**静默失败**，你只会看到「改好了但没生效」。

---

## 源码结构

```
native/
├─ Program.cs              入口（--tray / --data / --selftest / --dump / --test-shot）
├─ Core/
│  ├─ NativeMethods.cs      Win32 P/Invoke：前台窗口 / 进程路径 / 空闲 / 抓屏
│  ├─ ConsoleHost.cs        让 WinExe 也能往控制台输出（命令行用法要用）
│  ├─ SingleInstance.cs     单实例守卫 + 命名管道（第二个实例叫第一个开窗口）
│  ├─ MemCheck.cs           内存体检（--memcheck，造合成日志量占用）
│  ├─ Tracker.cs            后台采集线程，按天分片写日志，跨零点拆分
│  ├─ Store.cs              增量读取多个日志文件 + 聚合
│  ├─ TopicParser.cs        窗口标题清洗 / 剥后缀 / 认站点
│  ├─ CategoryMap.cs        应用分类 + 站点关键词表
│  ├─ SiteHintsCodec.cs     站点词表的文本编解码（设置页编辑用）
│  ├─ ReportModel.cs        日报数据模型
│  ├─ AppSorter.cs          应用明细表的排序
│  ├─ DataBackup.cs         打包使用记录（密钥抹掉）
│  ├─ ReportBuilder.cs      从 Store 装配日报（纯函数、离线）
│  ├─ SelfTest.cs           内置自测用例（--selftest）
│  ├─ TokenUtil.cs          凭据提取（按固定长度截取，避免粘串）
│  ├─ Screenshotter.cs      抓窗 + JPEG 编码（含黑图检测与降级）
│  ├─ ShotService.cs        截图后台线程 + 限额 + outbox 重试
│  ├─ ShotUploader.cs       GitHub Contents API 存档
│  ├─ ShotIndex.cs          列出当天缩略图 + 算 GitHub 原图链接
│  ├─ AiClient.cs           DeepSeek 调用
│  └─ Config.cs / AppPaths.cs / UiPrefs.cs / Models.cs
├─ Ui/
│  ├─ MainForm.cs           主界面
│  ├─ ReportForm.cs         日报页
│  ├─ SettingsForm.cs       设置页
│  ├─ SiteHintsForm.cs      站点关键词编辑器
│  ├─ HeatmapControl.cs     自绘热力图
│  ├─ SlimScrollBar.cs      深色皮肤下自绘的细滚动条（系统那条是白条）
│  ├─ ReportWidgets.cs      构成条 / 主题列表 / 站点列表
│  ├─ Widgets.cs            统计卡 / 排行 / 时间线 / 趋势条
│  ├─ ShotGrid.cs           截图缩略图网格
│  ├─ SmtControls.cs        斜切按钮 / 斜体文字
│  ├─ Card.cs               卡片容器 / 顶栏
│  ├─ Theme.cs              配色 + 几何 + DPI 缩放
│  ├─ TrayHost.cs           托盘常驻宿主
│  ├─ Autostart.cs          开机自启（快捷方式）
│  └─ ReportDump.cs         --dump 的文本输出
└─ tools/                   开发自检脚本（抓图 / 模拟点击 / 窗口枚举）
```

> **DPI 说明**：PerMonitorV2 下 WinForms **不会**自动缩放手工布局（`AutoScaleFactor` 恒为 1），
> 所以设计常量统一按 96 DPI 编写，绘制时乘 `Dpi.Scale`（本机 150% 缩放 → 1.5）。
> 字体点数**不要乘**（GDI+ 会按 DPI 自动换算成像素），只有空间常量要乘。

---

## 已知边界

- 只能统计**前台**应用。最小化到托盘的后台程序不计入。
- 全屏独占的游戏能拿到前台窗口，但部分游戏会自己清空窗口标题。
- 远程桌面 / 锁屏期间视同空闲，不会计时长。
- 换了 exe 名就认成新应用，升级改名的软件历史会断档。
- 纯前台轮询，秒级精度约 ±1 秒。

---

## 数据备份

使用记录是这个程序最值钱的东西，却只躺在应用目录的 `dist\data\logs\` 里——
换应用、删文件夹、磁盘出点事，几年的记录就没了。

**设置页 →「备份数据」**（或 `AppUsage.exe --backup`）会打一个 ZIP 到**桌面**：

```
appusage-备份_2026-10-24_0043.zip
├── logs/        原始使用记录（一天一个文件）
├── config.json  配置（**API Key 和 PAT 已被抹掉**）
└── README.txt   怎么恢复
```

> 密钥被抹掉是**故意的**：备份这种东西很容易被转发出去，密钥不该跟着走。
> 恢复后自己在「设置」里重新填就行。

---

## 出问题先看这几个文件

| 文件 | 里面是什么 |
|---|---|
| `dist\data\error.log` | **未捕获异常**（含完整堆栈）+ 启动失败原因 |
| `dist\data\trace.log` | 调试流水：皮肤切换、截图尝试、布局参数（超过 2 MB 自动轮转为 `.1`） |
| `dist\data\selftest.txt` | 最近一次 `--selftest` 的完整结果 |
| `dist\data\logs\*.log` | 原始采集数据（TSV，一天一个文件） |
| `dist\data\shots\outbox\` | 截图上传失败的排队队列（下次启动自动补传） |

程序不会再弹那种丑陋的 .NET 未处理异常对话框——异常一律写进 `error.log`，
主界面照常可用。

---

## 附：早期网页版（已归档）

`legacy-web/` 是最初用 Python + 本地网页实现的版本（同样是纯本地）。功能基本一致，
但需要开浏览器、常驻一个 Python 进程，现已不作为主力。

```powershell
cd legacy-web
python tracker.py        # 采集
python server.py          # 起本地服务
```

> **改 `.ps1` 脚本后记得跑一次 `python scripts\fix-encoding.py`**：
> Windows PowerShell 5.1 读取没有 BOM 的 `.ps1` 会按 GBK 解码，中文会变乱码甚至语法错误。
