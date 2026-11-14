using System;
using Microsoft.Win32;
using Wantodo.Core;

namespace Wantodo.App.Appearance;

/// <summary>
/// 读系统「应用主题」：HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
/// 的 <c>AppsUseLightTheme</c>。读不到就按深色（与 <see cref="ThemePalette.DarkTheme"/> 一致）。
/// 只在 <c>Theme = System</c> 时使用；启动时读一次，不订阅系统主题变化（plan 未排期）。
/// </summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public static bool IsLightApplicationTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue(AppsUseLightThemeValue) is int value)
            {
                return value != 0;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            AppLog.Warn($"读取系统主题失败，按深色处理：{ex.Message}");
        }

        return false;
    }
}
