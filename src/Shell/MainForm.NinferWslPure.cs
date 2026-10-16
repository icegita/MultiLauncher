using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// Ninfer WSL 侧的纯函数族：脚本构造与输出解析（自检直接喂样本，不联网、不碰真件）。
// 2026-10-15 从 MainForm.UpdateNinfer.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 纯函数（自检直接喂样本，不联网、不碰真件） ----------------

    /// <summary>JSON 里取一个字符串；数值型（size 有时是数字）取它的字面。读不动一律空串 = "上游没给这条"（DEC-067 口径）。</summary>
    static string Str(JsonNode? n)
    {
        if (n is null) return "";
        try { return n.GetValue<string>(); } catch { return n.ToString(); }
    }

    // ============================================================================
    // WSL 那条更新链（DEC-106：git fetch/ff-only + cmake 重编 + 快照留 5 份 + 免重编回退）
    // ============================================================================
    // 形状 = 旧壳（旧壳工程\MainForm.Update.cs）整体搬运，只换三处：
    //   · 仓库根**从设置里那格 NinferWslBin 反推**（git rev-parse --show-toplevel），不再写死 /home/user/ninfer-4090-yarn；
    //   · 脚本走**登录壳** `bash -l -s` 从 stdin 喂（旧壳 `bash -lc` 的环境 + stdin 的免引号）；
    //   · 界面上这一段保持「一行版本 + 一枚「更新…」」（DEC-097③ / 本轮批复）：按钮字态跑全流程
    //     （更新… → 检查中… → 重编中…），检查、结果、进度全在日志与 toast 上落；回退入口住设置页 WSL 组。
    // 忙态与 WIN 链**共用** `_ninferUpdating`：两条链互斥（一模块一窗，重编还要停引擎，别叠着跑）。
    // 快照根沿用旧壳那个（$HOME/.ninfer-launcher/snapshots）—— 他手工留下的老快照继续可选，别换根（DEC-084 先例）。

    /// <summary>VM 里那份快照根（旧壳同名常量；回退清单与"留 5 份"都按它）。</summary>
    internal const string NinferWslSnapshotRoot = "$HOME/.ninfer-launcher/snapshots";

    string _ninferWslPhase = "";            // 忙态字（"检查中…"/"重编中…"；空 = 闲）—— 第二段那枚按钮的字跟着它走
    // DEC-110③：WSL 窗的结果回显。以前这条链的结论只写日志 + toast，而另三扇窗（Strata / llama / Ninfer-WIN）
    // 都有 memo 那一行 —— 用户抓的"已是最新只出现在日志区、窗里看不见"就是这一处缺。
    string _ninferWslMemo = "";
    System.Drawing.Color _ninferWslMemoColor = UiTheme.TextMuted;
    internal (string Memo, System.Drawing.Color Color) NinferWslStatusMemo() => (_ninferWslMemo, _ninferWslMemoColor);
    internal void SetNinferWslMemoForTest(string text, bool danger)
    {
        _ninferWslMemo = text;
        _ninferWslMemoColor = danger ? System.Drawing.Color.Firebrick : System.Drawing.Color.Green;
        PokeUpdateUi();
    }
    /// <summary>把一条结果写进 WSL 窗那一行（同另三扇的 SetUpdateInfo / SetLlamaInfo / SetNinferInfo 一个形状）。</summary>
    void SetNinferWslInfo(string text, System.Drawing.Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetNinferWslInfo(text, color))); } catch { } return; }
        _ninferWslMemo = text; _ninferWslMemoColor = color;
        PokeUpdateUi();
    }
    string _ninferWslBlockedForTest = "";   // 干跑证人位：入口被哪一条判据挡下（"test"/"disabled"/"busy"/""）
    int _ninferWslAttemptsForTest;          // 干跑证人位：入口被调用过几次

    internal string NinferWslButtonText() => _ninferWslPhase.Length > 0 ? _ninferWslPhase : "更新…";
    internal string NinferWslButtonTextForTest => NinferWslButtonText();
    internal string NinferWslBlockedForTest => _ninferWslBlockedForTest;
    internal int NinferWslAttemptsForTest => _ninferWslAttemptsForTest;
    internal void ResetNinferWslWitnessForTest() { _ninferWslAttemptsForTest = 0; _ninferWslBlockedForTest = ""; }

    /// <summary>忙态一把抓：按钮字（第二段那枚）+ 第三行的阶段名（都走 UI 线程调；阶段名要说得清"在忙哪条链"）。</summary>
    void SetNinferWslBusy(string button, string phase)
    {
        _ninferWslPhase = button; _ninferPhase = phase; _ninferFrac = -1;
        // DEC-138（B-6）：开新一轮先把上一轮的结果回显清掉。那一行**闲时**才归 memo、**忙时**归阶段名，
        // 上一轮那句"检查失败"留在原地就会与这一轮的 fetch 并存一瞬（两句话挤在同一行里，看着像新结果）。
        // 只在进入忙态时清（button 非空）—— 忙态收尾那次调用（button = ""）不许把刚写进去的结果擦掉。
        if (button.Length > 0) { _ninferWslMemo = ""; _ninferWslMemoColor = UiTheme.TextMuted; }
        PokeUpdateUi();
    }
    internal void SetNinferWslBusyForTest(string button, string phase) => SetNinferWslBusy(button, phase);

    /// <summary>这条链能不能开跑（纯判据、一处收口）：测试态（自检/出图）绝不允许、那一侧要启用、两条链互斥。</summary>
    internal static bool NinferWslChainAllowed(bool testDisabled, bool sideEnabled, bool busy)
        => !testDisabled && sideEnabled && !busy;

    /// <summary>分支名形状（要拼进脚本的，先过这一关；不许 `-` 开头 = 防被当旗标）。</summary>
    internal static bool NinferWslBranchOk(string branch) =>
        branch.Length > 0 && branch[0] != '-' && Regex.IsMatch(branch, @"^[A-Za-z0-9._/-]+$");

    /// <summary>DEC-110④：更新源那一格的形状关。留空 = 合法（走仓库自己的 origin，旧行为一字不变）；
    /// 填了就必须是 https / ssh / git@ 三种地址之一，且不带任何 shell 元字符 —— 这条链整个是拼进 sh 脚本跑的，
    /// 注入串进不了脚本靠的就是这一关（同分支名与提交号那条规矩）。</summary>
    internal static bool NinferWslUrlOk(string url)
    {
        var u = (url ?? "").Trim();
        if (u.Length == 0) return true;
        if (!(u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
              || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
              || Regex.IsMatch(u, @"^git@[^/]+:.+$"))) return false;
        return !Regex.IsMatch(u, @"[\s'`$;|&<>()\[\]{}*?!#~""\\]");
    }

    /// <summary>取上游那两行：填了更新源就**显式从那一头**取指定分支（写进 FETCH_HEAD，不改他仓库的 remote 配置）；
    /// 留空照旧 `git fetch origin`。</summary>
    static string WslFetchLines(string url) =>
        url.Length > 0
            ? "git fetch " + BashQuote(url) + " \"$BR\" 2>&1 || { echo FETCH_FAIL; exit 5; }\n"
            : "git fetch origin 2>&1 || { echo FETCH_FAIL; exit 5; }\n";

    /// <summary>与 <see cref="WslFetchLines"/> 成对的那一头叫什么：显式取来的是 FETCH_HEAD，走 origin 的是跟踪引用。</summary>
    static string WslRemoteRef(string url) => url.Length > 0 ? "FETCH_HEAD" : "\"origin/$BR\"";

    /// <summary>提交号形状（短/全长都收；拼进脚本前一律过这一关 —— 回退清单里的名字也走它）。</summary>
    internal static bool NinferWslHexOk(string hex, int min = 4) =>
        hex.Length >= min && hex.Length <= 40 && Regex.IsMatch(hex, "^[0-9a-fA-F]+$");

    /// <summary>长输出只留尾巴（日志给个现场就够了，别把整段 cmake 日志灌进去）。</summary>
    internal static string TailText(string s, int max) { s = (s ?? "").TrimEnd(); return s.Length <= max ? s : "…" + s[^max..]; }

    // ---------------- 脚本（纯构造；自检断"该有的都在、别处不该有"） ----------------

    /// <summary>每段脚本的开场：从 exe 那格反推仓库根（git 自己往上找 .git，壳不猜仓库在哪）。
    /// 找不到 = 输出 NOREPO 并退出 4（调用方有专门的说法）。</summary>
    static string WslChainPreamble(string bin) =>
        "B=" + BashQuote(bin) + "\n"
        + "R=$(git -C \"$(dirname \"$B\")\" rev-parse --show-toplevel 2>/dev/null)\n"
        + "[ -n \"$R\" ] || { echo NOREPO; exit 4; }\n"
        + "cd \"$R\" || exit 4\n";

    /// <summary>检查：先认分支（origin/HEAD 认不出就 main，纯本地动作）→ fetch（失败 5）→ 报两侧提交。
    /// DEC-110④：更新源那一格非空时，fetch 与"远端那一头"都改走 FETCH_HEAD（见 WslFetchLines / WslRemoteRef）。</summary>
    internal static string NinferWslCheckScript(string bin, string url) =>
        WslChainPreamble(bin)
        + "BR=$(git symbolic-ref --short refs/remotes/origin/HEAD 2>/dev/null)\n"
        + "BR=${BR#origin/}\n"
        + "[ -n \"$BR\" ] || BR=main\n"
        + "echo \"BRANCH=$BR\"\n"
        + WslFetchLines(url)
        + "echo \"LOCAL=$(git rev-parse HEAD 2>/dev/null)\"\n"
        + "echo \"REMOTE=$(git rev-parse " + WslRemoteRef(url) + " 2>/dev/null)\"\n";

    /// <summary>更新内容 = 本地到远端的提交清单（`%h %B`：短号 + 标题正文）。三枚入参先过形状关。
    /// DEC-110④：走更新源这一头时这里要**自己 fetch 一次**（上一次检查取的 FETCH_HEAD 早就不作数了）。</summary>
    internal static string NinferWslLogScript(string bin, string url, string branch, string localHex) =>
        NinferWslBranchOk(branch) && NinferWslHexOk(localHex, 7) && NinferWslUrlOk(url)
            ? WslChainPreamble(bin)
              + (url.Length > 0 ? WslFetchLines(url) : "")
              + "git log --pretty=format:'%h %B' " + localHex + ".." + WslRemoteRef(url) + " 2>&1\n"
            : "echo BADREF; exit 13\n";

    /// <summary>同步上游：脏树不构建（6）→ `checkout 分支`（7）→ `merge --ff-only`（8，绝不产生本地提交）。
    /// DEC-110④：走更新源这一头时同步前补一次 fetch（那一头的提交得现取）；走 origin 的旧路一字未动。</summary>
    internal static string NinferWslSyncScript(string bin, string url, string branch) =>
        NinferWslBranchOk(branch) && NinferWslUrlOk(url)
            ? WslChainPreamble(bin)
              + "D=$(git status --porcelain)\n"
              + "[ -z \"$D\" ] || { echo \"$D\"; echo DIRTY; exit 6; }\n"
              + "git checkout " + BashQuote(branch) + " 2>&1 || { echo CHECKOUT_FAIL; exit 7; }\n"
              + (url.Length > 0 ? WslFetchLines(url) : "")
              + "git merge --ff-only " + (url.Length > 0 ? "FETCH_HEAD" : BashQuote("origin/" + branch))
              + " 2>&1 || { echo MERGE_FAIL; exit 8; }\n"
              + "echo \"HEAD=$(git rev-parse --short HEAD)\"\n"
            : "echo BADBRANCH; exit 13\n";

    /// <summary>重编：缺 CMakeCache 先 configure（9）→ 增量编 ninfer 与 ninfer-serve（10）。配置与目标名照旧壳。</summary>
    internal static string NinferWslBuildScript(string bin) =>
        WslChainPreamble(bin)
        + "if [ ! -f build/CMakeCache.txt ]; then\n"
        + "  echo CONFIGURE\n"
        + "  cmake -S . -B build -DCMAKE_BUILD_TYPE=Release -DNINFER_BUILD_APPS=ON -DBUILD_TESTING=OFF -DNINFER_BUILD_BENCHMARKS=OFF 2>&1 || { echo CONFIGURE_FAIL; exit 9; }\n"
        + "fi\n"
        + "cmake --build build -j --target ninfer ninfer-serve 2>&1 || { echo BUILD_FAIL; exit 10; }\n"
        + "echo \"HEAD=$(git rev-parse --short HEAD)\"\n";

    /// <summary>快照：把刚编好的 ninfer-serve 按当前短提交存一份，按 mtime 留最近 <b>3</b> 份（DEC-110⑤：原 5 份）。
    /// 旧壳同款，只是 `tail -n +4`（= 第 4 份起删）。</summary>
    internal static string NinferWslSnapshotScript(string bin) =>
        WslChainPreamble(bin)
        + "set -e\n"
        + "c=$(git rev-parse --short HEAD)\n"
        + "d=\"" + NinferWslSnapshotRoot + "/$c\"\n"
        + "mkdir -p \"$d\"\n"
        + "cp -f build/apps/ninfer-serve \"$d/\"\n"
        + "ls -1dt " + NinferWslSnapshotRoot + "/*/ 2>/dev/null | tail -n +" + (NinferBackupKeep + 1)
        + " | tr '\n' '\0' | xargs -0 -r rm -rf --\n"      // DEC-136：①「留几份」由 NinferBackupKeep 生成（以前那个 4 是第二处家）
                                                           //          ②名字带空格的老快照会被 xargs 按空白拆开，而脚本开头 cd 在
                                                           //            用户仓库根 = 删到人家仓库里的同名目录（tr + xargs -0 收掉）
        + "echo \"SNAP=$c\"\n";

    /// <summary>快照清单（最新在前；调用方保持这个原序）。</summary>
    internal static string NinferWslSnapshotsListScript() =>
        "ls -1dt " + NinferWslSnapshotRoot + "/*/ 2>/dev/null\n";

    /// <summary>免重编回退：先确认快照在（12）→ `checkout 提交`（7）→ 把快照二进制拷回去（11）。</summary>
    internal static string NinferWslRollbackScript(string bin, string commit) =>
        NinferWslHexOk(commit)
            ? WslChainPreamble(bin)
              + "[ -f \"" + NinferWslSnapshotRoot + "/" + commit + "/ninfer-serve\" ] || { echo NOSNAP; exit 12; }\n"
              + "git checkout " + commit + " 2>&1 || { echo CHECKOUT_FAIL; exit 7; }\n"
              + "cp -f \"" + NinferWslSnapshotRoot + "/" + commit + "/ninfer-serve\" build/apps/ninfer-serve 2>&1 || { echo CP_FAIL; exit 11; }\n"
              + "echo \"HEAD=$(git rev-parse --short HEAD)\"\n"
            : "echo BADCOMMIT; exit 13\n";

    // ---------------- 解析（纯函数，自检直接喂样本） ----------------

    internal sealed record NinferWslCheck(string Branch, string Local, string Remote, string? Problem);

    /// <summary>检查脚本的输出解析。形状关（分支/提交号）在这里做掉 —— 后面几段脚本拿到的值都出自这里，
    /// 形状不对就当场按"读不动"处理，绝不把不认识的串拼进下一段脚本。</summary>
    internal static NinferWslCheck ParseNinferWslCheck(string output)
    {
        string br = "", local = "", remote = ""; string? problem = null;
        foreach (var raw in (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw == "NOREPO") { problem ??= "VM 里那个目录不是 git 检出（git 找不到仓库根）"; continue; }
            if (raw == "FETCH_FAIL") { problem ??= "git fetch 失败（网络不通？）"; continue; }
            var eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            var k = raw[..eq];
            var v = raw[(eq + 1)..].Trim();
            if (k == "BRANCH") br = v; else if (k == "LOCAL") local = v; else if (k == "REMOTE") remote = v;
        }
        if (problem is not null) return new("", "", "", problem);
        if (br.Length == 0 || local.Length == 0 || remote.Length == 0) return new("", "", "", "读不全分支/提交（脚本没跑成？）");
        if (!NinferWslBranchOk(br)) return new("", "", "", "分支名形状不认识，不拼进脚本：" + br);
        if (!NinferWslHexOk(local, 7) || !NinferWslHexOk(remote, 7)) return new("", "", "", "提交号形状不认识，不拼进脚本");
        return new(br, local, remote, null);
    }

    /// <summary>检查结果三态（纯判据）：两个都给得齐才比。</summary>
    internal static string NinferWslCheckVerdict(string local, string remote) =>
        local.Length == 0 || remote.Length == 0 ? "unknown"
        : string.Equals(local, remote, StringComparison.OrdinalIgnoreCase) ? "uptodate" : "update";

    /// <summary>快照清单解析：`ls -1dt` 的目录名只留短提交（别的行一律丢，保持"最新在前"的原序）。</summary>
    internal static List<string> ParseNinferWslSnapshotList(string output)
    {
        var list = new List<string>();
        foreach (var raw in (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = raw.TrimEnd('/');
            var slash = name.LastIndexOf('/');
            if (slash >= 0) name = name[(slash + 1)..];
            if (NinferWslHexOk(name)) list.Add(name);
        }
        return list;
    }

    /// <summary>提交短号（7 位；比 sha 的 16 短，因为这是给人对提交用的）。</summary>
    internal static string NinferWslShort(string hex) => hex.Length <= 7 ? hex : hex[..7];

}
