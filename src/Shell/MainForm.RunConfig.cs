using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 运行配置物化（界面参数 → 引擎实读的那一份）。2026-10-15 从 MainForm.Models.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 运行配置物化 ----------------

    /// <summary>DEC-097④：上下文超过训练长度时必须开 rope 扩展（他那份 512K 实测文档的原话：
    /// "512K 必须开 rope 扩展"，引擎启动行里就是 <c>rope scaling yarn, factor 2 … against a trained context of 262144</c>）。
    /// 界面上只有「上下文」一格，所以这两枚由壳按 <c>因子 = 上下文 / 训练长度</c> 推 ——
    /// 与 SETUP 自己的规则一致（文档验算过：393216→1.5、524288→2、786432→3）。
    /// 返回 null = 不用开（上下文没超过训练长度）。**训练长度从模板反推**：模板自己那把
    /// <c>--max-context</c> 除以模板已带的 <c>--rope-scale</c>（没带就当 1）——
    /// 这样"模板本来就是 512K + yarn 2"的场合推出来还是 262144，不会把 524288 当训练长度再乘一次。</summary>
    internal static (string Mode, string Factor)? RopeFor(long trainedContext, long ctx)
    {
        if (trainedContext <= 0 || ctx <= trainedContext) return null;
        double f = Math.Round((double)ctx / trainedContext, 2);
        return ("yarn", f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>从模板 args 里反推训练长度（见 <see cref="RopeFor"/>）。取不到数就给 0 = 这条不推。</summary>
    internal static long TrainedContextOf(IReadOnlyList<string> args)
    {
        // IReadOnlyList 没有 IndexOf（List.IndexOf 那个扩展要 List<T>），自己扫一遍标记位
        string? At(string flag)
        {
            for (int i = 0; i + 1 < args.Count; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }
        if (!long.TryParse(At("--max-context"), out var baseCtx) || baseCtx <= 0) return 0;
        var scale = At("--rope-scale");
        if (scale is not null && double.TryParse(scale, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0)
            return (long)Math.Round(baseCtx / s);
        return baseCtx;
    }

    internal static bool BuildEffectiveConfig(Settings s, out string effPath, out string error)
    {
        effPath = ""; error = "";
        if (s.Model.Length == 0) { error = "还没有选择模型。"; return false; }
        var basePath = Path.Combine(s.Root, "strata-" + s.Model + ".json");
        if (!File.Exists(basePath)) { error = "找不到模型配置: " + basePath; return false; }

        JsonObject? node;
        try { node = JsonNode.Parse(File.ReadAllText(basePath)) as JsonObject; }
        catch (Exception ex) { error = "模型配置不是合法 JSON: " + ex.Message; return false; }
        if (node is null) { error = "模型配置不是 JSON 对象: " + basePath; return false; }
        if (!int.TryParse(s.Port, out var port) || port < 1 || port > 65535) { error = "端口必须是 1-65535 的整数。"; return false; }
        if (!int.TryParse(s.Context, out var ctx) || ctx < 1024) { error = "上下文必须是 >= 1024 的整数。"; return false; }
        if (!int.TryParse(s.VramReserveMib, out var reserve) || reserve < 0) { error = "显存预留必须是 >= 0 的整数。"; return false; }
        if (s.Spec.Trim().Length == 0 || !int.TryParse(s.Spec, out _)) { error = "--spec 必须是整数（默认 4）。"; return false; }
        if (!double.TryParse(s.SpecMinP, out var specMinP) || specMinP < 0 || specMinP > 1) { error = "--spec-min-p 必须是 0-1 之间的数（默认 0.5）。"; return false; }
        if (s.ExpertCache.Trim().Length == 0) { error = "--expert-cache 不能为空（默认 auto）。"; return false; }
        if (s.Prefill.Trim().Length == 0) { error = "--prefill 不能为空（默认 auto）。"; return false; }
        if (s.ArenaPinGib.Trim().Length > 0 && (!int.TryParse(s.ArenaPinGib, out var pin) || pin < 0))
        { error = "arena pin 必须是 >= 0 的整数，或留空沿用配置里的值（不覆盖）。"; return false; }
        // 思考深度（2026-10-04）：只认聊天页那四个档位（服务端 clean_shared_defaults 也只收这四个，写坏 = 服务拒绝启动）
        var effort = s.ReasoningEffort.Trim();
        if (effort.Length > 0 && effort is not ("none" or "low" or "medium" or "high"))
        { error = "思考深度只能是 none / low / medium / high（或留空 = 不写）。"; return false; }

        // GPU：留空 = 引擎自行选卡；0 = 单卡；0,2 = 多卡
        var gpu = s.Gpu.Trim();
        if (gpu.Length == 0) node.Remove("gpu");
        else if (gpu.Contains(',')) node["gpu"] = gpu;
        else if (int.TryParse(gpu, out var gi) && gi >= 0) node["gpu"] = gi;
        else { error = "指定显卡：留空、一个编号（0）或逗号分隔的多个（0,2）。"; return false; }
        if (gpu.Contains(','))
        {
            if (s.LayerSplit.Trim().Length > 0) node["layer_split"] = s.LayerSplit.Trim();
            else node.Remove("layer_split");
        }
        else node.Remove("layer_split");

        node["port"] = port;
        if (s.Host.Trim().Length > 0) node["host"] = s.Host.Trim(); else node.Remove("host");
        // 界面已去掉 API 密钥字段：填了才写，留空则保留 config 里原有的（不主动删）
        if (s.ApiKey.Trim().Length > 0) node["api_key"] = s.ApiKey;
        // 服务对外宣告的模型名：客户端 API 里的 model 字段就是它（留空 = 保留 config 原本的）
        if (s.ApiModel.Trim().Length > 0) node["model_name"] = s.ApiModel.Trim();

        // 引擎参数：丢掉壳要接管的那些（值和布尔两种），再按当前界面追加
        var src = (node["args"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? new List<string>();
        var kept = new List<string>();
        for (int i = 0; i < src.Count; i++)
        {
            var a = src[i];
            if (BoolFlags.Contains(a)) continue;
            if (ValueFlags.Contains(a)) { i++; continue; }
            kept.Add(a);
        }
        kept.Add("--expert-cache"); kept.Add(s.ExpertCache.Trim());
        kept.Add("--prefill"); kept.Add(s.Prefill.Trim());
        kept.Add("--spec"); kept.Add(s.Spec.Trim());
        kept.Add("--spec-min-p"); kept.Add(s.SpecMinP.Trim());
        kept.Add("--max-context"); kept.Add(ctx.ToString());
        // DEC-097④：上下文超过训练长度 → 自动补 rope（yarn + 因子），回到训练长度以内就把这两枚摘掉。
        // 界面上不该再多两格：这两枚从来不是独立旋钮，它是「上下文」这个数的机械后果（写坏 = 拿没扩展的角度跑 2 倍长度）。
        var rope = RopeFor(TrainedContextOf(src), ctx);
        if (rope is not null)
        {
            kept.Add("--rope-scaling"); kept.Add(rope.Value.Mode);
            kept.Add("--rope-scale"); kept.Add(rope.Value.Factor);
            // DEC-098④ 续：同一个触发条件再补一枚 `fit_max_tokens`。超训练长度跑的就是"长提示 + 大 max_tokens"
            // 那种请求，而引擎默认对装不下的 max_tokens **直接回 400**（serve/server.py：`or add "fit_max_tokens": true`）；
            // 他那份 512K 实测里三件候选全都开了它（截断到装得下的量，而不是让客户端吃一个 400）。
            // **模板里写过就照模板的**（true / false 都尊重）：不想被悄悄截短就在
            // <Strata 目录>\strata-<tag>.json 里加 "fit_max_tokens": false —— 那份是他人手编的、壳只读。
            // 物化每次都是从模板全量重建，所以回到训练长度以内时这一枚自然消失，不会留下上一次的 true。
            if (node["fit_max_tokens"] is null) node["fit_max_tokens"] = true;
        }
        if (ctx > 8192) { kept.Add("--kv"); kept.Add(s.Kv.Trim()); }
        if (s.KvResident) { kept.Add("--kv-resident"); kept.Add("32768"); }
        // （原来这里按 s.LowRam 追加 --mmap-experts：2026-10-04 DEC-075 整链删除——配置 args 自带则原样透传）

        var vision = s.Vision;
        if (vision != "none")
        {
            if (node["vision"] is not JsonObject v)
            { error = $"模型 {s.Model} 的配置里没有视觉块，无法开启视觉；请跑 SETUP 开启，或把视觉设为「关闭」。"; return false; }
            v["gpu"] = vision == "gpu";
            v["max_tokens"] = vision == "gpu" ? 1024 : 300;
            if (vision == "cpu") v["threads"] = Math.Max(1, Environment.ProcessorCount / 2);
            else v.Remove("threads");
            kept.Add("--vision");
            kept.Add("--vram-reserve-mib");
            kept.Add(reserve.ToString());
        }
        else node.Remove("vision");

        foreach (var extra in s.ExtraArgs.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            kept.Add(extra);

        var arr = new JsonArray();
        foreach (var x in kept) arr.Add(JsonValue.Create(x));
        node["args"] = arr;

        // 本机修复相关环境变量：经 config 的 env 传给引擎（serve/server.py 的 child_env）。
        // STRATA_ARENA_PIN_GIB（2026-10-03，DEC-036）：**留空 = 沿用配置里的值（不删）**——0.1.35 起本机
        // WDDM 修复就写在配置的 env 里（官方机制），壳绝不能把它抹掉；填了值才覆盖（8 = 应急降级；0/auto ≈ 不设限，勿用）。
        var env = node["env"] as JsonObject ?? new JsonObject();
        if (s.ArenaPinGib.Trim().Length > 0) env["STRATA_ARENA_PIN_GIB"] = s.ArenaPinGib.Trim();
        if (s.HeadLate) env["STRATA_HEAD_LATE"] = "1";
        else env.Remove("STRATA_HEAD_LATE");
        if (env.Count > 0) node["env"] = env; else node.Remove("env");

        try
        {
            var dir = RunConfigDir();
            Directory.CreateDirectory(dir);
            effPath = EffectiveConfigPath(s.Model);
            File.WriteAllText(effPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new System.Text.UTF8Encoding(false));
            // 网页里的 Chat 设置存在 config 同名的 .shared-settings.json：第一次接过来，免得用户的偏好丢失
            var sharedSrc = Path.Combine(s.Root, "strata-" + s.Model + ".shared-settings.json");
            var sharedDst = Path.Combine(dir, s.Model + ".shared-settings.json");
            if (File.Exists(sharedSrc) && !File.Exists(sharedDst)) File.Copy(sharedSrc, sharedDst, false);
            // 思考深度（2026-10-04）：Strata 服务端的"默认思考档"就住在这个共享文件里（聊天页的 Thinking 是它，
            // 服务启动时读、请求没自带档位时套用）。非空 = 每次物化按这一格写入；留空 = 一个字都不碰。
            // 代价说清：填了这一格，聊天页里临时改的档位会在下次启动服务时被这里盖回来。
            if (effort.Length > 0) MergeSharedReasoningEffort(sharedDst, effort);
        }
        catch (Exception ex) { error = "写入运行配置失败: " + ex.Message; return false; }
        return true;
    }

    // 把共享默认思考档并进 <configStem>.shared-settings.json（2026-10-04）：读旧的（坏 JSON 当空重来）→ 设键 → 写回。
    // 单独抽成静态的原因：自检能拿临时文件把四种情形（没有文件 / 保留别的键 / 覆盖旧值 / 坏文件重来）全断言一遍，
    // 不碰真实运行目录里的任何东西。
    internal static void MergeSharedReasoningEffort(string sharedPath, string level)
    {
        JsonObject shared;
        try
        {
            shared = File.Exists(sharedPath)
                ? JsonNode.Parse(File.ReadAllText(sharedPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch { shared = new JsonObject(); }   // 坏文件按空重来（服务端读它本来也是"读不了就当没有"）
        shared["reasoning_effort"] = level;
        File.WriteAllText(sharedPath, shared.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(false));
    }

    // 让用户用官方 SETUP 装/配另一个模型（壳不接管几十 GB 的下载流程）。
    // 2026-10-02（DEC-033）：重扫从「启停服务」改挂到这里——SETUP 窗一关就自动重扫，
    // 新模型不用再靠"装完来回启停一次服务"拾取；原来那句"装完回来展开下拉会重扫"早已不成立（DEC-015 删了展开重扫）。
    internal void RunSetup()
    {
        var s = CollectSettings();
        var bat = Path.Combine(s.Root, "START-HERE.bat");
        if (!File.Exists(bat)) { Dialogs.Message(this, "找不到 SETUP", "找不到 " + bat, "Strata 目录对不对？到设置页「Strata」节里核对。", danger: true); return; }
        LogS("$ 打开 SETUP（START-HERE.bat --setup）：按提示回答问题；关掉 SETUP 窗口后会自动重扫模型");
        try
        {
            // 用 cmd /c call 起 bat（UseShellExecute=false 才能挂 Exited）：照旧弹独立控制台窗，
            // 窗口关闭（cmd 退出）即视为 SETUP 结束 → 回到 UI 线程重扫一次
            var psi = new ProcessStartInfo("cmd.exe")
            {
                WorkingDirectory = s.Root,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("call");
            psi.ArgumentList.Add(bat);
            psi.ArgumentList.Add("--setup");
            var p = Process.Start(psi);
            if (p is not null)
            {
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) =>
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (IsDisposed) return;
                            LogS("SETUP 窗口已关闭：重新扫描模型");
                            RefreshModels();
                        }));
                    }
                    catch { /* 窗口已销毁 */ }
                };
            }
        }
        catch (Exception ex) { LogS("打开 SETUP 失败: " + ex.Message); }
    }

}
