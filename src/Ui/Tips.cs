using System.Runtime.CompilerServices;
using System.Text;

namespace MultiLauncher;

// WinForms 的 ToolTip 没有宽度上限 API（.NET 8+ 已移除），长文本会渲染成超宽单行。
// 统一经 Tip.Set 预折行：每行 <= 28 字（9pt 微软雅黑 CJK 约 12px，28 字约 350px）。
// 约定：单条 tooltip <= 80 字，只写语义摘要，长篇说明放文档。
static class Tip
{
    const int MaxCharsPerLine = 28;
    static readonly ToolTip Shared = new();

    public static void Set(Control control, string? text)
    {
        var wrapped = Wrap(text);
        Remember(control, text ?? "", wrapped ?? "");
        Shared.SetToolTip(control, wrapped);
        // 2026-10-02 反馈（"悬浮在参数上方没提示"）：圆角输入框的显示面是**内层 TextBox 子窗口**，
        // 鼠标落点在那里——只挂外框的话系统不认（子窗口盖住父窗口的提示区域）。内层必须也挂一份。
        if (control is RoundedTextBox box) Shared.SetToolTip(box.Inner, Wrap(text));
    }

    /// <summary>单行档：不预折行，原样交给系统画（WinForms 的 ToolTip 没有宽度上限 API = 长文本就是一整行）。
    /// 28 字那一档是给「参数说明」那类长句准备的；状态行那一类（「Strata · 待机 · 模型未加载 · 端口 8080」= 29 字）
    /// 被折成两行反而读不断句 —— 他 2026-10-15 点名"我需要 tooltip 能整行显示,不换行"。</summary>
    public static void SetOneLine(Control control, string? text)
    {
        Remember(control, text ?? "", text ?? "");
        Shared.SetToolTip(control, text ?? "");
    }

    static void Remember(Control control, string raw, string rendered)
    {
        Last.Remove(control); Last.Add(control, raw);
        Rendered.Remove(control); Rendered.Add(control, rendered);
    }

    /// <summary>自检用：读回当前挂在某个控件上的提示（DEC-079 的「继承全局」标注是动态文案，只能这么核）。
    /// ToolTip 自己没有"取回某控件的提示"这种实例 API，所以 Set 时顺手记一份原文（折行前的那一份）；
    /// 用 ConditionalWeakTable：控件销毁就跟着走，不额外管释放（DEC-061 那条"带句柄的协作对象要点名释放"的例外）。</summary>
    static readonly ConditionalWeakTable<Control, string> Last = new();

    public static string TextOf(Control control) => Last.TryGetValue(control, out var v) ? v : "";

    /// <summary>自检用：**实际交给系统画的那一份**（折行后）。判"有没有被折成两行"只能读这个 ——
    /// <see cref="TextOf"/> 存的是折行之前的原文，走 <see cref="SetOneLine"/> 与走 <see cref="Set"/> 在它俩没区别。</summary>
    static readonly ConditionalWeakTable<Control, string> Rendered = new();

    public static string RenderedOf(Control control) => Rendered.TryGetValue(control, out var v) ? v : "";

    static string? Wrap(string? text)    {
        if (string.IsNullOrEmpty(text)) return text;
        var lines = text.Split('\n');
        if (lines.Length == 1) return WrapLine(lines[0]);
        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(WrapLine(lines[i]));
        }
        return sb.ToString();
    }

    static string WrapLine(string line)
    {
        if (line.Length <= MaxCharsPerLine) return line;
        var sb = new StringBuilder(line.Length + 8);
        for (int i = 0; i < line.Length; i += MaxCharsPerLine)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(line, i, Math.Min(MaxCharsPerLine, line.Length - i));
        }
        return sb.ToString();
    }
}
