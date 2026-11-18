using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Wantodo.App.Windows;

/// <summary>
/// 认出「这一份鼠标捕获属于弹出层」。
///
/// WPF 的 <see cref="Popup"/>（下拉框、颜色面板、右键菜单、ToolTip）在
/// <c>StaysOpen=false</c> 时会把鼠标捕获挂到内部的 <c>PopupRoot</c> 上，靠它判定
/// 「用户点了外面」而自动关掉。全局钩子的捕获自愈（<see cref="TopEdgePanelService"/>）
/// 若把这份捕获当成泄漏清掉，等于替用户点了一下外面：面板在按下的瞬间关闭，
/// 下拉项永远收不到这一次点击 —— 用户看到的就是「待办卡的选栏目、新建栏目点了没反应」。
/// </summary>
internal static class PopupCapture
{
    /// <summary>这份捕获是不是弹出层的（此时既不能清捕获，也不能因为「点在面板矩形外」收起面板）。</summary>
    public static bool IsPopup(object? captured)
    {
        if (captured is null)
        {
            return false;
        }

        if (captured is Popup or ContextMenu or ToolTip)
        {
            return true;
        }

        // PopupRoot 是 WPF 的内部类型（不可直接引用），类型名是唯一稳定的判据。
        return LooksLikePopupTypeName(captured.GetType().Name);
    }

    /// <summary>类型名判据单独暴露出来，探针没有 PopupRoot 实例也能验它。</summary>
    public static bool LooksLikePopupTypeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.Contains("Popup", StringComparison.Ordinal)
            || name.Contains("ContextMenu", StringComparison.Ordinal)
            || name.Contains("ToolTip", StringComparison.Ordinal);
    }
}
