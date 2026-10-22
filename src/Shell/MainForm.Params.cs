using System.Text;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 主窗「保存」与「什么算参数改动」的唯一一张表（DEC-073/074/092）：保存落盘、参数表、脏状态、INI 持久化。
// 2026-10-15 从 MainForm.Config.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 主窗「保存」：把界面上的参数修改落盘（2026-10-04，原「存为预设」改义，DEC-073） ----------------
    // 与设置页「保存」同一条路（CollectSettings → SaveConfig）：写 [Current] 与选中模型自己的那份；
    // 切页 / 关窗 / 设置页保存本来也会自动收一遍，这枚按钮把"保存"变成看得见、点得到的动作。
    // DEC-079：llama 页还多走一步 —— 界面上**真的与生效值不一样**的格子写进 llama_models.ini 的那一段
    //（先备份、只写这些键、留空 = 删键；一个格子都没变就一个字都不写）。
    /// <summary>主窗「保存」= 唯一把界面参数写进文件的动作（DEC-092 起这是硬口径）。
    /// 返回 false = 被校验拦下（上下文非法），调用方（关窗那条路）别把窗口关掉。</summary>
    bool SaveParameterEdits()
    {
        // DEC-091 第二条收口：端口那格若还带着没确认过的手改，先在这里问一次（失焦那条路通常已经问过 = 不再弹）。
        // 放在收集**之前**：CollectSettings 会把文本框直接读进 _config.Current，之后再比就永远"没改"了。
        SettleNinferPort("点「保存」");
        _config.Current = CollectSettings();
        // 写段前先校验：那一段/那一行 args 是引擎直接读的，写坏了服务起不来（[Current] 那些软件自己的值照旧落盘）
        // DEC-097 补的一条作用域：**只校验本次真会写的那一家**。主窗「保存」只写激活模块那一段
        //（`ActiveView.SaveSectionEdits()`），拿另一页的格子拦这一页的保存 = 假拒（本轮实测：
        //  Strata 页点「保存」被 Ninfer 那一格「pending-timeout-ms = 0」挡下，而这一击根本不写 Ninfer 的段）。
        bool onLlama = ActiveView.Key == LlamaKey, onNinfer = ActiveView.Key == NinferKey;
        if (onLlama)
        {
            var bad = LlamaCtxProblem(_config.Current.LlamaCtx);
            if (bad is not null)
            {
                LogL("上下文没写进去：" + bad);
                Toast.Show(this, "上下文不对，没写进配置文件：" + bad, Toast.Kind.Error);
                return false;   // 拦下来：按钮保持亮着，别让"没落盘"看起来像"已保存"
            }
        }
        // Ninfer 侧写段前也过一遍数值判据（口径同上面 llama 那一条：那行 args 是引擎直接读的，写坏了服务起不来）
        if (onNinfer && _config.Current.NinferModel.Length > 0)
        {
            // DEC-136（C-6）：这一族以前用**界面上那枚方块**选校验集合，而写段那一步（SaveNinferSectionEdits）
            // 用的是**段里写的 backend**（NinferBackendOf）= 两个家。方块切到 WIN、选中的段还是 WSL 那一件时：
            // 该校验的（pending-timeout-ms / 状态槽数 / 视觉…）一条没判，不该判的（主机缓存 / 分块预填充）反倒拦人。
            // 口径：段里写的 backend 说了算（与写段、扫描同一句）。
            var ninferBackend = NinferBackendOf(_config.Current, _config.Current.NinferModel);
            foreach (var f in UiFlagsFor(ninferBackend))
            {
                var ninferProblem = NinferIni.ValueProblem(f, NinferUiValue(f, _config.Current, ninferBackend), ninferBackend);
                if (ninferProblem is null) continue;
                // 值要一起报出来（DEC-082 那条口径：只报键名不算落点；本轮排障也证明"界面上那格到底是什么"必须看得见）
                LogN("这一格没写进去：" + f + " = 「" + NinferUiValue(f, _config.Current, ninferBackend) + "」 " + ninferProblem);
                Toast.Show(this, "「" + f + "」不对，没写进配置文件：" + ninferProblem, Toast.Kind.Error);
                return false;
            }
        }
        if (!SaveConfig(commitParams: true))
        {
            // DEC-137（D-3）：壳自己的 ini 没落盘 = 远端读的那一份还是旧的 → 按钮保持亮、不当"已保存"
            //（口径与下面段没写成那两条一致：写不下去就明说，别让"没落盘"看起来像"已保存"）。
            LogActive("界面参数没存进文件（上面一行写了原因）：改动仍在界面上，「保存」按钮保持亮着。");
            Toast.Show(this, "没存进文件，详见日志", Toast.Kind.Error);
            return false;
        }
        // 第 0 轮 DEC-094：本模块那一步「把与当前生效值真不一样的键写进家底」由模块表给
        //（Strata 那一行返回 Nothing + 空明细，llama 那一行才是写 llama_models.ini 的那一段）
        var (seg, segDetail) = ActiveView.SaveSectionEdits();
        if (seg == SegWrite.Failed) return false;   // 段没写成：按钮保持亮，别让"没落盘"看起来像"已保存"
        bool wroteSeg = seg == SegWrite.Wrote;
        MarkParamsSaved();      // 收口即新基线：「保存」按钮回灰（DEC-074）
        // DEC-110①：这一行走的是**激活模块那一页**的通道。以前用默认 launcher = 三页都显示，
        // 而"保存"只写当前页那一份（Strata 写 strata_models.ini 里该模型那段（DEC-210）、llama 写它自己那段、Ninfer 写它的 args），
        // 另两页根本没被碰过 —— 同 DEC-047 那条老账（壳自己写的话全用默认通道 = 等于没分流）。
        LogActive("界面参数已保存。");
        // 用户 2026-10-04 第 6 条：落点要把"改了什麼"说出来（日志那一行有全量明细，这里够短就带上）。
        // 太长时退回"改动 N 处，详见日志"——toast 的字宽上限只有 S(360)，堆一屏明细反而看不清。
        string toast = !wroteSeg ? "界面参数已保存"
            : segDetail.Length == 0 ? $"界面参数已保存（已写进 {ActiveView.HomeIniName} 那一段）"
            : segDetail.Length <= 48 ? $"界面参数已保存（已写进那一段：{segDetail}）"
            : $"界面参数已保存（已写进那一段：改动 {segDetail.Split('、').Length} 处，详见日志）";
        Toast.Show(this, toast, Toast.Kind.Ok);
        return true;
    }

    /// <summary>「保存」里 llama 那一步：把界面上与生效值不一样的格子写进选中模型那一段，
    /// 写完重新按文件校一遍界面与基线（继承标注也跟着刷新）。返回"有没有真的写进文件"；
    /// detail = 逐条改动明细（<c>top-k 20 → 15</c> 这种，给 toast 用，口径见 <see cref="LlamaChangeText"/>）。</summary>
    SegWrite SaveLlamaSectionEdits(out string detail)
    {
        detail = "";
        var tag = _config.Current.LlamaModel;
        if (tag.Length == 0)
        {
            LogL("还没选中 llama 模型，这些参数没处写（只存了软件自己的设置）。");
            return SegWrite.Failed;
        }
        var edits = LlamaSectionEdits(_config.Current, _llamaEffective);
        if (edits.Count == 0) return SegWrite.Nothing;          // 一个格子都没变 = 不碰文件、也不留无谓备份（继承不摊平）
        if (WriteLlamaSectionEdits(tag, edits, out var err, out detail) < 0)
        {
            LogL("写 llama_models.ini 失败：" + err);
            Toast.Show(this, "没写进配置文件：" + err, Toast.Kind.Error);
            detail = "";
            return SegWrite.Failed;
        }
        ApplyLlamaRecord(tag);   // 重读这一段：界面、继承标注与生效值基线都和文件对齐（按钮也在这一步回灰）
        return SegWrite.Wrote;
    }

    /// <summary>写段前唯一的数值校验：ctx-size 要么是空（= 删键、跟随全局），要么是正整数。
    /// 其余格子（temperature / top-p …）是引擎自己解析的标量，写坏了 llama-server 会在日志里报，
    /// 而且每次写之前都留了备份（设置页 llama 节能恢复），所以壳不替它们猜范围。</summary>
    internal static string? LlamaCtxProblem(string? ctx)
    {
        var v = (ctx ?? "").Trim();
        if (v.Length == 0) return null;
        if (!int.TryParse(v, out var n) || n < 1) return "必须是正整数（或留空 = 跟随全局）";
        return null;
    }

    // ---------------- 「什么算参数改动」的唯一一张表（DEC-092 第 2 条） ----------------
    // 用户口径：界面上改了参数、没点「保存」，切页回来还在（内存留痕这条保留），但不该已经落盘。
    // 原来切页 / 关窗 / 设置页保存 / 模式条都会顺手收一遍界面值再写文件，于是"没点保存也存住了"。
    // 这一张表把三件事收到一处，免得以后加格子只改一处：
    //   · ParamSignature() = 脏判定（「保存」按钮亮不亮）
    //   · ForDisk()        = 落盘时哪些格取"上次提交的值"（冻结表：未保存的改动搭不上别的落盘路径）
    //   · ParamDiff()      = 关窗那句「写进去 / 丢弃」要列的明细（口径同 DEC-082：只报键名不算落点）
    // Freeze=false 的四格不是"参数"：两枚模型选中项与两枚模式条开关 = 导航/即时开关，自己就是落点。
    // Home=LlamaSection 的格子家底在 llama_models.ini 的模型段里（DEC-079），明细按落点分两组报。

    internal enum ParamHome { Launcher, LlamaSection, NinferSection }
    // internal（第 0 轮 DEC-094）：模块表里那一行「保存时本模块要写家底吗」的返回类型，不能比 ModuleView 更严
    internal enum SegWrite { Nothing, Wrote, Failed }
    internal sealed record ParamField(string Name, string Label, ParamHome Home, Func<Settings, string> Get,
                             Action<Settings, string> Set, bool Freeze = true, Func<string, string>? Show = null);

    static string Bool01(bool v) => v ? "1" : "0";
    static readonly Func<string, string> BoolWord = v => v == "1" ? "开" : "关";
    static readonly Func<string, string> ModeWord = v => v == "1" ? "路由模式" : "单体模型";

    static readonly ParamField[] ParamFields =
    {
        // —— 卡1 ——
        new(nameof(Settings.Host), "监听地址", ParamHome.Launcher, s => s.Host, (s, v) => s.Host = v),
        new(nameof(Settings.Port), "监听端口", ParamHome.Launcher, s => s.Port, (s, v) => s.Port = v),
        new(nameof(Settings.LlamaInternalPort), "监听端口", ParamHome.Launcher, s => s.LlamaInternalPort, (s, v) => s.LlamaInternalPort = v),
        new(nameof(Settings.ReasoningEffort), "思考深度", ParamHome.Launcher, s => s.ReasoningEffort, (s, v) => s.ReasoningEffort = v),
        new(nameof(Settings.LlamaReasoningEffort), "思考深度", ParamHome.LlamaSection, s => s.LlamaReasoningEffort, (s, v) => s.LlamaReasoningEffort = v),
        // —— 卡2（模型名称｜上下文 ／ KV 精度｜附加参数：两页共用那四只格子，按页收进各自字段）——
        new(nameof(Settings.ApiModel), "模型名称", ParamHome.Launcher, s => s.ApiModel, (s, v) => s.ApiModel = v),
        new(nameof(Settings.Context), "上下文", ParamHome.Launcher, s => s.Context, (s, v) => s.Context = v),
        new(nameof(Settings.Kv), "KV 精度", ParamHome.Launcher, s => s.Kv, (s, v) => s.Kv = v),
        new(nameof(Settings.ExtraArgs), "附加参数", ParamHome.Launcher, s => s.ExtraArgs, (s, v) => s.ExtraArgs = v),
        new(nameof(Settings.LlamaCtx), "上下文", ParamHome.LlamaSection, s => s.LlamaCtx, (s, v) => s.LlamaCtx = v),
        new(nameof(Settings.LlamaKv), "KV 精度", ParamHome.LlamaSection, s => s.LlamaKv, (s, v) => s.LlamaKv = v),
        new(nameof(Settings.LlamaExtraArgs), "附加参数", ParamHome.Launcher, s => s.LlamaExtraArgs, (s, v) => s.LlamaExtraArgs = v),
        // —— 高级区：Strata 一套 ——
        new(nameof(Settings.Gpu), "指定显卡", ParamHome.Launcher, s => s.Gpu, (s, v) => s.Gpu = v),
        new(nameof(Settings.LayerSplit), "层拆分起点", ParamHome.Launcher, s => s.LayerSplit, (s, v) => s.LayerSplit = v),
        new(nameof(Settings.KvResident), "KV 驻留内存", ParamHome.Launcher, s => Bool01(s.KvResident), (s, v) => s.KvResident = v == "1", Show: BoolWord),
        new(nameof(Settings.Spec), "投机解码", ParamHome.Launcher, s => s.Spec, (s, v) => s.Spec = v),
        new(nameof(Settings.SpecMinP), "最低接受率", ParamHome.Launcher, s => s.SpecMinP, (s, v) => s.SpecMinP = v),
        new(nameof(Settings.ExpertCache), "expert-cache", ParamHome.Launcher, s => s.ExpertCache, (s, v) => s.ExpertCache = v),
        new(nameof(Settings.Prefill), "prefill", ParamHome.Launcher, s => s.Prefill, (s, v) => s.Prefill = v),
        new(nameof(Settings.Vision), "视觉", ParamHome.Launcher, s => s.Vision, (s, v) => s.Vision = v),
        new(nameof(Settings.VramReserveMib), "显存预留", ParamHome.Launcher, s => s.VramReserveMib, (s, v) => s.VramReserveMib = v),
        new(nameof(Settings.ArenaPinGib), "arena pin (GiB)", ParamHome.Launcher, s => s.ArenaPinGib, (s, v) => s.ArenaPinGib = v),
        new(nameof(Settings.HeadLate), "STRATA_HEAD_LATE", ParamHome.Launcher, s => Bool01(s.HeadLate), (s, v) => s.HeadLate = v == "1", Show: BoolWord),
        // —— 高级区：llama 一套（家底都在段里）——
        new(nameof(Settings.LlamaTemperature), "temperature", ParamHome.LlamaSection, s => s.LlamaTemperature, (s, v) => s.LlamaTemperature = v),
        new(nameof(Settings.LlamaTopP), "top-p", ParamHome.LlamaSection, s => s.LlamaTopP, (s, v) => s.LlamaTopP = v),
        new(nameof(Settings.LlamaTopK), "top-k", ParamHome.LlamaSection, s => s.LlamaTopK, (s, v) => s.LlamaTopK = v),
        new(nameof(Settings.LlamaMinP), "min-p", ParamHome.LlamaSection, s => s.LlamaMinP, (s, v) => s.LlamaMinP = v),
        new(nameof(Settings.LlamaPresencePenalty), "presence-penalty", ParamHome.LlamaSection, s => s.LlamaPresencePenalty, (s, v) => s.LlamaPresencePenalty = v),
        new(nameof(Settings.LlamaRepeatPenalty), "repeat-penalty", ParamHome.LlamaSection, s => s.LlamaRepeatPenalty, (s, v) => s.LlamaRepeatPenalty = v),
        new(nameof(Settings.LlamaSpecDraftNMax), "spec-draft-n-max", ParamHome.LlamaSection, s => s.LlamaSpecDraftNMax, (s, v) => s.LlamaSpecDraftNMax = v),
        new(nameof(Settings.LlamaFlashAttn), "flash-attn", ParamHome.LlamaSection, s => Bool01(s.LlamaFlashAttn), (s, v) => s.LlamaFlashAttn = v == "1", Show: BoolWord),
        // —— 高级区 + 卡1/卡2：Ninfer 一套（DEC-096；家底都在 ninfer_models.ini 那一段的 args 里）——
        new(nameof(Settings.NinferPort), "监听端口", ParamHome.Launcher, s => s.NinferPort, (s, v) => s.NinferPort = v),
        new(nameof(Settings.NinferEffort), "思考深度", ParamHome.NinferSection, s => s.NinferEffort, (s, v) => s.NinferEffort = v),
        new(nameof(Settings.NinferCtx), "上下文", ParamHome.NinferSection, s => s.NinferCtx, (s, v) => s.NinferCtx = v),
        new(nameof(Settings.NinferKvDtype), "KV 精度", ParamHome.NinferSection, s => s.NinferKvDtype, (s, v) => s.NinferKvDtype = v),
        new(nameof(Settings.NinferPool), "设备池", ParamHome.NinferSection, s => s.NinferPool, (s, v) => s.NinferPool = v),
        new(nameof(Settings.NinferPrefill), "分块预填充", ParamHome.NinferSection, s => s.NinferPrefill, (s, v) => s.NinferPrefill = v),
        new(nameof(Settings.NinferHostKib), "主机缓存", ParamHome.NinferSection, s => s.NinferHostKib, (s, v) => s.NinferHostKib = v),
        new(nameof(Settings.NinferSpec), "投机方式", ParamHome.NinferSection, s => s.NinferSpec, (s, v) => s.NinferSpec = v),
        new(nameof(Settings.NinferDraft), "投机深度", ParamHome.NinferSection, s => s.NinferDraft, (s, v) => s.NinferDraft = v),
        new(nameof(Settings.NinferExtraArgs), "附加参数", ParamHome.Launcher, s => s.NinferExtraArgs, (s, v) => s.NinferExtraArgs = v),
        new(nameof(Settings.NinferMaxTokens), "输出上限", ParamHome.NinferSection, s => s.NinferMaxTokens, (s, v) => s.NinferMaxTokens = v),
        new(nameof(Settings.NinferTemperature), "temperature", ParamHome.NinferSection, s => s.NinferTemperature, (s, v) => s.NinferTemperature = v),
        new(nameof(Settings.NinferTopP), "top-p", ParamHome.NinferSection, s => s.NinferTopP, (s, v) => s.NinferTopP = v),
        new(nameof(Settings.NinferTopK), "top-k", ParamHome.NinferSection, s => s.NinferTopK, (s, v) => s.NinferTopK = v),
        new(nameof(Settings.NinferPresence), "presence-penalty", ParamHome.NinferSection, s => s.NinferPresence, (s, v) => s.NinferPresence = v),
        new(nameof(Settings.NinferConcurrency), "并发数", ParamHome.NinferSection, s => s.NinferConcurrency, (s, v) => s.NinferConcurrency = v),
        new(nameof(Settings.NinferSlots), "状态槽数", ParamHome.NinferSection, s => s.NinferSlots, (s, v) => s.NinferSlots = v),
        new(nameof(Settings.NinferPending), "排队上限", ParamHome.NinferSection, s => s.NinferPending, (s, v) => s.NinferPending = v),
        new(nameof(Settings.NinferPendingSec), "排队超时（秒）", ParamHome.NinferSection, s => s.NinferPendingSec, (s, v) => s.NinferPendingSec = v),
        new(nameof(Settings.NinferLmHead), "草稿头模式", ParamHome.NinferSection, s => Bool01(s.NinferLmHead), (s, v) => s.NinferLmHead = v == "1", Show: BoolWord),
        new(nameof(Settings.NinferVision), "视觉", ParamHome.NinferSection, s => Bool01(s.NinferVision), (s, v) => s.NinferVision = v == "1", Show: BoolWord),
        new(nameof(Settings.NinferPreserve), "保留思考", ParamHome.NinferSection, s => Bool01(s.NinferPreserve), (s, v) => s.NinferPreserve = v == "1", Show: BoolWord),
        // —— 不是"参数改动"的四格（脏判定仍认，落盘不冻结）——
        new(nameof(Settings.Model), "模型选中", ParamHome.Launcher, s => s.Model, (s, v) => s.Model = v, Freeze: false),
        new(nameof(Settings.LlamaModel), "llama 段选中", ParamHome.LlamaSection, s => s.LlamaModel, (s, v) => s.LlamaModel = v, Freeze: false),
        new(nameof(Settings.NinferModel), "Ninfer 段选中", ParamHome.NinferSection, s => s.NinferModel, (s, v) => s.NinferModel = v, Freeze: false),
        new(nameof(Settings.WakeOnRequest), "Strata 启动模式", ParamHome.Launcher, s => Bool01(s.WakeOnRequest), (s, v) => s.WakeOnRequest = v == "1", Freeze: false, Show: ModeWord),
        new(nameof(Settings.LlamaRouteMode), "llama 启动模式", ParamHome.Launcher, s => Bool01(s.LlamaRouteMode), (s, v) => s.LlamaRouteMode = v == "1", Freeze: false, Show: ModeWord),
    };

    // 磁盘上那一份参数（= 上一次真正提交过的值）。收口点只动 _config.Current（内存），落盘时参数格取这里。
    Settings _savedParams = new();
    int _diskWrites;
    void MirrorSavedParams() => _savedParams = _config.Current.Clone();
    void CommitSavedParams()
    {
        foreach (var f in ParamFields)
            if (f.Freeze) f.Set(_savedParams, f.Get(_config.Current));
    }
    // 非提交式的落盘（更新链记跳过位、内部端口避让、关窗记窗口位置…）：先把冻结格按回上次提交的值再写
    Settings ForDisk(Settings live)
    {
        var s = live.Clone();
        foreach (var f in ParamFields)
            if (f.Freeze) f.Set(s, f.Get(_savedParams));
        return s;
    }
    // 关窗明细：按落点分组报「标签 旧 → 新」（空值报"留空"，别留个空格子让人猜）
    List<(string Label, string From, string To)> ParamDiff(ParamHome home)
    {
        var cur = CollectSettings();
        var outp = new List<(string, string, string)>();
        foreach (var f in ParamFields)
        {
            if (!f.Freeze || f.Home != home) continue;
            string a = f.Get(_savedParams), c = f.Get(cur);
            if (a == c) continue;
            string Text(string v) => v.Length == 0 ? "留空" : (f.Show is not null ? f.Show(v) : v);
            outp.Add((f.Label, Text(a), Text(c)));
        }
        return outp;
    }

    // 自检出口：明细文案与"磁盘那份"的当前值（DEC-092 用它断"切页不落盘 / 保存才落盘"）
    internal string ParamDiffForTest(ParamHome home) =>
        string.Join("、", ParamDiff(home).Select(d => d.Label + " " + d.From + " → " + d.To));
    internal int DiskWritesForTest => _diskWrites;
    internal bool PendingParamEditsForTest => ParamSignature() != _paramBaseline;
    // 自检出口：走真路径的两个动作 + 直接读回文件里那一格（DEC-092「保存才落盘」的判据必须打在文件上）
    internal void SaveWithoutCommitForTest() => SaveConfig(commitParams: false);
    internal bool SaveParameterEditsForTest() => SaveParameterEdits();
    internal string CurrentValueInIniForTest(string key) => ValueInIniForTest(key);
    /// <summary>读 ini 文件里某一段的某一个键（DEC-122③ 起要能读 [Model:&lt;tag&gt;] 那一段 ——
    /// 那一族键的家已经搬过去了，"保存才落盘"的判据必须跟着打在真正的落点上，不然就成了
    /// 盯着一个永远不会再出现的行、永远红或永远空过）。</summary>
    internal string ValueInIniForTest(string key, string section = "Current")
    {
        // A/C（2026-10-22，DEC-206/207/210）：模型参数的家已从 ini 的 `[Model:<tag>]` 段搬到
        // `strata_models.ini` 里同名的那一段。所有"保存才落盘"的判据读的都是这一段，所以这里按段名转一次路 ——
        // 判据照旧打在**真正的落点**上，而不是盯着一个永远不会再出现的行（那等于把断言悄悄变成空跑，BUG-043 那一族）。
        if (section.StartsWith(Config.ModelSnapPrefix, StringComparison.Ordinal))
        {
            var path = ModelParamsPath();
            if (!File.Exists(path)) return "<没有这一行>";
            try
            {
                var sec = ModelParamsIni.Parse(File.ReadAllText(path)).Find(Config.ModelSnapTag(section));
                if (sec is null || !ModelParamsIni.TryGet(sec, key, out var raw)) return "<没有这一行>";
                var prop = typeof(Settings).GetProperty(key);
                if (prop is null) return "<没有这一行>";
                if (prop.PropertyType == typeof(bool)) return TryParseIniBool(raw, out var b) && b ? "True" : "False";
                return raw;
            }
            catch { return "<没有这一行>"; }
        }
        var want = "[" + section + "]";
        bool inWanted = false;
        foreach (var line in File.ReadLines(ConfigPath))
        {
            var t = line.Trim();
            if (t.StartsWith("[")) { inWanted = t == want; continue; }
            if (!inWanted) continue;
            var eq = t.IndexOf('=');
            if (eq > 0 && t[..eq].Trim() == key) return t[(eq + 1)..].Trim();
        }
        return "<没有这一行>";
    }

    internal void SwitchModuleRealForTest(string key) => SwitchModule(key);

    // ---------------- 主窗「保存」按钮的脏状态（2026-10-04 DEC-074；2026-10-06 DEC-092 收紧） ----------------
    // 用户选定口径「只认手动改动」：界面上手动改过任何主窗参数 → 按钮亮。
    // DEC-092 起判据更硬：亮 = 界面与**磁盘上那一份**不一致 —— 切页不再算收口（见 SwitchModule），
    // 只有「保存」与关窗选「写进去」把它按回灰；程序化推视图（启动 / 换模型 / 设置页保存）仍走 MarkParamsSaved。
    // 签名逐格取自上面那张表；设置页专有字段不在表里（改它们不该点亮主窗这枚按钮）。
    string _paramBaseline = "";

    string ParamSignature()
    {
        var s = CollectSettings();
        var sb = new StringBuilder();
        foreach (var f in ParamFields) sb.Append(f.Get(s)).Append('\u0001');
        return sb.ToString();
    }

    // 手动改动事件都挂到它上面（WireEvents 里一次性接线）；程序化推视图不挂，它们各自走 MarkParamsSaved
    void RefreshSaveButton()
    {
        bool dirty = ParamSignature() != _paramBaseline;
        if (_btnSave.Enabled != dirty)
        {
            _btnSave.Enabled = dirty;
            _btnSave.Invalidate();      // UserPaint 控件 Enabled 变化不保证重绘（DEC-020 的教训）
        }
    }

    // 收口点：把当前界面态记为「已保存」基线（ApplySettings（启动 / 换模型 / 设置页保存）/ 保存 / 模式条）。
    // DEC-092：切页与关窗**不再**经过这里 —— 它们不落盘，也就没资格把按钮按回灰。
    void MarkParamsSaved()
    {
        _paramBaseline = ParamSignature();
        RefreshSaveButton();
    }

    // ---------------- INI 持久化 ----------------

    // 「模型家底」= 换模型时该跟着变的那批字段（`ReadKnobsFromModel` 写的那批 + 界面独有的附加参数）。
    // 端口 / 监听地址 / 启动模式 / 内部端口 / 空闲停服 / 自动启动归"服务怎么跑"，不跟着模型走，所以不在这里。
    internal static readonly string[] ModelOwnedKeys =
    {
        nameof(Settings.ApiModel), nameof(Settings.Context), nameof(Settings.Kv), nameof(Settings.KvResident),
        nameof(Settings.Spec), nameof(Settings.SpecMinP), nameof(Settings.ExpertCache), nameof(Settings.Prefill),
        nameof(Settings.Vision), nameof(Settings.VramReserveMib),
        nameof(Settings.Gpu), nameof(Settings.LayerSplit), nameof(Settings.ArenaPinGib), nameof(Settings.HeadLate),
        nameof(Settings.ExtraArgs), nameof(Settings.ReasoningEffort),
    };

    static readonly System.Reflection.PropertyInfo[] ModelOwnedProps =
        ModelOwnedKeys.Select(k => typeof(Settings).GetProperty(k)!).ToArray();

    // 只搬模型家底，其余字段一律不碰（端口、模式、另一模块的值都留在原处）
    internal static void ApplyModelFields(Settings from, Settings to)
    {
        foreach (var p in ModelOwnedProps) p.SetValue(to, p.GetValue(from));
    }

    /// <summary>DEC-155：Strata 页"卡面 vs 家底"真不一样的格子数（0 = 没有未保存改动）。
    /// 家底那份 = <see cref="BuildWakeSettings"/>（模板 ⊕ `[Model:&lt;tag&gt;]` 记录 = 唤醒取参同一个家），
    /// 所以这一条只比"换模型会重载的那批键"，跟模型前用它拦"别顶掉本页草稿"。</summary>
    internal int StrataPendingEditCount(Settings cur)
    {
        if (cur.Model.Length == 0) return 0;
        var home = BuildWakeSettings(cur.Model);
        int n = 0;
        foreach (var p in ModelOwnedProps)
            if (!Equals(p.GetValue(cur), p.GetValue(home))) n++;
        return n;
    }

    /// <summary>DEC-135（审查 A-1）：模型家底那 16 格的**打底**只有一个含义 = 「出厂默认」，
    /// 不是"上一件此刻在界面上/在冻结表里的那份"。谁都没写过这一件时（SETUP 新加的一件还没记录、
    /// 或模板里根本没有那枚旗标），带上上一件的格子就是**跨件串参数**：
    /// · `ExtraArgs` 会被原样拼进引擎命令行末尾（`BuildEffectiveConfig` 末尾那一句）；
    /// · `Spec`/`SpecMinP`/`ExpertCache`/`Prefill` 是无条件写 + 非空校验，带错 = 静默改行为、留空 = 一句他从没碰过的「--spec 必须是整数」；
    /// · 更坏的一支：界面上带着幽灵值点一次「保存」，`ModelSnaps[tag] = SnapshotModelFields(cur)` 就把它**永久写进这一件的记录**。
    /// 先例 = `ReasoningEffort` 那一格早在 DEC-064 就被单独归零（同一个坑，当时只补了一格，这一族还剩 8 格没人管）。</summary>
    internal static void ResetModelFieldsToDefaults(Settings to) => ApplyModelFields(new Settings(), to);

    internal static Settings SnapshotModelFields(Settings from)
    {
        var to = new Settings();
        ApplyModelFields(from, to);
        return to;
    }

}
