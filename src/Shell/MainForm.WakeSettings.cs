using System.Text;

namespace MultiLauncher;

// DEC-122①：唤醒取参 = 「配置模板 ⊕ [Model:<tag>] 记录」（唯一判据位）。
// 2026-10-15 从 MainForm.Config.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- DEC-122①：唤醒取参 = 「配置模板 ⊕ [Model:<tag>] 记录」 ----------------
    // 根因（DEC-122 背景②，2026-10-09 凌晨他报的"速度大降 + 网关转发失败"）：唤醒以前用**界面卡的现值**物化，
    // 而换下拉是一个"看一眼"的动作、会把所看那个模型的存档载上卡（ApplyStrataRecord，不写盘）。
    // 于是他"打开 512K 那一件的配置看了一眼"之后，请求另一件的唤醒就从这张卡物化 ——
    // 524288 + KV 驻留关被带进别人的启动：预填充 45-49s → 58-62s、专家缓存 9272 → 5072 格、解码 ~100 → 50-65，
    // 慢到踩破客户端 ~60 秒超时，网关往死连接写数据才报那句"转发失败"。
    // 判据改成正向的：**唤醒只认文件里的那一份**，界面上没点「保存」的草稿一律不参与。
    // 为什么不用物化产物打底：产物是上一次跑出来的结果，拿它当输入 = 自洽循环，会把上一次的错误固化成家底。

    /// <summary>一次启动该用哪一份值（DEC-122①，只有两条路）：
    /// <paramref name="wakeFromRecord"/> = true → 网关唤醒/切件，按「配置模板 ⊕ 该模型记录」；
    /// false → 手动「启动 / 加载」，按界面卡现值（含没点保存的草稿 = 他的试验流，一个字没动）。
    /// 端口 / 监听地址这类"不跟模型走"的字段两种情况都照旧取壳设置；<c>Model</c> 恒等于要起的那一件。</summary>
    internal Settings StartSettingsFor(string tag, bool openBrowser, bool wakeFromRecord)
    {
        var s = wakeFromRecord ? BuildWakeSettings(tag) : CollectSettings();
        s.Model = tag;
        s.OpenBrowser = openBrowser;
        return s;
    }

    /// <summary>某一模型"文件里的那一份"= 配置模板打底 ⊕ <c>[Model:&lt;tag&gt;]</c> 记录
    /// （次序与界面载入那条同一条 = 模板 → 记录覆盖，两个调用点共用这一个家）。
    /// 模板读不到（换目录 / 删了配置）就在这里原样不动，交给 <see cref="BuildEffectiveConfig"/> 那句
    /// "找不到模型配置: …" 拒启 —— 唤醒不许猜一套默认值把进程起来。</summary>
    internal Settings BuildWakeSettings(string tag)
    {
        var s = _config.Current.Clone();            // 不跟模型走的字段（Root / 端口 / 地址 / API 密钥…）照旧取当下这份
        // DEC-135（A-1）：打底改成**出厂默认**，不再用 `_savedParams`。后者是"上一件的那一份"——
        // 它跟着界面上看过的最后一件走（`SyncModelBaselineFromRecord` 每次换下拉都同步一次），
        // 拿它打底就等于把别人的格子交给这一件。模板与记录覆盖不到的那几格（`ExtraArgs` 与模板里没写的
        // Context/Kv/Spec/SpecMinP/ExpertCache/Prefill/VramReserveMib）从此落到默认值，而不是上一件的值。
        ResetModelFieldsToDefaults(s);
        s.Model = tag;
        ReadKnobsFromModel(s.Root, tag, s);         // 模板打底（模板里没有的键保持默认，见上面那条注释）
        if (_config.ModelSnaps.TryGetValue(tag, out var snap)) ApplyModelFields(snap, s);
        // 「没有记录 → 思考档位必须归零」那一条现在由上面的默认打底统一兜住（`new Settings().ReasoningEffort == ""`），
        // 不再单独写一格 —— 单独写正是"这一族还有别的格没人管"的字面证据。
        return s;
    }

    /// <summary>DEC-122③：模型家底那 16 格的"磁盘基准"（冻结表 <see cref="_savedParams"/>）改从记录取。
    /// 这一族的家已经从 <c>[Current]</c> 搬到 <c>[Model:&lt;tag&gt;]</c>，基准不跟着改就会冒**假差异**：
    /// 记录里 Context=524288、而基准还是 <c>new Settings()</c> 的 131072 → 关窗那句「写进去 / 丢弃」
    /// 一开就是十几行他从没改过的"131072 → 524288"（他点名的防护①，这一族出过 BUG-034/049）。
    /// 只在载入某个模型的记录之后调（换下拉与启动校正走的都是 <see cref="ApplyStrataRecord"/>）。</summary>
    void SyncModelBaselineFromRecord(string tag) => ApplyModelFields(BuildWakeSettings(tag), _savedParams);

    // 一次性迁移（DEC-064）：老 ini 只有一份扁平值，升级后各模型第一次该显示什么？
    // 取「该模型此刻真正生效的那份」= data\config\strata\<tag>.json（每次启动服务按界面物化，文件名就是 tag，
    // 比 [Current] 可信：[Current] 里那批值可能属于另一个模型）。没有这份文件（该模型从没启动过）就不建快照，
    // 让它回落到配置模板。
    // 例外：ArenaPinGib 不从物化配置取 —— 它的语义是「留空 = 沿用模板里的值、只覆盖不删除」，
    // 文件里那个数分不清是用户填的还是从模板抄来的，所以沿用界面上的旧值（AGENTS 里那条硬约定）。
    internal static int SeedModelSnapsFromRunConfigs(Settings current, Dictionary<string, Settings> snaps,
        IEnumerable<string> tags, string runDir)
    {
        if (snaps.Count > 0) return 0;        // 已经有快照 = 已迁过（或用户存过），不再插手
        int n = 0;
        foreach (var tag in tags)
        {
            var runPath = Path.Combine(runDir, tag + ".json");
            if (!File.Exists(runPath)) continue;
            var s = new Settings();
            if (!ReadKnobsFromFile(runPath, s)) continue;
            s.ArenaPinGib = current.ArenaPinGib;
            snaps[tag] = s;
            n++;
        }
        return n;
    }

    internal readonly struct WindowRect(int x, int y, int w, int h, bool max)
    {
        public readonly int X = x, Y = y, W = w, H = h;
        public readonly bool Max = max;
    }

    internal sealed class ConfigParseResult
    {
        public bool SawAutoStartStrata, SawLlamaRoute;
        public int X, Y, W, H;
        public bool Maximized;
        public WindowRect ToRect() => new(X, Y, W, H, Maximized);
    }

    // 整个 ini 的序列化 / 解析抽成静态：新增的 [Model:<tag>] 段要能被自检 round-trip 断言，
    // 而自检不开窗、也没有 MainForm 实例（同 DEC-037 把改名抽成 MigrateConfigName 的理由）。
    // 2026-10-04（DEC-073）：预设已拆；DEC-206 起段只剩 [Current] / [Window]（每模型参数搬出本 ini）。
    // A（2026-10-22，DEC-206 / DEC-210）：**`[Model:<tag>]` 段整套不再写出** —— 模型参数的家搬到
    // `data\config\strata\strata_models.ini`（见 MainForm.ModelSnaps.cs）。于是 ini 里只剩 [Current] / [Window]，
    // 而"老段下次保存成功后自动删"就是这一步的机械后果：整份 ini 是重写的，不写的段自然没了。
    // 读侧照旧认老段（ParseConfigLines 一个字没改）—— 迁移要从旧值里取那一次。
    internal static List<string> BuildConfigLines(Settings current, WindowRect win)
    {
        var lines = new List<string>(IniSection("Current", current));
        lines.Add("");
        lines.Add("[Window]");
        lines.Add($"X={win.X}");
        lines.Add($"Y={win.Y}");
        lines.Add($"Width={win.W}");
        lines.Add($"Height={win.H}");
        lines.Add($"Maximized={win.Max}");
        return lines;
    }

    /// <summary>通用那一枚 API Key（DEC-165②：三模块共用一枚，住设置页「通用 → 接口」）—— 取值只有一个家，
    /// 三个物化点（Strata 运行配置的 api_key / llama 的 --api-key / Ninfer 的 --api-key）与壳自己的探活都问它。</summary>
    internal string ApiKeyValue() => _config.Current.ApiKey.Trim();

    /// <summary>壳自己那几行 `$ 命令行` 要过它：设了 API Key 时把 key 换成 *** ——
    /// 否则密钥会跟着命令行落进界面日志、内存历史与 `logs\*.log`（口径同"日志不许替用户做决定"那条：该记的记，密钥除外）。</summary>
    internal static string RedactApiKey(string line, string key)
        => key.Length == 0 ? line : line.Replace(key, "***", StringComparison.Ordinal);

    /// <summary>壳自己的探测请求要带的那枚头（DEC-165②）：留空 = null（一个头都不加，引擎不认它）。</summary>
    internal static string? ApiAuthHeaderValue(string key)
        => key.Trim() is { Length: > 0 } k ? "Bearer " + k : null;

    internal static void ParseConfigLines(IEnumerable<string> lines, Settings current,
        Dictionary<string, Settings> snaps, ConfigParseResult res)
    {
        string? section = null;
        Settings target = current;
        // DEC-151：llama 空闲卸载单位 秒 → 分钟 的一次性换算要知道"同一份文件里见过新键没有"（见过 = 新键为准、不接管）
        bool sawSleepIdleMinutes = false;
        // DEC-157：显存互斥那一档升到三模块共用，两个键改名的同一条换算判据（见过新键 = 旧键不接管）
        bool sawGpuMutex = false, sawGpuWakeWait = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                if (section == "Current") target = current;
                else if (section.StartsWith(Config.LegacyPresetPrefix, StringComparison.Ordinal))
                {
                    // 旧版预设段（2026-10-04 已拆，DEC-073）：**识别并跳过**——键写进一个丢弃对象，
                    // 绝不能落回 [Current]（老段随下一次保存自然消失，不做迁移）。
                    target = new Settings();
                }
                else if (Config.IsModelSnap(section))
                {
                    // DEC-064：每个模型自己的参数快照（段名里的 tag 就是键）
                    target = new Settings();
                    snaps[Config.ModelSnapTag(section)] = target;
                }
                else target = current;
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (section == "Window")
            {
                switch (key)
                {
                    case "X": int.TryParse(val, out res.X); break;
                    case "Y": int.TryParse(val, out res.Y); break;
                    case "Width": int.TryParse(val, out res.W); break;
                    case "Height": int.TryParse(val, out res.H); break;
                    case "Maximized": res.Maximized = val is "True" or "true"; break;
                }
                continue;
            }
            if (section == "Current")
            {
                if (key == nameof(Settings.AutoStartStrata)) res.SawAutoStartStrata = true;
                if (key == nameof(Settings.LlamaRouteMode)) res.SawLlamaRoute = true;
                if (key == nameof(Settings.LlamaSleepIdleMinutes)) sawSleepIdleMinutes = true;
                if (key == nameof(Settings.GpuMutex)) sawGpuMutex = true;
                if (key == nameof(Settings.GpuWakeWaitSec)) sawGpuWakeWait = true;
            }
            // DEC-157：那一档从 Ninfer 一家升到**三模块共用**，两个键跟着改名（NinferGpuMutex → GpuMutex、
            // NinferWakeWaitSec → GpuWakeWaitSec）。读侧只认旧名**这一次**（同文件见过新键 = 新键为准），
            // 旧键随下一次保存自然消失（写侧没有那两个属性，口径同 DEC-151 的 LlamaSleepIdleSeconds / DEC-075 的 LowRam）。
            if (section == "Current" && key == "NinferGpuMutex" && !sawGpuMutex)
            {
                current.GpuMutex = val;
                continue;
            }
            if (section == "Current" && key == "NinferWakeWaitSec" && !sawGpuWakeWait)
            {
                current.GpuWakeWaitSec = val;
                continue;
            }
            // DEC-151：旧键 LlamaSleepIdleSeconds（秒）退役后不再是属性 —— 读侧只在这里认**这一次**做换算：
            // 同文件见过新键 = 不接管（新键为准）；秒数 > 0 向上取整到分钟（卸载不早于原来），0/负/空 = 不自动卸
            //（= 旧行为"不传旗标"）。旧键随下一次保存自然消失（写侧没有这个属性，口径同 LowRam DEC-075 / LlamaUseRawIni DEC-079）。
            if (section == "Current" && key == "LlamaSleepIdleSeconds")
            {
                if (!sawSleepIdleMinutes)
                    current.LlamaSleepIdleMinutes = int.TryParse(val, out var secOld) && secOld > 0
                        ? ((secOld + 59) / 60).ToString() : "";
                continue;
            }
            // 快照段只收模型家底：别的键（哪怕被旧版本整段写过）都不该进到 per-model 值里
            if (section is not null && Config.IsModelSnap(section) && !ModelOwnedKeys.Contains(key)) continue;
            var prop = typeof(Settings).GetProperty(key);
            if (prop is null) continue;
            if (prop.PropertyType == typeof(bool))
                prop.SetValue(target, val is "True" or "true");
            else
                prop.SetValue(target, val);
        }
    }

    static IEnumerable<string> IniSection(string name, Settings s, string[]? onlyKeys = null)
    {
        yield return $"[{name}]";
        foreach (var p in typeof(Settings).GetProperties())
        {
            if (onlyKeys is not null && !onlyKeys.Contains(p.Name)) continue;
            // [Current] 走反射全属性，所以"退役"必须在写侧点名（DEC-079）：这些键的家底已经搬到
            // llama_models.ini 的模型段里 / 或路径改成固定不再可配。读侧照旧认（一次性搬家要从旧值里取），
            // 于是旧键随下一次保存自然消失 —— 口径同 [Preset:]（DEC-073）与 LowRam（DEC-075）。
            if (onlyKeys is null && RetiredCurrentKeys.Contains(p.Name)) continue;
            yield return $"{p.Name}={p.GetValue(s)}";
        }
    }

    /// <summary>[Current] 里已退役、不再写出的键（DEC-079 取消融合层：llama 的每模型参数住 llama_models.ini 的段里；
    /// DEC-122③ 起 Strata 那 16 个模型键的**重复副本**同理，家 = [Model:&lt;tag&gt;] 记录）。
    /// 读侧照旧认（一次性搬家要从旧值里取；③ 之后启动值与冻结基准也还从旧行里读），于是旧键随下一次保存自然消失（口径同 [Preset:] DEC-073）。
    /// 退役之后"磁盘上那一份"必须改从记录取，否则关窗明细会冒他没改过的假差异 —— 见 <see cref="SyncModelBaselineFromRecord"/>。
    /// 「用原始 ini 启动」那位不列在这里 —— 属性整个删了，读到就命中"没这个属性"被忽略（同 LowRam，DEC-075）。</summary>
    internal static readonly string[] RetiredCurrentKeys =
    {
        nameof(Settings.RouterIni),              // 路径固定 = exe 同目录；旧值只当一次性搬家的种子
        nameof(Settings.LlamaCtx), nameof(Settings.LlamaKv), nameof(Settings.LlamaFlashAttn),
        nameof(Settings.LlamaTemperature), nameof(Settings.LlamaTopP), nameof(Settings.LlamaTopK),
        nameof(Settings.LlamaMinP), nameof(Settings.LlamaPresencePenalty), nameof(Settings.LlamaRepeatPenalty),
        nameof(Settings.LlamaSpecDraftNMax), nameof(Settings.LlamaSpecType), nameof(Settings.LlamaMmprojOffload),
        nameof(Settings.LlamaReasoningEffort),
        // DEC-096：Ninfer 那批格子同理 —— 家底在 ninfer_models.ini 对应段的 args 里，[Current] 只当界面载体。
        // 「服务怎么跑」那一档（NinferPort / NinferInternalPort / 两份空闲阈值 / 显存互斥处理 / 两个后端开关 /
        //  目录与发行版 / 自动启动 / 跳过位）**照旧落 [Current]**，别把它们加进这张表。
        nameof(Settings.NinferCtx), nameof(Settings.NinferKvDtype), nameof(Settings.NinferPool),
        nameof(Settings.NinferHostKib), nameof(Settings.NinferConcurrency), nameof(Settings.NinferPrefill),
        nameof(Settings.NinferSlots), nameof(Settings.NinferPending), nameof(Settings.NinferPendingSec),
        nameof(Settings.NinferSpec), nameof(Settings.NinferDraft), nameof(Settings.NinferMaxTokens),
        nameof(Settings.NinferTemperature), nameof(Settings.NinferTopP),
        nameof(Settings.NinferTopK), nameof(Settings.NinferPresence), nameof(Settings.NinferEffort),
        nameof(Settings.NinferLmHead), nameof(Settings.NinferVision), nameof(Settings.NinferPreserve),
        // DEC-122③：Strata 那 16 格模型家底的**重复副本**也退役 —— 它们每次提交保存时与当次选中模型的
        // [Model:<tag>] 记录从同一份值写两处（ApiModel / Gpu / LayerSplit / Context / Kv / KvResident / Spec /
        // SpecMinP / ExpertCache / Prefill / Vision / VramReserveMib / ArenaPinGib / HeadLate / ExtraArgs /
        // ReasoningEffort）。值由记录承接，旧副本随下一次保存自然消失（口径同 [Preset:] DEC-073、LowRam DEC-075）。
        // 名单里**没有** Model=（选中指针）：那是导航字段、不是模型家底，照旧落 [Current]。
        // 读侧照旧认（老 ini 里那 16 行还要当启动值与一次性打底的基准），写侧不再写 = 用户无感。
        nameof(Settings.ApiModel), nameof(Settings.Context), nameof(Settings.Kv), nameof(Settings.KvResident),
        nameof(Settings.Spec), nameof(Settings.SpecMinP), nameof(Settings.ExpertCache), nameof(Settings.Prefill),
        nameof(Settings.Vision), nameof(Settings.VramReserveMib), nameof(Settings.Gpu), nameof(Settings.LayerSplit),
        nameof(Settings.ArenaPinGib), nameof(Settings.HeadLate), nameof(Settings.ExtraArgs),
        nameof(Settings.ReasoningEffort),
    };

    // DEC-092：这个函数现在**必须**写明这次落盘算不算"提交界面参数"（没有默认值 = 每个调用点都得表态）。
    //   commitParams=true  → 只有主窗「保存」与关窗选「写进去」：收一遍界面、写 [Current] + 该模型那一段（DEC-210）。
    //   commitParams=false → 顺手落盘（更新链记跳过位 / 内部端口避让 / 切模块开关 / 关窗记窗口位置…）：
    //                         表里那些"参数格"一律按 _savedParams（上次提交的值）写，未保存的改动搭不上这条路。
    // DEC-137（D-3）：这个函数现在**返回"这次到底落盘了没有"**，并且改成"同目录写 tmp → 改名替换"的原子写法。
    //   两条理由：① DEC-126 之后那一份记录（DEC-206/210 起住 `strata_models.ini`）是唤醒取参的唯一事实源，写失败却让「保存」报"界面参数已保存"
    //           = 他以为远端已经用上新参数（口径同 AGENTS 给 llama 段写的那条"写段失败别 MarkParamsSaved"）；
    //   ② `File.WriteAllLines` 是截断后顺序写，写到一半断电 / 被杀就只剩半份 ini，而 data\backup 三格里
    //           没有壳自己 ini 的任何备份 —— 它现在是家底，不能是整机唯一一份不做原子替换的落盘。
    /// <summary>「保存」这一趟要不要动**模型参数文件**（`strata_models.ini`）—— 2026-10-23 审查 A 条立的唯一判据。
    /// 只有提交那一趟写：`commitParams=false` 的落盘（关窗记位置 / 设置页保存 / 切模块 / 端口避让…）内存字典
    /// 一个字都没变，写了只会把他手改的那份盖回旧值。抽成函数是为了这条语义能被自检钉住（不许改回"每次都写"）。</summary>
    internal static bool SaveWritesModelStore(bool commitParams) => commitParams;

    bool SaveConfig(bool commitParams)
    {
        try
        {
            if (commitParams) _config.Current = CollectSettings();
            var cur = _config.Current;
            // 2026-10-23（审查 A 条）：模型参数那份文件**只在「提交」那一趟写**。
            //   commitParams=false 这一趟（关窗记窗口位置 / 设置页点保存 / 切模块开关 / 端口避让 / 更新链记跳过位）
            //   内存里的 `ModelSnaps` 一个字都不会变 —— 写盘没有意义，却正好把他**此刻手改的** `strata_models.ini`
            //   盖回内存那份旧值（16 格回退、注释与 ArgsN 保住，事后看不出发生过什么）。
            //   所以门槛提到这里，写盘只发生在真有话要说的时候；写之前先把文件里被别人改过的读进来。
            if (SaveWritesModelStore(commitParams))
            {
                // 先重读：**别的模型以文件为准**（当前模型那一格紧接着由界面值覆盖 ——
                // 「保存」的意思就是"界面这份我要"，这是 DEC-064 那条口径，没变）。
                var reloadNotes = new List<string>();
                ReloadModelSnapsIfChanged(_config.ModelSnaps, reloadNotes, out _);
                foreach (var n in reloadNotes) Log(n);
                // 模型家底快照只在**提交**时写（DEC-064 的口径：换模型只是看，没点保存不该把这套值记成"你要的"）
                if (cur.Model.Length > 0) _config.ModelSnaps[cur.Model] = SnapshotModelFields(cur);
                CommitSavedParams();
                // A（2026-10-22，DEC-206 / DEC-210）：只改各段那 16 格对应的行（注释 / ArgsN / 自建键一行不动）。
                // **这一步失败就不动 ini** —— 老 `[Model:*]` 段此刻还在 ini 里（写侧已不再写它），
                // 不写 ini = 那份退路原样留着（A1 ①②）。
                // 成功这一趟**不写日志**：保存是日常动作，参数落哪儿在设置页/说明书里说一次就够（口径同 DEC-092 的 llama 侧）。
                if (!SaveModelSnapStore(_config.ModelSnaps, out _, out var snapsErr))
                {
                    Log("模型参数没搬进文件，这次保存没动 ini（老 [Model:*] 段留着当退路）：" + snapsErr);
                    return false;
                }
            }
            var disk = commitParams ? cur : ForDisk(cur);
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var lines = BuildConfigLines(disk,
                new WindowRect(b.X, b.Y, b.Width, b.Height, WindowState == FormWindowState.Maximized));
            var tmp = ConfigPath + ".tmp";
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
            File.Move(tmp, ConfigPath, overwrite: true);      // 同目录改名 = 中途炸了也不会留下半截 ini（形状同 LlamaIni.EditSection）
            _diskWrites++;
            return true;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(ConfigPath + ".tmp")) File.Delete(ConfigPath + ".tmp"); } catch { /* 半截临时文件删不掉也只是垃圾 */ }
            Log("配置保存失败: " + ex.Message);
            return false;
        }
    }

    void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var res = new ConfigParseResult();
                ParseConfigLines(File.ReadAllLines(ConfigPath), _config.Current,
                    _config.ModelSnaps, res);
                (_winX, _winY, _winW, _winH, _winMax) = (res.X, res.Y, res.W, res.H, res.Maximized);
                // B 段迁移（旧 ini 没有新键时）：单一「自动启动服务」→ 每模块一个（当年只作用于 Strata）；
                // llama 的模式跟旧的全局模式（旧版两模块共用一个模式条）
                if (!res.SawAutoStartStrata && _config.Current.AutoStartService) _config.Current.AutoStartStrata = true;
                if (!res.SawLlamaRoute) _config.Current.LlamaRouteMode = _config.Current.WakeOnRequest;
            }
        }
        catch (Exception ex)
        {
            Log("配置读取失败: " + ex.Message);
        }
    }
}
