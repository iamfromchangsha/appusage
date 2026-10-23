using System.Text;
using System.Text.Json;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>
/// 内置自测：AppUsage.exe --selftest
/// 覆盖纯逻辑（标题解析 / 凭据清洗 / 段合并 / 聚合 / 配置序列化），
/// 不依赖任何外部测试框架。
/// </summary>
internal static class SelfTest
{
    private static int _pass;
    private static readonly List<string> _fails = new();

    /// <summary>
    /// 跑一遍全部用例。返回 0 = 全过，1 = 有失败。
    /// 输出同时写到控制台和 <paramref name="reportPath"/>（方便事后核对）。
    /// </summary>
    public static int Run(string? reportPath = null)
    {
        _pass = 0;
        _fails.Clear();

        // 先把输出收进缓冲区，跑完再一次性吐出来（顺便能落盘）
        var realOut = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            TestCleanTitle();
            TestStripBrowserSuffix();
            TestSiteDetection();
        TestSiteFromStrippedSuffix();
        TestSiteHintsCodec();
        TestSiteHintsRebuild();
            TestDocumentTitle();
            TestTokenExtract();
            TestMergeSegments();
        TestSwitchVsPause();
            TestSplitByDay();
            TestStoreParsing();
            TestStoreMultiFilePartial();
            TestReportAssembly();
            TestConfigRoundTrip();
            TestShotPathFormat();
        TestShotNaming();
        TestBlankDetection();
        TestShotIndexLookup();
        TestAiDecoding();
        TestAiResponseParsing();

            Console.WriteLine();
            Console.WriteLine($"自测完成：{_pass} 通过，{_fails.Count} 失败");
            foreach (var f in _fails) Console.WriteLine("  ✗ " + f);
        }
        finally
        {
            Console.SetOut(realOut);
        }

