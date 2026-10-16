namespace MultiLauncher;

// ============================================================================
// 动画曲线与时钟的唯一家（2026-10-16 收口，方案文档 docs/design/方案-动效优化方向-2026-10-15.md §5.1）
// ============================================================================
// 收口之前全项目两处过渡各写一套：StackedToggle 按**帧计数**（12 帧除尽 180ms）+ **线性**，
// Toast 按**实际时间** + 三次缓出。同一族"逐帧过渡"两套曲线，手感必然不一样，以后加第三处的人还得再选一次。
// 这里只收两样东西：**曲线**与**心跳档位**。跑道一条都没新增 —— 仍然是原来那两条
// （控件内自绘 alpha / 整窗 Opacity+Location），零第三方、零新 interop，符合"保持轻量、尽量不引入新的方法"。
//
// 三条口径（写死，别在调用点各拼一套）：
//   1) 进度按**实际经过的时间**算，不按 tick 数除尽 —— WM_TIMER 会被消息泵合并，按帧计数会让总时长漂。
//   2) 心跳 15ms（沿用 Toast / StackedToggle 原有那一档，没改）。它只决定"多久看一眼表"，不决定动画总时长。
//   3) **位置动、透明度动、颜色瞬切**：这一处只提供 0→1 的进度，不提供两支点色之间的插值 ——
//      插值会造出 UiTheme 色表外的中间色，违反 conventions「悬停 / 选中 / 置灰这类取色也一律取色表里既有的一档」。
internal static class UiMotion
{
    /// <summary>定时器心跳（毫秒）。动画总时长由各调用点的 *Ms 常量定，与这个数无关。</summary>
    public const int FrameMs = 15;

    /// <summary>**跟随类**那一档时长（毫秒）：光标一动就该有反应的东西都吃它 —— 悬停擦入（按钮与叠放按钮）、
    /// 页头胶囊滑动。比"确认一次动作"那一档（叠放轮换 / toast 出现 = 180ms）快半档，
    /// 因为悬停是高频扫过事件，慢了就变成"跟不上手"。开关行程另有一档 200ms（照用户给的参考值）。</summary>
    public const int FollowMs = 150;

    /// <summary>入场曲线：ease-out cubic = 1−(1−t)³（DEC-075 变体 B 定稿那一条，toast 滑入用的就是它）。</summary>
    public static double EaseOutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double u = 1 - t;
        return 1 - u * u * u;
    }

    /// <summary>退场曲线：ease-in quad = t²（"飘走"感，与出现同向；toast 退场用的那一条）。</summary>
    public static double EaseInQuad(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t;
    }

    /// <summary>**位移类**曲线：两端速度为 0 的 smoothstep = t²(3−2t)，是 CSS 默认 <c>ease</c>
    /// （cubic-bezier(.25,.1,.25,1)）的多项式近似。
    /// 为什么位移不用 EaseOutCubic：心跳 15ms 之下 150ms 只有十帧，三次缓出前三帧就走掉 66% 行程、
    /// 后面七帧爬剩下的 34% —— 肉眼读作"跳一下再慢慢蹭过去"（2026-10-16 他实跑报"卡顿"就是这个）。
    /// 淡入淡出那一族仍用 EaseOutCubic / EaseInQuad：不透明度前重后轻读作"干脆"，没有台阶问题。</summary>
    public static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>从 from 缓到 to（擦入这类"目标值可能中途翻面"的过渡共用这一条；eased 吃上面两条曲线之一）。</summary>
    public static double Ramp(double from, double to, double eased) => from + (to - from) * Math.Clamp(eased, 0, 1);

    /// <summary>位移类的一整条算式（进度 = EaseInOut(elapsed / total)）：悬停擦入、胶囊滑动、圆点行程三族共用，
    /// 各自只喂自己的 from / to / total —— 曲线与"到点归位"只有一处写法，自检也只按纯函数喂数。</summary>
    public static double RampAt(double from, double to, int elapsedMs, int totalMs) =>
        Ramp(from, to, EaseInOut(elapsedMs / (double)totalMs));

    /// <summary>矩形按 t 插值（纯函数 = 画与自检共用这一个家）。分段控件的胶囊滑动只动 X 与宽，高与顶不动。</summary>
    public static Rectangle LerpRect(Rectangle a, Rectangle b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rectangle(
            (int)Math.Round(a.X + (b.X - a.X) * t),
            (int)Math.Round(a.Y + (b.Y - a.Y) * t),
            (int)Math.Round(a.Width + (b.Width - a.Width) * t),
            (int)Math.Round(a.Height + (b.Height - a.Height) * t));
    }
}
