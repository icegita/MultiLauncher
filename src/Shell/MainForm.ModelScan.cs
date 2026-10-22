using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 模型扫描（两个后端各扫各的目录、估算大小、就绪判定）。2026-10-15 从 MainForm.Models.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 扫描 ----------------

    internal static List<ModelEntry> ScanModels(string root)
    {
        var list = new List<ModelEntry>();
        if (!Directory.Exists(root)) return list;
        foreach (var file in Directory.EnumerateFiles(root, "strata-*.json"))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".shared-settings.json", StringComparison.OrdinalIgnoreCase)) continue;
            var tag = name["strata-".Length..^".json".Length];
            string modelName = tag, note = "";
            bool ready = true;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                var argv = (node?["args"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? new List<string>();
                modelName = node?["model_name"]?.GetValue<string>() ?? tag;
                string? Arg(string flag)
                {
                    var i = argv.IndexOf(flag);
                    return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
                }
                var pack = Arg("--pack");
                var native = Arg("--native");
                var mtp = Arg("--mtp");
                if (pack is null || native is null) { ready = false; note = "配置里缺 --pack/--native"; }
                else if (!Directory.Exists(pack)) { ready = false; note = "pack 目录不存在"; }
                else if (!File.Exists(Path.Combine(pack, "tokenizer", "vocab.json"))) { ready = false; note = "pack 还没生成 tokenizer"; }
                else if (!File.Exists(native)) { ready = false; note = "GGUF 模型文件不存在"; }
                else if (mtp is not null && !File.Exists(Path.Combine(mtp, "experts.bin"))) { ready = false; note = "MTP 草稿层不完整"; }
            }
            catch (Exception ex) { ready = false; note = "配置读取失败: " + ex.Message; }
            list.Add(new ModelEntry(tag, file, modelName, ready, note) { SizeText = ModelSize.EstimateStrata(file) });
        }
        return list;
    }

    // llama.cpp 侧的模型列表：只读解析 llama_models.ini 的 section（后端拉起在 MainForm.Llama.cs）
    internal static List<ModelEntry> ScanLlamaModels(string iniPath)
    {
        var list = new List<ModelEntry>();
        foreach (var m in LlamaIni.ReadModels(iniPath))
        {
            bool ready = m.GgufPath.Length > 0 && File.Exists(m.GgufPath);
            var note = m.GgufPath.Length == 0 ? "ini 的该段没有 model = ..." : ready ? "" : "GGUF 文件不存在";
            list.Add(new ModelEntry(m.Name, m.GgufPath, m.Name, ready, note)
            {
                Backend = ModelEntry.BackendLlama,
                SizeText = ModelSize.EstimateLlama(m),
            });
        }
        return list;
    }

    // 模型数据目录：Strata 目录下的 Strata-data，或与 Strata 目录同级的 Strata-data。
    internal static string FindDataDir(string root)
    {
        var a = Path.Combine(root, "Strata-data");
        if (Directory.Exists(a)) return a;
        var parent = Directory.GetParent(root.TrimEnd('\\', '/'));
        if (parent is not null)
        {
            var b = Path.Combine(parent.FullName, "Strata-data");
            if (Directory.Exists(b)) return b;
        }
        return "";
    }

    // 重扫模型目录 + 解析 llama.cpp ini，并刷新下拉、提示行、高级区。
    // 允许的调用点就这几处（别再加，尤其别加在下拉展开或服务启停/网关启停上 —— 那会白扫一遍还刷一屏日志）：
    //   · 外壳启动（MainForm 构造）
    //   · SETUP 窗口关闭（RunSetup 挂的 Exited）、升级完成（MainForm.Update.cs）
    //   · 设置页改了 Strata 目录 / llama_models.ini / 模块开关（Program.cs）
    // 2026-10-02（DEC-033）：服务启停不再重扫——那两个时点模型列表不会变，扫了只有噪音。
    void RefreshModels() => RefreshModels(null);

    /// <summary>DEC-097⑤：<c>onlyKey</c> = 只有那一个模块的家底变了（Ninfer 切后端 / 某一侧恢复备份 / 某一侧升级完成）。
    /// 不传 = 全量重扫（启动、SETUP、Strata 升级那种确实会一次动到多家的时点）。
    /// 为什么要分：以前 Ninfer 页点一下后端方块走的是全量重扫，于是 Strata 与 llama 各多一行扫描日志
    /// —— 分流规则没错（标了模块的行只在本模块页显示），但那两行是**替 Ninfer 的动作记到别人账上**，
    /// 切到那两页会凭空多出没做过的操作，<c>logs\strata.log</c> / <c>logs\llama.log</c> 也一样被污染。</summary>
    void RefreshModels(string? onlyKey)
    {
        var root = _config.Current.Root;
        // 模块开关（B 段地基，DEC-024）：关闭 = 全隐身——不扫描它的模型（下拉 / 网关列表随之剔除）。
        bool strataOn = ModuleRegistry.StrataEnabled(_config.Current);
        bool llamaOn = ModuleRegistry.LlamaEnabled(_config.Current);
        bool ninferOn = ModuleRegistry.NinferEnabled(_config.Current);
        bool wantStrata = onlyKey is null || onlyKey == ModuleRegistry.Strata.Key;
        bool wantLlama = onlyKey is null || onlyKey == ModuleRegistry.Llama.Key;
        bool wantNinfer = onlyKey is null || onlyKey == ModuleRegistry.Ninfer.Key;
        if (wantStrata) _models = strataOn ? ScanModels(root) : new List<ModelEntry>();
        // llama.cpp 侧：解析家底那段 ini 出列表与估算（DEC-079 起这一份也能就地写；拉起在 MainForm.Llama.cs）
        if (wantLlama) _llamaModels = llamaOn ? ScanLlamaModels(RouterIniPath()) : new List<ModelEntry>();
        // Ninfer 侧（DEC-096）：段就是事实，两个后端各扫各的（关掉那一侧连扫都不扫）
        if (wantNinfer) _ninferModels = ninferOn ? ScanNinferModels() : new List<ModelEntry>();
        EnsureActiveModuleValid();

        // 停用的模块连扫描日志都不打（全隐身）；本轮没重扫的模块也不打（DEC-097⑤）
        if (strataOn && wantStrata)
        {
            LogS($"已扫描 {_models.Count} 个 Strata 模型配置 ({root})");
            foreach (var m in _models.Where(m => !m.Ready))
                LogS($"  模型 {m.Tag} 未就绪：{m.Note}");
        }
        if (llamaOn && wantLlama)
        {
            LogL($"已解析 {_llamaModels.Count} 个 llama.cpp 模型（{RouterIniPath()}）");
            foreach (var m in _llamaModels.Where(m => !m.Ready))
                LogL($"  llama.cpp 模型 {m.Tag} 不可用：{m.Note}");
        }
        if (ninferOn && wantNinfer)
        {
            LogN($"已解析 {_ninferModels.Count} 个 Ninfer 模型（{NinferIniPath()}，当前后端 " +
                 ActiveNinferBackend(_config.Current) + "）");
            foreach (var m in _ninferModels.Where(m => !m.Ready))
                LogN($"  Ninfer 模型 {m.Tag} 不可用：{m.Note}");
        }
        else if (!ninferOn && _ninferModels.Count > 0) _ninferModels = new List<ModelEntry>();   // 模块停用 = 全隐身

        var data = strataOn && wantStrata ? FindDataDir(root) : "";
        var unconfigured = new List<string>();
        var modelsDir = data.Length > 0 ? Path.Combine(data, "models") : "";
        if (modelsDir.Length > 0 && Directory.Exists(modelsDir))
        {
            foreach (var d in Directory.GetDirectories(modelsDir))
            {
                var n = Path.GetFileName(d);
                if (_models.Any(m => string.Equals(m.Tag, n, StringComparison.OrdinalIgnoreCase))) continue;
                if (Directory.EnumerateFiles(d, "*.gguf").Any()) unconfigured.Add(n);
            }
        }
        // DEC-097①：Ninfer 页在家底缺失时，那一行说的是它自己的事 —— 模型下拉与那一圈格子的家底
        // 就是那份文件，缺了界面只会一片空白（用户实测："文件没有，导致现在无法启动，也看不到模型参数"）。
        // 只在**当前页是 Ninfer** 时顶掉 Strata 那条，另两页的提示一字不动。
        var homeMissing = ninferOn && ActiveView.Key == NinferKey && !File.Exists(NinferIniPath());
        // 本轮没重扫 Strata（DEC-097⑤ 那批作用域调用）就别动那条"未配置"提示，否则 Ninfer 的一次点击
        // 会把 Strata 页的提示行擦掉；家底缺失那一句照说，它是 Ninfer 自己的落点。
        if (wantStrata || homeMissing)
            SetHint(homeMissing ? NinferHomeMissingText(NinferIniPath())
                : unconfigured.Count == 0
                ? ""
                : "已下载但未配置：" + string.Join("、", unconfigured) + " —— 在设置页 Strata 节里点「运行 SETUP」配好后即可在此切换");
        if (unconfigured.Count > 0) LogS("已下载但未配置的模型: " + string.Join(", ", unconfigured));

        // 2026-10-02 反馈（BUG-027："加载模型时上方填的参数变回初始值"）：推视图前先把当前页的编辑
        // 收进 _config.Current——PushCard2ForActiveModule 是**从 _config.Current 推**的，不收就会用
        // 旧的已保存值把用户刚改的格子盖回去（加载/停止/服务退出都会走到这里）。
        _config.Current = CollectSettings();
        RefreshModuleScopedUi();    // 页头分段 / 卡2 / 卡3（含选中恢复）/ 左栏 / 状态灯（B 段整块视图）
        OnModelChanged();
        RefreshStatusUi();
    }

    ModelEntry? SelectedModel() => _cboModel.Selected?.Tag as ModelEntry;

    // 卡3 下拉（B 段）：只列激活模块的模型（分组标题自然消失），并恢复它自己上次的选中。
    // 事件被 _applying 压住——切页/重扫是"换视图"，不触发"载入模型参数"。
    // 第 0 轮 DEC-094：模型表与"选中记在哪一格"都从 ActiveView 要。
    void RebuildModelList()
    {
        _applying = true;
        _cboModel.SetItems(BuildModelItems());
        var tag = ActiveView.TagOf(_config.Current);
        if (tag.Length > 0) SelectModel(tag);
        if (_cboModel.Selected is null)
        {
            var src = ActiveView.Models();
            var first = src.FirstOrDefault(m => m.Ready) ?? src.FirstOrDefault();
            if (first is not null) _cboModel.SelectByTag(first);
        }
        _applying = false;
        var sel = SelectedTag();
        if (sel.Length > 0) ActiveView.SetTag(_config.Current, sel);
    }

    // 下拉的项：只列激活模块的模型，每项尾部带估算大小（B 段：分页后不再需要分组标题）
    List<DarkItem> BuildModelItems()
    {
        var src = ActiveView.Models();
        var items = src.Select(ToItem).ToList();
        if (items.Count == 0) items.Add(new DarkItem { Text = "（没有可用模型）", Enabled = false });
        return items;
    }

    DarkItem ToItem(ModelEntry m) => new()
    {
        Text = m.Display, Right = m.SizeText, Tag = m, Badge = ModelLoadedBadge(m),
    };

    // 「已加载」判据（DEC-065，纯函数供自检穷举）：这一枚模型此刻是不是真的在显存里。
    // · llama 侧 = 它的 router 报了 loaded/loading（models-max>1 时可以同时标好几枚）
    // · Strata 侧 = 本壳拉起的进程跑的就是它（_runningTag）；网关在听但模型已空闲停掉 → 不标，
    //   端口上是**外部**拉起的服务也一律不标 —— 不知道它加载了哪个，宁可不标也不标错。
    internal const string LoadedBadgeText = "已加载";

    // 第 0 轮的 bool 分岔参数换成按模块键走：三个模块三种"这件在不在显存里"的判据，
    // 再多一个模块时这里仍然是同一张 switch（加模块不许在这里长 if）。
    internal static string ModelLoadedBadgeFor(string backend, bool llamaModelLoaded,
                                               bool strataProcessRunsThisTag, bool ninferProcessRunsThisTag)
        => backend switch
        {
            ModelEntry.BackendLlama => llamaModelLoaded ? LoadedBadgeText : "",
            ModelEntry.BackendNinfer => ninferProcessRunsThisTag ? LoadedBadgeText : "",
            _ => strataProcessRunsThisTag ? LoadedBadgeText : "",
        };

    string ModelLoadedBadge(ModelEntry m) => ModelLoadedBadgeFor(
        m.Backend,
        _llamaLoaded.Contains(m.Tag),
        _process is { HasExited: false } && string.Equals(_runningTag, m.Tag, StringComparison.OrdinalIgnoreCase),
        NinferRunning && string.Equals(_ninferRunningTag, m.Tag, StringComparison.OrdinalIgnoreCase));

    // 加载状态变了就把下拉里那几枚状态词原地刷一遍（不重建列表：重建会吞掉选中项与展开态）
    void RefreshModelBadges() => _cboModel.RefreshBadges(it =>
        it.Tag is ModelEntry m ? ModelLoadedBadge(m) : "");

    // 模型相关的可用性：模型没配视觉就禁用视觉项。
    // （「低内存模式」已于 2026-10-04 DEC-075 整链删除：开关 / Settings.LowRam / --mmap-experts 接管都没了——
    //  配置 args 里若自带 --mmap-experts 属于原样透传，壳不再删也不再补。）
    void OnModelChanged()
    {
        var m = SelectedModel();
        if (m is null)
        {
            _cboVision.Enabled = false;
            return;
        }
        // 「这份模型配置里有没有视觉块」= 只有 Strata 那一族成立：**按 Strata 判，不是按"不是 llama"判**。
        // DEC-097 实跑抓到的 13 秒假死就在这儿：Ninfer 的 ModelEntry.ConfigPath 是那只 14 GB 的 .ninfer，
        // 第三个模块进来时这里没跟着改（还是两模块时代的 `== llama 就跳过`），于是切到 WIN 后端
        // 就把 .ninfer 当 JSON 整读一遍（File.ReadAllText 到 14 GB 直接 OOM 边缘跑十几秒），
        // 而 WSL 那侧的件路径是 /home/... 在 Windows 上秒失败 → 所以"只有 WSL→WIN 卡"。
        if (!ReadsModelConfigForVision(m.Backend))
        {
            _cboVision.Enabled = false;
            return;
        }
        bool hasVision = false;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(m.ConfigPath)) as JsonObject;
            hasVision = node?["vision"] is JsonObject;
        }
        catch { /* 扫描时已报过 */ }

        _cboVision.Enabled = hasVision;
        if (!hasVision && VisionValue() != "none")
        {
            SetVision("none");
            LogS("模型 " + m.Tag + " 的配置里没有视觉块：视觉已置为「关闭」（要开视觉请跑 SETUP）");
        }
        Tip.Set(_cboVision, hasVision
            ? "让模型读图片（--vision）；GPU 为默认，CPU 省显存但很慢"
            : "该模型的配置里没有视觉块：需跑 SETUP 开启视觉后此项才可用");
    }

    /// <summary>要不要为这个模型读一次"配置里有没有视觉块"。**只有 Strata 那一族要**：
    /// Strata 的 <c>ConfigPath</c> 是几 KB 的 <c>strata-&lt;tag&gt;.json</c>，
    /// llama 的是几 GB 的 GGUF、Ninfer 的是几 GB 的 <c>.ninfer</c> —— 按 JSON 整读一遍就是十几秒的界面假死。
    /// 判据给的是"是不是 Strata"而不是"是不是 llama"，加第四个模块时不会再漏一次（DEC-094 那条教训的同族）。</summary>
    internal static bool ReadsModelConfigForVision(string backend) => backend == ModelEntry.BackendStrata;

    // 把某个模型 strata-<tag>.json 里的可调项读进 Settings：换模型时界面显示的就是该模型自己的参数。
    // 必要性：SETUP 写进去的值是该机该模型的合理默认（例如 128K 上下文时 --kv-resident 32768 是打开的、
    // 视觉块是否可用、显卡是怎么定的），壳若用一套硬编码默认值就会把这些静默改掉。
    // 把某个模型的运行配置读进 Settings：DEC-064 起有两个来源共用这套解析 ——
    // 配置模板 <Strata 目录>\strata-<tag>.json（SETUP 写的那份，只读）与物化出来的 data\config\strata\<tag>.json
    // （该模型此刻真正生效的那份，用于一次性迁移建快照）。
    internal static bool ReadKnobsFromModel(string root, string tag, Settings s)
        => ReadKnobsFromFile(Path.Combine(root, "strata-" + tag + ".json"), s);

    internal static bool ReadKnobsFromFile(string path, Settings s)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is null) return false;
            var argv = (node["args"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? new List<string>();
            string? Arg(string flag)
            {
                var i = argv.IndexOf(flag);
                return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
            }
            if (Arg("--max-context") is { } ctx) s.Context = ctx;
            if (Arg("--kv") is { } kv) s.Kv = kv;
            if (Arg("--spec") is { } spec) s.Spec = spec;
            if (Arg("--spec-min-p") is { } smp) s.SpecMinP = smp;
            if (Arg("--expert-cache") is { } ec) s.ExpertCache = ec;
            if (Arg("--prefill") is { } pf) s.Prefill = pf;
            if (Arg("--vram-reserve-mib") is { } vr) s.VramReserveMib = vr;
            s.ApiModel = node["model_name"]?.GetValue<string>() ?? "";
            s.KvResident = argv.Contains("--kv-resident");
            s.Vision = node["vision"] is JsonObject v ? (v["gpu"]?.GetValue<bool>() == true ? "gpu" : "cpu") : "none";
            // 「指定显卡」那一格吃的是「留空 / 0 / 0,2」三种写法，与写侧（`MainForm.RunConfig.cs` 那三条）互逆。
            // 文件里可能是整数（单卡）、逗号串（多卡那档写回的样子）、也可能是数组（SETUP 或手改的形状），
            // 数组那一档**不能**吃 `JsonNode.ToString()` —— 它交出 "[0,2]"，界面上像填了个坏值，下一次「保存」还把方括号原样写回产物。
            s.Gpu = node["gpu"] switch
            {
                null => "",
                JsonValue gv when gv.TryGetValue<int>(out var gi) => gi.ToString(),
                JsonArray ga => string.Join(",", ga.Select(x => x?.ToString() ?? "").Where(t => t.Length > 0)),
                JsonValue gv => gv.ToString(),          // 字符串那一档：实测交回原串（不带引号）
                _ => "",
            };
            s.LayerSplit = node["layer_split"]?.GetValue<string>() ?? "";
            s.ArenaPinGib = node["env"]?["STRATA_ARENA_PIN_GIB"]?.GetValue<string>() ?? "";
            s.HeadLate = node["env"]?["STRATA_HEAD_LATE"] is not null;
            return true;
        }
        catch { return false; }
    }

    // 把 llama.cpp 模型的 ini 段读进 Settings：换模型时界面显示的就是该模型自己的参数
    // （与上面 ReadKnobsFromModel 对位，两个后端各读各的家底）。
    // DEC-079（取消融合层）起两件事变了：
    //   1) **读序 = 段 → `[*]`/文件头 → 引擎默认（空）**，对所有键一视同仁（用户批复 §10.1 第②条：
    //      "全局配置有好几个，读取时都要注意，优先级 = 优先模型单独配置，然后全局"）；
    //   2) 界面上这些格子从"只读展示"变成**可编辑并写回这一段**（inheritedOut 带出"这值是继承来的"，
    //      界面据此标「继承全局」，见 RefreshLlamaInheritanceTips）。
    // 段不存在才返回 false（= 保持界面原样，别把别的模型的格子当成它的值）。
    internal static bool ReadLlamaKnobsFromIni(string iniPath, string section, Settings s,
        HashSet<string>? inheritedOut = null)
    {
        if (!LlamaIni.SectionExists(iniPath, section)) return false;
        var sec = LlamaIni.ReadSectionValues(iniPath, section);
        var glob = LlamaIni.ReadGlobalValues(iniPath);
        string Get(string key) => LlamaIni.EffectiveValue(sec, glob, key).Value;
        bool Inh(string key) => LlamaIni.EffectiveValue(sec, glob, key).Inherited;
        void Note(string key)
        {
            if (inheritedOut is not null && Inh(key)) inheritedOut.Add(key);
        }
        var (ck, cv) = (Get("cache-type-k"), Get("cache-type-v"));
        s.LlamaKv = KvDisplay(ck, cv);
        s.LlamaCtx = Get("ctx-size");
        s.LlamaFlashAttn = LlamaIni.IsTrue(Get("flash-attn"));
        s.LlamaTemperature = Get("temperature");
        s.LlamaTopP = Get("top-p");
        s.LlamaTopK = Get("top-k");
        s.LlamaMinP = Get("min-p");
        s.LlamaPresencePenalty = Get("presence-penalty");
        s.LlamaRepeatPenalty = Get("repeat-penalty");
        s.LlamaSpecDraftNMax = Get("spec-draft-n-max");
        s.LlamaSpecType = Get("spec-type");                       // DEC-145
        var mm = MmprojOffloadWriteForm(sec, glob);               // DEC-145：两拼写折算（旧写取反）
        s.LlamaMmprojOffload = mm.Value;
        s.LlamaReasoningEffort = Get("reasoning-effort");
        foreach (var k in LlamaIniKeys)
        {
            if (k == "mmproj-offload") { if (inheritedOut is not null && mm.Inherited) inheritedOut.Add(k); continue; }
            Note(k);
        }
        return true;
    }

    // 用户在下拉里换模型时调用：载入该模型自己的参数（端口/地址/密钥等与模型无关的项保持不变）。
    // 两个后端各有各的家底：Strata 读 strata-<tag>.json；llama.cpp 读 llama_models.ini 的 [<tag>] 段
    // （用户反馈 2026-10-01：只有 Strata 模型能带出配置，llama 模型不行——两端都要有）。
    //
    // DEC-064 起 Strata 侧是「模板打底 + 该模型自己的快照覆盖」，并且**必须同步回 _config.Current**：
    // 以前只把值推到界面、_config.Current 里只更新 tag，于是"下拉记了 A、参数还是 B"能一路留在落盘的 ini 里
    // （启动时卡2 推的正是那份 ini 快照）。现在两边一起走，且各模型各存各的。
    void LoadKnobsFromModel(string tag)
    {
        if (tag.Length == 0) return;
        var s = CollectSettings();
        // 优先用「选中的那个条目」判后端：两个后端撞名时，先查 _models 会拿错边（M2 撞名场景）
        var sel = SelectedModel();
        var entry = sel is { } s0 && s0.Tag == tag
            ? s0
            : _models.Find(m => m.Tag == tag) ?? _llamaModels.Find(m => m.Tag == tag) ?? _ninferModels.Find(m => m.Tag == tag);
        if (entry is not null && entry.Backend == ModelEntry.BackendNinfer)
        {
            ApplyNinferRecord(tag);       // DEC-096：家底在 ninfer_models.ini 的那一段 args 里
            return;
        }
        if (entry is not null && entry.Backend == ModelEntry.BackendLlama)
        {
            ApplyLlamaRecord(tag);
            return;
        }
        _config.ModelSnaps.TryGetValue(tag, out var snap);   // 没快照 = snap 为 null（DEC-082：来源不再写日志）
        ApplyStrataRecord(tag, snap);
    }

    // llama 侧「按模型取家底」（DEC-079，与下面 Strata 侧的 ApplyStrataRecord 对位）：
    // 读 llama_models.ini 的 [<tag>] 那一段，读序 = 段 → `[*]`/文件头 → 引擎默认（空）。
    // 两条路都要它：下拉换模型（LoadKnobsFromModel）与启动/切页时的校正（Program.cs、SwitchModule）。
    // 顺带记下两份状态 —— 「保存」按 _llamaEffective（**含继承的生效值**）判哪些格子真的变了，
    // 所以"打开看看、点保存"不会把继承摊平成显式值；_llamaInherited 给界面标「继承全局」。
    // 这里**只读不写**：写段是「保存」那一条路的活（与"选完只写输入框、点保存才落盘"同口径）。
    bool ApplyLlamaRecord(string tag)
    {
        if (tag.Length == 0) return false;
        var s = CollectSettings();
        var inh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!ReadLlamaKnobsFromIni(RouterIniPath(), tag, s, inh)) return false;
        _llamaInherited = inh;
        _llamaEffective = LlamaEffectiveWriteForm(RouterIniPath(), tag);
        _llamaLoadedTag = tag;
        // DEC-145：「视觉」那一格照 Strata 同款口径 —— 这一件没配 mmproj 路径就置灰（可用性判据与 Tips 分开收口）
        var secNow = LlamaIni.ReadSectionValues(RouterIniPath(), tag);
        _llamaHasMmproj = secNow.TryGetValue("mmproj", out var mmPath) && mmPath.Trim().Length > 0;
        // 界面载体也同步回 _config.Current（这批键不落 [Current]，但 CollectSettings / ParamSignature 读的是这份内存值；
        // **十四个键都要同步**——少一个，RestoreViewForTest/切页重推就会把界面上的旧 [Current] 值盖回来，
        // DEC-064 在 Strata 侧踩过的同一个坑：家底与选中的段不能各说各话）
        _config.Current.LlamaModel = tag;
        _config.Current.LlamaCtx = s.LlamaCtx;
        _config.Current.LlamaKv = s.LlamaKv;
        _config.Current.LlamaFlashAttn = s.LlamaFlashAttn;
        _config.Current.LlamaTemperature = s.LlamaTemperature;
        _config.Current.LlamaTopP = s.LlamaTopP;
        _config.Current.LlamaTopK = s.LlamaTopK;
        _config.Current.LlamaMinP = s.LlamaMinP;
        _config.Current.LlamaPresencePenalty = s.LlamaPresencePenalty;
        _config.Current.LlamaRepeatPenalty = s.LlamaRepeatPenalty;
        _config.Current.LlamaSpecDraftNMax = s.LlamaSpecDraftNMax;
        _config.Current.LlamaSpecType = s.LlamaSpecType;
        _config.Current.LlamaMmprojOffload = s.LlamaMmprojOffload;
        _config.Current.LlamaReasoningEffort = s.LlamaReasoningEffort;
        ApplySettings(s);                  // 里面会 PushCard2（llama 分支带出继承标注）+ MarkParamsSaved
        OnModelChanged();
        _llamaIniStamp = LlamaIniStamp();   // DEC-212：这一趟刚按文件载过，记下戳 —— 之后文件真变了才重读
        // 用户 2026-10-04 第 2 条：原来这里有一行「已按模型 X 的 llama_models.ini [X] 段载入参数（…）；
        // 继承全局的键：…」——日志区与 logs\llama.log 都不再打。卡2 与高级区本来就把这些值摆在界面上，
        // 换模型时它们就在变（那才是落点），再刷一行同义反复的话只是噪声；继承情况另有 tooltip 交代
        // （RefreshLlamaInheritanceTips）。logChange 这个开关位随之退役。
        return true;
    }

    // llama 家底（`llama_models.ini`）的"版本戳" + 手改检测（2026-10-22，他报"改完 ini 重启服务，
    // 界面上不更新，得切到别的模型再切回来才显示正确值"）。
    // 根因同 Strata 那一侧（DEC-211）：界面卡只在**载入某一段**时读文件，启动服务不读 —— 于是他手改文件后
    // 界面还是老值，而 `StartLlamaServer` 用 `CollectSettings()`（界面卡）取参数，起的还是老配置。
    static string _llamaIniStamp = "";
    internal static void ResetLlamaIniStampForTest() => _llamaIniStamp = "";

    internal string LlamaIniStamp()
    {
        try
        {
            var p = RouterIniPath();
            if (!File.Exists(p)) return "";
            var fi = new FileInfo(p);
            return fi.LastWriteTimeUtc.Ticks.ToString() + ":" + fi.Length.ToString();
        }
        catch { return ""; }
    }

    /// <summary>拉 llama 之前比一次 ini 的版本戳：变了就按当前选中的段重读一遍并推界面。
    /// 没变 = 一个字节都不读（界面上没保存的草稿照旧参与手动启动，DEC-122① 那条区分不被破坏）。</summary>
    internal bool ReloadLlamaRecordIfIniChanged()
    {
        var stamp = LlamaIniStamp();
        if (stamp.Length == 0 || stamp == _llamaIniStamp) return false;
        var tag = SelectedTag().Length > 0 ? SelectedTag() : _config.Current.LlamaModel;
        if (tag.Length == 0) { _llamaIniStamp = stamp; return false; }
        _llamaIniStamp = stamp;
        ApplyLlamaRecord(tag);
        LogL($"llama_models.ini 被改过，已按文件重读「{tag}」那一段");
        return true;
    }

    // Strata 侧「按模型取家底」：配置模板打底 + 该模型自己的快照覆盖，再同步回界面与 _config.Current。
    // 抽出来是因为有两条路都要它：换模型（LoadKnobsFromModel，按当前下拉选中项判后端）与
    // 启动时校正（Program.cs 直接按 _config.Current.Model 调 —— 启动时可能停在 llama 页，下拉里是 llama 模型）。
    // 这里**只读不写快照**：快照由「保存」（SaveConfig）与一次性迁移维护 —— 换模型只是看，
    // 没点保存就不该把这套值记成"你要的"（与设置页"选完只写输入框、点保存才落盘"同一条口径）。
    bool ApplyStrataRecord(string tag, Settings? snap)
    {
        var s = CollectSettings();
        // DEC-135（A-1）：先把那 16 格按回**出厂默认**，再读模板、再用快照覆盖 ——
        // 与唤醒那一条（`BuildWakeSettings`）同一次序、同一个家。少了这一步，模板里没有的那几格
        // （`ExtraArgs` 与 Context/Kv/Spec/SpecMinP/ExpertCache/Prefill/VramReserveMib）就把"上一件界面上那格"
        // 当成这一件的值；他当时点一次「保存」还会把这份幽灵值永久写进这一件的记录里。
        ResetModelFieldsToDefaults(s);
        if (!ReadKnobsFromModel(s.Root, tag, s)) return false;     // 模板读不到（换目录 / 删了配置）：保持原值
        if (snap is not null) ApplyModelFields(snap, s);
        // 「没有快照时思考档位必须归零」(DEC-064 那一格) 现在由上面的默认打底统一兜住，不再单独写。
        ApplySettings(s);
        ApplyModelFields(s, _config.Current);          // 家底与 tag 一起走，两者不再可能各说各话
        // DEC-122③：这一族键的家已经搬出 [Current]（DEC-206/210 起落 strata_models.ini 的该段），[Current] 里那份副本退役之后，
        // "磁盘上那一份"的基准只能从记录取 —— 不这么做，关窗的「写进去 / 丢弃」就会列出十几行他从没改过的假差异。
        SyncModelBaselineFromRecord(tag);
        OnModelChanged();
        RefreshCommonTips();
        // 用户 2026-10-04 第 2 条（补问一句后确认"要一起删的"）：原来这里有一行
        // 「已按模型 X 的 参数快照 / strata-<tag>.json 载入参数（上下文…、KV…、视觉…）」——
        // 与 llama 侧那条同罪：卡2 / 高级区就是这些值的落点，换模型时它们就在变，日志里再抄一遍只是噪声。
        // 快照 vs 模板这条来源信息一并去掉（`hasSnap` 参数只为这行日志而存在，跟着退役）。
        return true;
    }

}