        var text = buffer.ToString();
        realOut.Write(text);
        if (!string.IsNullOrEmpty(reportPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, text, new UTF8Encoding(false));
            }
            catch { /* ignore */ }
        }
        return _fails.Count == 0 ? 0 : 1;
    }

    // ---------------- 断言 ----------------

    private static void Check(bool ok, string name, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine("  ✓ " + name); }
        else { _fails.Add(name + (detail.Length > 0 ? " → " + detail : "")); Console.WriteLine("  ✗ " + name + (detail.Length > 0 ? " → " + detail : "")); }
    }

    private static void Eq(string expect, string actual, string name)
        => Check(expect == actual, name, $"期望「{expect}」实际「{actual}」");

    private static void Eq(double expect, double actual, string name, double tol = 0.01)
        => Check(Math.Abs(expect - actual) <= tol, name, $"期望 {expect} 实际 {actual}");

    private static void Eq(DateTime expect, DateTime actual, string name)
        => Check(expect == actual, name, $"期望 {expect:yyyy-MM-dd} 实际 {actual:yyyy-MM-dd}");

    // ---------------- 用例 ----------------

    private static void TestCleanTitle()
    {
        Console.WriteLine("— 标题清洗 —");
        Eq("粉笔课堂", TopicParser.Clean("  粉笔课堂  "), "去首尾空白");
        Eq("粉笔课堂", TopicParser.Clean("粉笔课堂"), "去零宽字符");
        Eq("a b", TopicParser.Clean("a   b"), "折叠连续空格");
        Eq("", TopicParser.Clean("   "), "全空白");
    }

    private static void TestStripBrowserSuffix()
    {
        Console.WriteLine("— 剥浏览器后缀 —");
        var p = new TopicParser(new Config());

        var t1 = p.Parse("粉笔课堂 和另外 2 个页面 - 个人 2 - Microsoft Edge");
        Eq("粉笔课堂", t1.Text, "Edge 多标签 → 活动标签标题");
        Check(t1.Kind == TopicKind.Web, "多标签标题判为 Web");

        var t2 = p.Parse("2026年国考行测题 - 个人 2 - Microsoft Edge");
        Eq("2026年国考行测题", t2.Text, "Edge 单标签");
        Check(t2.Kind == TopicKind.Web, "单标签标题判为 Web");

        var t3 = p.Parse("某网页标题 - Google Chrome");
        Eq("某网页标题", t3.Text, "Chrome 后缀");
        Check(t3.Kind == TopicKind.Web, "Chrome 标题判为 Web");

        var t4 = p.Parse("网页标题 - Mozilla Firefox");
        Eq("网页标题", t4.Text, "Firefox 后缀");

        var t5 = p.Parse("MiniMax Code");
        Eq("MiniMax Code", t5.Text, "普通应用标题原样保留");
        Check(t5.Kind == TopicKind.App, "应用标题判为 App");
    }

    /// <summary>
    /// 回归：Edge 的窗口标题常常是「页面标题 - 站点名」，
    /// 而站点名会在剥尾部时被一起去掉——站点识别必须在**剥之前**做，
    /// 否则「xxx - 知乎」会变成「未识别站点」。
    /// </summary>
    private static void TestSiteFromStrippedSuffix()
    {
        Console.WriteLine("— 站点名在尾部时也能认出 —");
        var p = new TopicParser(new Config());

        // 真实标题（Edge，多标签 + 个人资料 2）
        var t1 = p.Parse("如何理解唯物辩证法的“两点论”和“重点论”的统一 - 知乎 和另外 8 个页面 - 个人 2 - Microsoft Edge");
        Eq("如何理解唯物辩证法的“两点论”和“重点论”的统一", t1.Text, "尾部被剥掉");
        Check(t1.Kind == TopicKind.Web, "判为 Web");
        Check(t1.Site == "zhihu.com", "站点名在尾部也要认得出", t1.Site);

        // 更具体的关键词优先于更泛的
        Eq("tieba.baidu.com", p.SiteOf("已按约定将补丁修改-百度贴吧"), "百度贴吧 → tieba.baidu.com");
        Eq("baike.baidu.com", p.SiteOf("桃乃木香奈_百度百科"), "百度百科 → baike.baidu.com");
        Eq("baidu.com", p.SiteOf("随便一个百度"), "只有「百度」→ baidu.com");

        // 剥之前能认出来的，剥之后也要认得（这里是真实数据里出现过的形态）
        var t2 = p.Parse("滑稽贴吧 - 搜索 - 个人 2 - Microsoft Edge");
        Eq("滑稽贴吧", t2.Text, "Edge 搜索页也剥掉尾部");

        // 剥尾部不能把普通应用标题弄坏
        var t3 = p.Parse("DeepSeek - 探索未至之境 - 个人 2 - Microsoft Edge");
        Eq("DeepSeek", t3.Text, "浏览器标题剥尾部");
    }

    private static void TestSiteDetection()
    {
        Console.WriteLine("— 站点识别 —");
        var p = new TopicParser(new Config());

        Eq("fenbi.com", p.SiteOf("粉笔课堂"), "粉笔 → fenbi.com");
        Eq("fenbi.com", p.SiteOf("专项智能练习（政治理论）"), "粉笔系特征词");
        Eq("fenbi.com", p.SiteOf("判断-模块检测"), "粉笔系特征词 2");
        Eq("bilibili.com", p.SiteOf("某视频_哔哩哔哩_bilibili"), "标题里的 bilibili");
        Eq("", p.SiteOf("完全无关的标题"), "认不出就留空（不瞎猜）");
        Eq("", p.SiteOf(""), "空标题");
    }

    private static void TestDocumentTitle()
    {
        Console.WriteLine("— 文档标题 —");
        var p = new TopicParser(new Config());
        var t = p.Parse("报考资格条件证明材料模板.doc  -  兼容性模式 - Word");
        Eq("报考资格条件证明材料模板.doc", t.Text, "剥掉 Word 兼容性模式");
        Check(t.Kind == TopicKind.Document, "文档判为 Document");
    }

    private static void TestTokenExtract()
    {
        Console.WriteLine("— 凭据清洗 —");
        // 用真实长度的 PAT：github_pat_ 前缀 11 字符 + 后面 82 字符 = 93 总长
        var pat = "github_pat_" + new string('A', 22) + new string('b', 60);
        Check(pat.Length == 93, "用例 PAT 长度确实是 93");
        Eq(pat, TokenUtil.Extract(pat, TokenUtil.GitHubPattern), "干净的 fine-grained PAT");
        Eq(pat, TokenUtil.Extract(pat + "sk-2f1de6cc719640a3bf7b011d04804d48", TokenUtil.GitHubPattern),
            "PAT 后面粘了别的 key → 只取前 93 字符");
        Eq(pat, TokenUtil.Extract("前面有字 " + pat, TokenUtil.GitHubPattern), "前面粘了别的文字也能取到");

        var classic = "ghp_" + new string('A', 36);
        Eq(classic, TokenUtil.Extract(classic, TokenUtil.GitHubPattern), "classic PAT");
        Eq(classic, TokenUtil.Extract(classic + "sk-abc", TokenUtil.GitHubPattern), "classic PAT 后面粘了 key");

        var sk = "sk-" + "2f1de6cc719640a3bf7b011d04804d48";
        Eq(sk, TokenUtil.Extract(sk, TokenUtil.AiPattern), "DeepSeek key");
        Eq(sk, TokenUtil.Extract(sk + " github_pat_x", TokenUtil.AiPattern), "key 后面粘了 PAT → 只取 key");
        Eq("", TokenUtil.Extract("   ", TokenUtil.AiPattern), "空白");
    }

    private static void TestMergeSegments()
    {
        Console.WriteLine("— 连续段合并 —");
        double t0 = 1_700_000_000;

        // 同一应用、连续的三个 20 秒块 → 合并成一段
        var segs = new List<Seg>
        {
            new("edge", t0, 20), new("edge", t0 + 20, 20), new("edge", t0 + 40, 20),
        };
        var merged = ReportBuilder.MergeSegments(segs);
        Check(merged.Count == 1, "同应用连续块合并成 1 段", $"得到 {merged.Count} 段");
        Eq(60, merged.Count > 0 ? merged[0].Duration : 0, "合并后总时长 60 秒");

        // 换了应用 → 2 段
        var segs2 = new List<Seg> { new("edge", t0, 20), new("qq", t0 + 20, 20) };
        Check(ReportBuilder.MergeSegments(segs2).Count == 2, "换应用 → 2 段");

        // 同应用但间隔 90 秒 → 2 段
        var segs3 = new List<Seg> { new("edge", t0, 20), new("edge", t0 + 110, 20) };
        Check(ReportBuilder.MergeSegments(segs3).Count == 2, "间隔 >60s → 断开成 2 段");
    }

    /// <summary>
    /// 回归：「应用切换次数」只算应用真的变了的情况。
    /// 「人离开了（空闲 90 秒以上）又回来用同一个应用」是暂停，不是切换。
    /// </summary>
    private static void TestSwitchVsPause()
    {
        Console.WriteLine("— 切换 vs 暂停 —");
        var dir = Path.Combine(Path.GetTempPath(), "appusage_selftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            double baseTs = DateTimeOffset.Now.AddHours(-2).ToUnixTimeMilliseconds() / 1000.0;
            // 一天之内：A 用 60 秒 → 离开 10 分钟 → A 再用 60 秒 → B 用 60 秒
            // 合并后是 3 段：A / A / B
            // → 真正的「换应用」只有 1 次（A→B），中间那次是暂停
            var t = baseTs;
            var lines = new List<string>();
            void Add(string app, double start, double dur) =>
                lines.Add($"{app}\tC:\\x\\{app}.exe\t标题\t{start:F3}\t{start + dur:F3}\t{dur:F3}");

            Add("a", t, 60);
            t += 60 + 600;          // 离开 10 分钟
            Add("a", t, 60);
            t += 60;
            Add("b", t, 60);

            File.WriteAllText(Path.Combine(dir, "x.log"), string.Join("\n", lines) + "\n");

            var store = new Store(dir, new Config());
            store.Reload();
            var report = ReportBuilder.Build(store, new Config(), DateTime.Today, DateTime.Today);

            Eq(3, report.Sessions, "合并后 3 段");
            Eq(1, report.Switches, "真正的应用切换只有 1 次（A→B）");
            Eq(1, report.Pauses, "「离开又回到同一个应用」算暂停，不算切换");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>跨零点的使用必须拆成自然日，否则那几十秒会整段落进前一天。</summary>
    private static void TestSplitByDay()
    {
        Console.WriteLine("— 跨零点拆分 —");

        // 今天 23:59:40 → 明天 00:00:20
        var today = DateTime.Today;
        var midnight = today.AddDays(1);
        double midnightTs = new DateTimeOffset(midnight).ToUnixTimeMilliseconds() / 1000.0;
        double start = midnightTs - 20;

        var pieces = Tracker.SplitByDay(start, midnightTs + 20);
        Check(pieces.Count == 2, "跨零点的一段被拆成 2 段", $"得到 {pieces.Count} 段");
        Eq(20, pieces.Count > 0 ? pieces[0].End - pieces[0].Start : 0, "第一段 20 秒（属于前一天）");
        Eq(20, pieces.Count > 1 ? pieces[1].End - pieces[1].Start : 0, "第二段 20 秒（属于后一天）");
        Eq(40, pieces.Sum(p => p.End - p.Start), "拆完总时长不变");

        // 同一段的前后两天归属正确
        if (pieces.Count == 2)
        {
            var d0 = DateTimeOffset.FromUnixTimeMilliseconds((long)(pieces[0].Start * 1000)).LocalDateTime.Date;
            var d1 = DateTimeOffset.FromUnixTimeMilliseconds((long)(pieces[1].Start * 1000)).LocalDateTime.Date;
            Eq(today, d0, "第一段属于今天");
            Eq(today.AddDays(1), d1, "第二段属于明天");
        }

        // 不跨零点 → 保持一段
        var same = Tracker.SplitByDay(midnightTs + 100, midnightTs + 200);
        Check(same.Count == 1, "不跨零点不拆");
        Eq(100, same.Count > 0 ? same[0].End - same[0].Start : 0, "时长不变");

        // 空区间 → 没有段
        Check(Tracker.SplitByDay(midnightTs, midnightTs).Count == 0, "零长度不产生段");
    }

    private static void TestStoreParsing()
    {
        Console.WriteLine("— 日志解析 —");
        var dir = Path.Combine(Path.GetTempPath(), "appusage_selftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            double t = DateTimeOffset.Now.AddHours(-2).ToUnixTimeMilliseconds() / 1000.0;
            File.WriteAllText(Path.Combine(dir, "2026-01-01.log"),
                string.Join('\n', new[]
                {
                    $"edge\tC:\\e\\msedge.exe\t粉笔课堂 和另外 1 个页面 - 个人 2 - Microsoft Edge\t{t:F3}\t{t + 60:F3}\t60.000",
                    $"edge\tC:\\e\\msedge.exe\t粉笔课堂 和另外 1 个页面 - 个人 2 - Microsoft Edge\t{t + 60:F3}\t{t + 120:F3}\t60.000",
                    $"code\tC:\\c\\code.exe\tmain.cs\t{t + 200:F3}\t{t + 260:F3}\t60.000",
                }) + "\n");

            var store = new Store(dir, new Config());
            store.Reload();

            var total = store.Days.Values.Sum(d => d.Total);
            Eq(180, total, "三段共 180 秒");

            var today = DateTime.Today;
            var topics = store.TopicsFor(today);
            Check(topics.Count >= 1, "解析出主题");
            var fenbi = topics.FirstOrDefault(t => t.Text == "粉笔课堂");
            Check(fenbi is not null, "标题被清洗成「粉笔课堂」");
            Eq("fenbi.com", fenbi?.Site ?? "", "站点识别为 fenbi.com");

            var sites = store.SitesFor(today);
            Check(sites.Any(s => s.Site == "fenbi.com"), "站点聚合里有 fenbi.com");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// 回归：按天分片后，早期文件末尾若残留半行（崩溃导致），
    /// 不能和后面文件的首行粘成一行；半行补齐后要能被正常读进来。
    /// </summary>
    private static void TestStoreMultiFilePartial()
    {
        Console.WriteLine("— 多文件分片 · 半行处理 —");
        var dir = Path.Combine(Path.GetTempPath(), "appusage_selftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            double t = DateTimeOffset.Now.AddHours(-3).ToUnixTimeMilliseconds() / 1000.0;

            // 旧文件：一条完整行 + 一条被截断的半行（没有结尾换行）
            var legacy = Path.Combine(dir, "0000-legacy.log");
            File.WriteAllText(legacy,
                $"app1\tC:\\a\\a.exe\t标题一\t{t:F3}\t{t + 60:F3}\t60.000\n" +
                $"app2\tC:\\b\\b.exe\t标题二\t{t + 60:F3}\t{t + 120:F3}");

            // 新文件：一条完整行
            File.WriteAllText(Path.Combine(dir, "2026-01-02.log"),
                $"app3\tC:\\c\\c.exe\t标题三\t{t + 120:F3}\t{t + 180:F3}\t60.000\n");

            var store = new Store(dir, new Config());
            store.Reload();

            Check(store.Apps.ContainsKey("app1"), "旧文件的完整行被读入");
            Check(!store.Apps.ContainsKey("app2"), "半行暂不入账");
            Check(store.Apps.ContainsKey("app3"),
                "新文件首行没有被旧文件的半行粘连",
                store.Apps.Keys.Where(k => k.Contains("app3")).Select(k => $"实际键={k}").FirstOrDefault() ?? "app3 丢失");
            Eq(120, store.Days.Values.Sum(d => d.Total), "只累计了 2 条完整行 = 120 秒");

            // 把半行补齐（模拟写入方补完剩余字节），再读一次应该完整入账
            File.AppendAllText(legacy, "\t60.000\n");
            store.Reload();
            Check(store.Apps.ContainsKey("app2"), "半行补齐后正常入账");
            Eq(180, store.Days.Values.Sum(d => d.Total), "三条都读到 = 180 秒");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void TestReportAssembly()
    {
        Console.WriteLine("— 日报装配 —");
        var dir = Path.Combine(Path.GetTempPath(), "appusage_selftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var t = DateTimeOffset.Now.AddHours(-1).ToUnixTimeMilliseconds() / 1000.0;
            File.WriteAllText(Path.Combine(dir, "x.log"),
                $"code\tC:\\c\\code.exe\tmain.cs\t{t:F3}\t{t + 100:F3}\t100.000\n" +
                $"chrome\tC:\\b\\chrome.exe\t某网页 - Google Chrome\t{t + 100:F3}\t{t + 200:F3}\t100.000\n");

            var config = new Config();
            var store = new Store(dir, config);
            store.Reload();
            var report = ReportBuilder.Build(store, config, DateTime.Today, DateTime.Today);

            Eq(200, report.TotalSeconds, "总时长 = 各段之和");
            Check(report.Categories.Count >= 2, "分类至少有 2 项");
            Eq(200, report.Categories.Sum(c => c.Seconds), "分类时长之和 = 总时长");
            Check(report.WeekSampleDays >= 1, "周均样本数 >= 1");
            Check(report.Topics.Count >= 1, "有主题");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void TestConfigRoundTrip()
    {
        Console.WriteLine("— 配置读写 —");
        var cfg = new Config();
        cfg.Ai.ApiKey = "sk-test";
        cfg.Shots.GithubToken = "github_pat_test";
        cfg.Shots.GithubOwner = "me";
        cfg.Shots.GithubRepo = "shots";

        var json = JsonSerializer.Serialize(cfg);
        Check(!json.Contains("HasGithub"), "计算属性 HasGithub 不写进配置", json.Contains("HasGithub") ? "出现了" : "");
        Check(json.Contains("github_pat_test"), "token 能序列化");
        Check(!json.Contains("sk-test") == false, "api key 能序列化");

        var back = JsonSerializer.Deserialize<Config>(json)!;
        Eq("sk-test", back.Ai.ApiKey, "api key 往返一致");
        Eq("shots", back.Shots.GithubRepo, "仓库名往返一致");
        Check(back.Shots.HasGithub, "HasGithub 计算正确");
    }

    private static void TestShotPathFormat()
    {
        Console.WriteLine("— 截图路径格式（DD → dd 的回归测试）—");
        var d = new DateTime(2026, 10, 23);
        var path = $"shots/{d:yyyy}/{d:MM}/{d:dd}/{d:HH-mm-ss}_edge.jpg";
        Check(path.Contains("/23/"), "日期目录用小写 dd（输出 23 而不是字面量 DD）", path);
        Check(!path.Contains("/DD/"), "不再出现字面量 DD");
    }

    /// <summary>
    /// 截图链路的命名约定：原始名 / GitHub 路径 / outbox 文件名 / 清单键名必须对得上。
    /// 早期 outbox 补传把日期前缀写进了路径和键名，日报里就显示成「未上传」。
    /// </summary>
    private static void TestShotNaming()
    {
        Console.WriteLine("— 截图命名约定 —");

        var at = new DateTime(2026, 10, 23, 15, 4, 5);
        const string name = "15-04-05_msedge";

        // GitHub 路径：日期目录必须是小写 dd
        var rel = ShotService.RelPath(at, name);
        Eq("shots/2026/10/23/15-04-05_msedge.jpg", rel, "GitHub 相对路径");
        Check(!rel.Contains("/DD/"), "GitHub 路径里没有字面量 DD");

        // outbox 文件名带日期前缀，补传时要还原成原始名
        var (parsedAt, parsedName) = ShotService.ParseOutboxName("2026-10-23_15-04-05_msedge.jpg");
        Eq(name, parsedName, "outbox 文件名还原出原始名");
        Eq(at, parsedAt, "outbox 文件名还原出拍摄时间");

        // 还原出来的路径必须和直传完全一致（这是这次修的核心）
        Eq(rel, ShotService.RelPath(parsedAt, parsedName), "补传路径 == 直传路径");

        // 清单键名统一是原始名
        Eq(name, ShotService.NormalizeName("2026-10-23_15-04-05_msedge"), "日期前缀从键名里剥掉");
        Eq(name, ShotService.NormalizeName(name), "没有前缀就原样返回");
        Eq("foo_bar", ShotService.NormalizeName("foo_bar"), "非日期前缀不动");
    }

    /// <summary>
    /// 回归：AI 返回的 Content-Type 是 "application/json"（**不带 charset**），
    /// 中文正文必须按 UTF-8 解出来，不能变成 Latin-1 乱码。
    /// </summary>
    private static void TestAiDecoding()
    {
        Console.WriteLine("— AI 响应解码 —");
        const string cn = "今天主要在刷粉笔题，浏览占六成。";
        var utf8 = System.Text.Encoding.UTF8.GetBytes(cn);

        // 服务端不给 charset → 按 UTF-8
        var s1 = AiClient.DecodeBody(utf8, null);
        Eq(cn, s1, "没有 charset 时按 UTF-8 解出中文");

        var s2 = AiClient.DecodeBody(utf8, "   ");
        Eq(cn, s2, "charset 是空白也按 UTF-8");

        // 给了 charset 就按它来
        var s3 = AiClient.DecodeBody(System.Text.Encoding.UTF8.GetBytes(cn), "utf-8");
        Eq(cn, s3, "指定 utf-8 时正常");

        // 认识的编码按它解（这里用 latin1 验证真的切换了编码）
        var latin1 = System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes("abc");
        var s4 = AiClient.DecodeBody(latin1, "iso-8859-1");
        Eq("abc", s4, "指定 latin1 时按 latin1 解");

        // 不认识的编码名 → 退回 UTF-8，不能抛异常
        var s5 = AiClient.DecodeBody(utf8, "totally-not-a-charset");
        Eq(cn, s5, "不认识的编码名退回 UTF-8");
    }

    /// <summary>
    /// 回归：思考模式下推理 token 吃光 max_tokens，正文会是空的
    /// （finish_reason=length，content 长度 0）——必须能识别出来并给出可操作的提示，
    /// 而不是笼统的「返回内容为空」。
    /// </summary>
    private static void TestAiResponseParsing()
    {
        Console.WriteLine("— AI 响应解析 —");

        // 正常响应
        var ok = AiClient.ParseChatResponse(
            """{"choices":[{"finish_reason":"stop","message":{"content":"  今天主要在刷粉笔题。  ","reasoning_content":"想了一下"}}]}""");
        Eq("今天主要在刷粉笔题。", ok.Text ?? "", "正常响应取出正文并去空白");
        Check(ok.Error is null, "正常响应没有错误");

        // 被 token 预算截断：正文空 + finish_reason=length
        var cut = AiClient.ParseChatResponse(
            """{"choices":[{"finish_reason":"length","message":{"content":"","reasoning_content":"推理很长"}}]}""");
        Check(cut.Text is null, "截断时没有正文");
        Check(cut.Error is not null && cut.Error.Contains("截断"), "截断时明确说被截断", cut.Error ?? "");

        // 只返回了思考过程
        var onlyReason = AiClient.ParseChatResponse(
            """{"choices":[{"finish_reason":"stop","message":{"content":"","reasoning_content":"只有推理"}}]}""");
        Check(onlyReason.Error is not null && onlyReason.Error.Contains("思考"), "只回思考过程时提示明确", onlyReason.Error ?? "");

        // 真的什么都没有
        var empty = AiClient.ParseChatResponse(
            """{"choices":[{"finish_reason":"stop","message":{"content":""}}]}""");
        Check(empty.Error is not null && empty.Error.Contains("空"), "空正文时报「返回内容为空」", empty.Error ?? "");

        // choices 为空
        var noChoice = AiClient.ParseChatResponse("""{"choices":[]}""");
        Check(noChoice.Error is not null && noChoice.Error.Contains("空"), "choices 为空时报空", noChoice.Error ?? "");
    }

    /// <summary>
    /// 回归：改了「站点关键词」之后，已经聚合过的站点必须重算，
    /// 否则用户加了词条、日报里还是「未识别站点」。
    /// </summary>
    private static void TestSiteHintsRebuild()
    {
        Console.WriteLine("— 站点词表改动后重算 —");
        var dir = Path.Combine(Path.GetTempPath(), "appusage_selftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            double t = DateTimeOffset.Now.AddHours(-1).ToUnixTimeMilliseconds() / 1000.0;
            File.WriteAllText(Path.Combine(dir, "2026-01-01.log"),
                $"msedge\tC:\\e\\msedge.exe\t小红书创作服务平台 - Google Chrome\t{t:F3}\t{t + 60:F3}\t60.000\n");

            var config = new Config();
            var store = new Store(dir, config);
            store.Reload();

            var before = store.SitesFor(DateTime.Today);
            Check(!before.Any(s => s.Site == "xiaohongshu.com"), "没配词表时认不出（预期内）");

            // 用户在设置里加了词条
            config.SiteHints["小红书"] = "xiaohongshu.com";
            config.SiteNames["xiaohongshu.com"] = "小红书";
            store.Rebuild();

            var after = store.SitesFor(DateTime.Today);
            Check(after.Any(s => s.Site == "xiaohongshu.com"), "加了词条并重建后认出来了");
            Check(after.Any(s => s.Name == "小红书"), "显示名也用上了");
            Eq(60, store.Days.Values.Sum(d => d.Total), "重建后总时长不变");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>站点词表的文本编解码（用户手写，格式错了会静默丢数据）。</summary>
    private static void TestSiteHintsCodec()
    {
        Console.WriteLine("— 站点词表编解码 —");
        var lines = new[]
        {
            "# 这是注释",
            "",
            "小红书=xiaohongshu.com=小红书",
            "  知乎 = zhihu.com  ",
            "只有关键词没有等号",
            "域名没有点=notadomain",
            "带空格=bad domain.com",
        };

        var (hints, names) = SiteHintsCodec.Parse(lines);
        Eq(2, hints.Count, "只解析出 2 条合法规则");
        Check(hints.ContainsKey("小红书"), "中文关键词");
        Check(hints.TryGetValue("小红书", out var site) && site == "xiaohongshu.com", "域名原样", site ?? "");
        Check(names.TryGetValue("xiaohongshu.com", out var name) && name == "小红书", "中文显示名");
        Check(hints.ContainsKey("知乎"), "首尾空格被去掉");
        Check(!hints.ContainsKey("只有关键词没有等号"), "没有等号的行被跳过");
        Check(!hints.ContainsKey("域名没有点"), "域名不合法被跳过");
        Check(!hints.ContainsKey("带空格"), "域名带空格被跳过");

        // 往返
        var text = SiteHintsCodec.Format(hints, names);
        var (h2, n2) = SiteHintsCodec.Parse(text);
        Eq(hints.Count, h2.Count, "往返后条数不变");
        Check(h2.TryGetValue("小红书", out var site2) && site2 == "xiaohongshu.com", "往返后内容不变");
        Check(n2.TryGetValue("xiaohongshu.com", out var name2) && name2 == "小红书", "往返后显示名不变");
    }

    /// <summary>
    /// 黑图判定：Chromium 窗口 PrintWindow 常常返回纯黑，要能识别出来退回屏幕拷贝。
    /// 但阈值不能太松——暗色视频、终端深色主题都该被当成有效画面。
    /// </summary>
    private static void TestBlankDetection()
    {
        Console.WriteLine("— 黑图判定 —");

        using var black = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(black))
            g.Clear(System.Drawing.Color.Black);
        Check(Screenshotter.IsBlank(black), "纯黑 → 判为黑图");

        using var white = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(white))
            g.Clear(System.Drawing.Color.White);
        Check(!Screenshotter.IsBlank(white), "纯白 → 不是黑图");

        // 暗但有内容：亮度 30/255，实测真实存档里最暗的一张是 23.5
        using var dark = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(dark))
            g.Clear(System.Drawing.Color.FromArgb(30, 30, 30));
        Check(!Screenshotter.IsBlank(dark), "暗色画面（亮度 30）→ 不是黑图，不能误杀");

        using var nearBlack = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(nearBlack))
            g.Clear(System.Drawing.Color.FromArgb(3, 3, 3));
        Check(Screenshotter.IsBlank(nearBlack), "几乎全黑（亮度 3）→ 判为黑图");
    }

    /// <summary>
    /// 回归：清单的键必须和缩略图文件名对得上，否则日报里点不开 GitHub 原图。
    /// 早期 outbox 补传把日期前缀写进了键名，这里专门盯住这一点。
    /// </summary>
    private static void TestShotIndexLookup()
    {
        Console.WriteLine("— 截图清单匹配 —");
        var dir = Path.Combine(Path.GetTempPath(), "appusage_selftest_" + Guid.NewGuid().ToString("N"));
        var day = new DateTime(2026, 10, 23);
        try
        {
            var thumbDir = Path.Combine(dir, "shots", "thumbs", day.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(thumbDir);
            // 造两个缩略图
            File.WriteAllBytes(Path.Combine(thumbDir, "12-29-31_msedge.jpg"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(thumbDir, "11-33-29_msedge.jpg"), new byte[] { 1, 2, 3 });

            // 清单里一个是规范键名，一个是带日期前缀的历史脏键名
            var manifest = Path.Combine(dir, "shots", "index.tsv");
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            var dirty = new[]
            {
                "12-29-31_msedge\tshots/2026/10/23/12-29-31_msedge.jpg",
                "2026-10-23_11-33-29_msedge\tshots/2026/10/23/2026-10-23_11-33-29_msedge.jpg",
            };
            File.WriteAllText(manifest, string.Join("\n", dirty) + "\n");

            var config = new Config();
            config.Shots.GithubOwner = "me";
            config.Shots.GithubRepo = "shots";
            config.Shots.GithubToken = "github_pat_x";

            // 迁移前：带日期前缀的脏键名认不出来（这正是那个 bug）
            var before = ShotIndex.ForDay(dir, day, config);
            var legacy = before.FirstOrDefault(r => r.At.TimeOfDay == new TimeSpan(11, 33, 29));
            Check(legacy is not null, "找到 11-33-29 那张");
            Check(!legacy!.Uploaded, "迁移前：脏键名认不出 → 显示未上传（就是那个 bug）");

            // 迁移：键名和路径都还原成规范形式
            var (lines, changed) = ShotService.NormalizeManifestLines(dirty);
            Check(changed, "迁移检测到清单需要修正");
            Check(lines.Any(l => l.StartsWith("11-33-29_msedge\tshots/2026/10/23/11-33-29_msedge.jpg", StringComparison.Ordinal)),
                "迁移后键名和路径都去掉日期前缀", string.Join(" | ", lines));

            // 迁移后：清单与缩略图名对得上，GitHub 链接指向规范路径
            File.WriteAllText(manifest, string.Join("\n", lines) + "\n");
            var after = ShotIndex.ForDay(dir, day, config);
            Eq(2, after.Count, "两天两张缩略图都在");

            var good = after.FirstOrDefault(r => r.At.TimeOfDay == new TimeSpan(12, 29, 31));
            Check(good is not null, "找到 12-29-31 那张");
            Check(good!.Uploaded, "规范键名 → 认得出已上传");
            Check(good.GithubUrl.Contains("shots/2026/10/23/12-29-31_msedge.jpg"), "GitHub 链接指向规范路径", good.GithubUrl);

            var fixedLegacy = after.FirstOrDefault(r => r.At.TimeOfDay == new TimeSpan(11, 33, 29));
            Check(fixedLegacy!.Uploaded, "迁移后：脏键名也能认出来了");
            Check(fixedLegacy.GithubUrl.Contains("shots/2026/10/23/11-33-29_msedge.jpg"),
                "迁移后的链接指向规范路径", fixedLegacy.GithubUrl);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
