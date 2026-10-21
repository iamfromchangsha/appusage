using System.Globalization;
using System.Text;

namespace AppUsage.Core;

/// <summary>
/// 后台采集线程：每秒读一次前台窗口，把「连续使用同一应用」的时间段追加写进日志。
/// 空闲超过阈值（默认 90 秒没人碰键鼠）时完全不计时长。
/// </summary>
internal sealed class Tracker : IDisposable
{
    private readonly string _logPath;
    private readonly Config _config;
    private readonly HashSet<string> _excluded;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeLock = new();

    private Thread? _thread;
    private StreamWriter? _writer;

    private string _currentKey = string.Empty;
    private string _currentExe = string.Empty;
    private string _currentTitle = string.Empty;
    private double _currentStart;

    public bool IsRunning => _thread is { IsAlive: true };
    public DateTime StartedAt { get; private set; }

    public Tracker(string logPath, Config config)
    {
        _logPath = logPath;
        _config = config;
        _excluded = new HashSet<string>(config.ExcludedExes, StringComparer.OrdinalIgnoreCase);
    }

    public void Start()
    {
        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _writer = new StreamWriter(
            new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough),
            new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        StartedAt = DateTime.Now;
        _thread = new Thread(Loop) { IsBackground = true, Name = "AppUsage.Tracker", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Stop()
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _thread?.Join(2000); } catch { /* ignore */ }
        FlushCurrent();
        lock (_writeLock)
        {
            try { _writer?.Flush(); _writer?.Dispose(); } catch { /* ignore */ }
            _writer = null;
        }
    }

    private void Loop()
    {
        var pollMs = Math.Max(200, (int)(_config.PollInterval * 1000));
        var flushSec = Math.Max(5.0, _config.FlushInterval);
        var selfPid = (uint)Environment.ProcessId;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var (exe, title, pid) = NativeMethods.GetForeground();
                var key = string.Empty;
                if (exe.Length > 0 || title.Length > 0)
                {
                    key = _config.AppKey(exe, title);
                    if (_excluded.Contains(key)) key = string.Empty;
                    // 别把自己这个程序也统计进去
                    if (pid != 0 && pid == selfPid) key = string.Empty;
                }

                // 人不在就不计时长
                if (NativeMethods.IdleSeconds() >= _config.IdleSeconds) key = string.Empty;

                var now = Timestamp();

                if (key != _currentKey)
                {
                    FlushCurrent();
                    _currentKey = key;
                    _currentExe = exe;
                    _currentTitle = title;
                    _currentStart = now;
                }
                else if (key.Length > 0)
                {
                    if (title.Length > 0) _currentTitle = title;
                    if (now - _currentStart >= flushSec)
                    {
                        WriteSegment(_currentKey, _currentExe, _currentTitle, _currentStart, now);
                        _currentStart = now;
                    }
                }
            }
            catch { /* 单次采样失败不致命，继续下一轮 */ }

            if (_cts.Token.WaitHandle.WaitOne(pollMs)) break;
        }
    }

    private void FlushCurrent()
    {
        if (_currentKey.Length == 0) return;
        var now = Timestamp();
        if (now - _currentStart < _config.MinSpanSeconds) { _currentKey = string.Empty; return; }
        WriteSegment(_currentKey, _currentExe, _currentTitle, _currentStart, now);
        _currentKey = string.Empty;
    }

    private void WriteSegment(string key, string exe, string title, double start, double end)
    {
        var duration = end - start;
        if (duration <= 0) return;
        var line = string.Join('\t',
            Sanitize(key),
            Sanitize(exe),
            Sanitize(title),
            start.ToString("F3", CultureInfo.InvariantCulture),
            end.ToString("F3", CultureInfo.InvariantCulture),
            duration.ToString("F3", CultureInfo.InvariantCulture));

        lock (_writeLock)
        {
            try { _writer?.WriteLine(line); } catch { /* ignore */ }
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c is '\t' or '\r' or '\n' ? ' ' : c);
        }
        if (sb.Length > 240) sb.Length = 240;
        return sb.ToString();
    }

    private static double Timestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
