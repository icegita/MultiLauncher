using System.Text;

namespace MultiLauncher;

// 自检跑一趟的共享状态（2026-10-15 从 RunBody 的局部变量原样提升成字段）。
// 一进程只跑一趟自检（Run 只被 --selftest 调用一次），按进程收口够用，与全仓"按进程收口的闸"同族。
// **字段名故意沿用原局部变量名（小写开头）**：用例块整体搬进各域文件时，块内引用一行都不用改 ——
// 这是这套拆法的安全底线（搬家不改字，编译器和 diff 都能直接证明"没动"）。
internal static partial class SelfTest
{
    // Check/W 的本体 = 原 RunBody 里的两个局部闭包（Delegate 支持可选参数，调用点 Check(a, b) 照旧）
    internal delegate void CheckFn(string name, bool ok, string detail = "");

    internal static string runTmp = "";
    internal static string llamaHome = "";
    internal static string ninferHome = "";
    internal static string root = "";

    // 跨用例块共享的扫描结果（原 RunBody try 里的局部量；models 一直用到 5a 之后，target 到物化组为止）
    internal static List<ModelEntry> models = new();
    internal static ModelEntry? target = null;
    internal static string localVersion = "";
    internal static string routerLive = "";   // llama 家底当前路径（Scan 组算，5a 之后的网关/后端组读）
    internal static bool haveLive = false;

    // 界面排布组的主窗体（原 RunBody 里 `using var form` 的局部量；二次切分要跨文件共享 → 提成字段，
    // 释放改由 UiLayoutChecks 的 try/finally 显式做，语义与 using 一致）
    internal static MainForm uiForm = null!;
    internal static List<string> rows = new();    // 主窗布局行（Row/InOrder 吃它）
    internal static List<string> srows = new();   // 设置页布局行（SRow 吃它）

    // 五个子窗体（原 5a 里各自的 `using var` 局部量）：二次切分要跨区共享 → 提成字段，
    // 释放统一挪进 UiLayoutChecks 的 finally（逆序，照 using 的次序；异常路径也用 null 检查兜住）
    internal static SettingsForm sf = null!;
    internal static UpdateForm uf = null!;
    internal static UpdateForm lf = null!;
    internal static UpdateForm nf = null!;
    internal static UpdateForm wf = null!;
    internal static ModelEntry? strataForTest = null;   // 5a 里显式选中的 Strata 模型（跨段共用）

    internal static StringBuilder sb = null!;
    internal static int failures = 0;
    internal static CheckFn Check = null!;
    internal static Action<string> W = null!;

    // ⓪ 护栏的跑前快照（preamble 填、CheckRunDirUntouched 读；DEC-138 缺口 9/11 一族）
    internal static (string? path, byte[] bytes) ninferIniBefore;
    internal static Dictionary<string, string> ninferBkBefore = new();
    internal static Dictionary<string, string> ninferExeBkBefore = new();
    internal static (string? path, byte[] bytes) llamaIniBefore;
    internal static Dictionary<string, string> llamaBkBefore = new();
    internal static Dictionary<string, string> realRunBefore = new();
    internal static string strataCfgBakBefore = "";
    internal static string strataSnapBefore = "";
    internal static string cfgPathBefore = "";
    internal static byte[]? cfgBytesBefore = null;
}
