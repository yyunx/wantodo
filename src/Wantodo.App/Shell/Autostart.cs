using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Wantodo.App.Shell;

/// <summary>
/// 开机自启只写 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>，
/// 不碰系统级路径、不需要管理员权限（AGENTS.md 硬规则 6）。
/// </summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Wantodo";

    /// <summary>当前进程的可执行文件；发布版这里就是 Wantodo.exe 的完整路径。</summary>
    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Wantodo.exe");

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return string.Equals(Unquote(value), ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            AppLog.Warn($"读自启项失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>开 / 关自启。返回操作后实际的开关状态（失败时与请求值不同，方便调用方回滚界面）。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                AppLog.Warn("拿不到 HKCU Run 键，自启设置未生效");
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
                AppLog.Info($"已开启开机自启：{ExecutablePath}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                AppLog.Info("已关闭开机自启");
            }

            return IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            AppLog.Error($"写自启项失败（{enabled}）", ex);
            return IsEnabled();
        }
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 1 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }
}
