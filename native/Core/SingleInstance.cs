using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

namespace AppUsage.Core;

/// <summary>
/// 单实例守卫 + 进程间通道。
/// <para>
/// <b>为什么必须有</b>：两个实例同时跑会往同一个日志文件里重复写 —— 同一段时间被记两遍，
/// 截图额度也会被吃两次，数据直接被污染。
/// </para>
/// <para>
/// <b>为什么还要管道</b>：本机开机自启走的是托盘模式，所以「双击 EXE 打开主界面」时
/// 托盘实例<b>一定</b>已经在跑了。要是第二个实例只会弹一句「已在运行」，
/// 就把打开主界面的正常途径堵死了。所以第二个实例改为通过命名管道通知已有实例，
/// 让它把主窗口显示出来。
/// </para>
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _key;
    private readonly string _pipeName;
    private readonly ConcurrentQueue<string> _requests = new();
    private CancellationTokenSource? _cts;
    private Task? _listener;

    /// <summary>true = 我是第一个实例，应该正常常驻。</summary>
    public bool IsPrimary { get; }

    public SingleInstance(string dataDir)
    {
        // 同一个数据目录 = 同一个实例；不同 --data 互不干扰
        _key = StableHash(Path.GetFullPath(dataDir));
        _pipeName = "AppUsage." + _key;
        _mutex = new Mutex(false, @"Local\AppUsage." + _key, out var createdNew);
        IsPrimary = createdNew;
    }

    /// <summary>
    /// 通知已在运行的实例「把主窗口显示出来」。
    /// 返回 false = 对方没接上（可能正在启动，或已经退出了）。
    /// </summary>
    public bool NotifyShow() => Notify("show", TimeSpan.FromSeconds(3));

    /// <summary>主实例：后台监听请求。收到的命令进队列，由 UI 线程取走处理。</summary>
    public void Listen()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _listener = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        _pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    var buf = new byte[16];
                    var n = await server.ReadAsync(buf, ct).ConfigureAwait(false);
                    if (n > 0)
                    {
                        var cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();
                        if (cmd.Length > 0) _requests.Enqueue(cmd);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* 管道出问题了，歇一轮再试 */ }
            }
        }, ct);
    }

    /// <summary>取出一条待处理命令（必须在 UI 线程调用）。</summary>
    public bool TryTakeRequest(out string command) => _requests.TryDequeue(out command!);

    private bool Notify(string command, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var bytes = Encoding.UTF8.GetBytes(command);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(500);
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
                return true;
            }
            catch (TimeoutException) { /* 对方还没起来，再试 */ }
            catch (IOException) { /* 管道忙，再试 */ }
            catch { return false; }
            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>
    /// 稳定的字符串哈希（FNV-1a）。
    /// 不能用 string.GetHashCode —— 它每个进程都随机，取模后名字就对不上了。
    /// </summary>
    internal static string StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s.ToLowerInvariant())
            {
                h ^= c;
                h *= 16777619;
            }
            return h.ToString("X8");
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _listener?.Wait(500); } catch { /* ignore */ }
        try { _cts?.Dispose(); } catch { /* ignore */ }
        try { _mutex.Dispose(); } catch { /* ignore */ }
    }
}
