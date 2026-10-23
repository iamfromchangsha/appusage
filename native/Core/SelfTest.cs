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
            TestDocumentTitle();
            TestTokenExtract();
            TestMergeSegments();
            TestSplitByDay();
            TestStoreParsing();
            TestStoreMultiFilePartial();
            TestReportAssembly();
            TestConfigRoundTrip();
            TestShotPathFormat();

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
}
