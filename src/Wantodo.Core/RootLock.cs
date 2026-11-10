using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Wantodo.Core;

/// <summary>
/// 数据根上的 <c>.lock</c>：记录 pid + 启动时间，防止两个实例写同一个根
/// （单实例 Mutex 只覆盖同一会话；换了数据根或换了会话就需要它）。
/// </summary>
public sealed class RootLock : IDisposable
{
    private readonly string _path;
    private readonly int _pid;
    private bool _released;

    private RootLock(string path, int pid)
    {
        _path = path;
        _pid = pid;
    }

    public string Path => _path;

    /// <summary>拿锁。返回 null 表示有活着的进程占着，<paramref name="message"/> 说明是谁。</summary>
    public static RootLock? TryAcquire(string path, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "锁文件路径为空";
            return null;
        }

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var pid = Environment.ProcessId;

        if (File.Exists(path))
        {
            var holder = ReadHolder(path);
            if (holder is not null && IsProcessAlive(holder.Value.Pid))
            {
                message = holder.Value.Pid == pid
                    ? "数据目录已被本进程占用（重复获取？）"
                    : $"数据目录被另一个进程占用（pid={holder.Value.Pid}，开始于 {holder.Value.StartedAt:HH:mm:ss}）";
                return null;
            }
        }

        var line = string.Join(
            '\t',
            pid.ToString(CultureInfo.InvariantCulture),
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            Environment.MachineName,
            Environment.UserName);

        File.WriteAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        return new RootLock(path, pid);
    }

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        try
        {
            // 只删自己写的锁，避免把接管者的锁删掉。
            var holder = ReadHolder(_path);
            if (holder is not null && holder.Value.Pid == _pid && File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (Exception)
        {
            // 删不掉不影响退出。
        }
    }

    private static (int Pid, DateTimeOffset StartedAt)? ReadHolder(string path)
    {
        try
        {
            var first = File.ReadLines(path).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(first))
            {
                return null;
            }

            var parts = first.Split('\t');
            if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
            {
                return null;
            }

            return DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var started)
                ? (pid, started)
                : (pid, DateTimeOffset.MinValue);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // 进程不存在
        }
        catch (Exception)
        {
            return true; // 拿不到信息就当它还活着，宁可拒绝也不双写
        }
    }
}
