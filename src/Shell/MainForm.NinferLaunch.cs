using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MultiLauncher;

// Ninfer 的起前几何：端口选择、显存账（nvidia-smi 判据）、起前护栏（2026-10-15 从 MainForm.Ninfer.cs 整段搬出，一行未改）
public partial class MainForm
{
    // ---------------- 端口 ----------------

    int NinferPublicPortValue() => int.TryParse(_txtPortNinfer.Text.Trim(), out var p) && p is >= 1 and <= 65535
        ? p : 8068;

    /// <summary>内部端口：留空/非法/等于公开口 = 公开口 + 1；再避开 Strata 与 llama 的内部口，
    /// 以及**实际还占着**的那些口（网关/服务改过端口要下次启动才让出）。
    /// wslrelay 那一档**不算撞车**（mirrored 网络下它会代绑 VM 里的口，把它当占用就永远挑不出可用端口）。</summary>
    internal static int NinferPortChoose(int pub, string? text, int strataInternal, int llamaInternal,
                                         int busyGw, int busySvc, int busyLlama)
    {
        int Normalize(string? t)
        {
            var v = int.TryParse((t ?? "").Trim(), out var p) && p is >= 1 and <= 65535 && p != pub ? p : pub + 1;
            if (v == strataInternal || v == llamaInternal || v == pub) v = pub + 2;
            if (v == strataInternal || v == llamaInternal || v == pub) v = pub + 3;
            // DEC-136（C-10）：公开口填到 65535 时上面三条全算出 65536~65538，而旧兜底退回的还是那个越界值
            // —— 日志自称「在 1-65535」，落盘与起进程却拿它当合法口。越界就往**下**找（同样避开另两口与公开口）。
            if (v is >= 1 and <= 65535) return v;
            for (int down = pub - 1; down >= 1; down--)
                if (down != strataInternal && down != llamaInternal && down != pub) return down;
            return 1;
        }
        var p0 = Normalize(text);
        int Busy(int x) => x == busyGw || x == busySvc || x == busyLlama ? 1 : 0;
        if (Busy(p0) == 0) return p0;
        for (int cand = pub + 1; cand < pub + 40 && cand <= 65535; cand++)
        {
            if (cand == pub || cand == strataInternal || cand == llamaInternal) continue;
            if (Busy(cand) == 0) return cand;
        }
        return p0;       // 挤爆了：原样交回去，让引擎自己报错（比猜一个更说得清）
    }

    int NinferInternalPortValue()
    {
        var s = _config.Current;
        var p = NinferPortChoose(NinferPublicPortValue(), s.NinferInternalPort,
            InternalPortValue(), LlamaPortValue(),
            AnyGateway?.Port ?? 0, _serviceBindPort, _llamaActivePort);
        // DEC-137（A-3）：同 llama 那一路 —— 多网关并存时还要对一遍所有在听的口（Strata 的网关口不在上面那三个参数里）
        if (GatewayPortOn(p) != 0)
            p = NinferPortChoose(NinferPublicPortValue(), p.ToString(),
                InternalPortValue(), LlamaPortValue(), p, _serviceBindPort, _llamaActivePort);
        return p;
    }

    int NinferServicePortValue() => NinferGatewayUp ? NinferInternalPortValue() : NinferPublicPortValue();
    bool NinferGatewayUp => GatewayFor(ModelEntry.BackendNinfer) is not null;
    internal bool NinferRunningOrExternal => NinferRunning || _ninferExternal;

    /// <summary>两份空闲阈值按后端各存（DEC-083：阈值各一张），留空/非法 = 0 = 不自动停。</summary>
    internal static int NinferIdleMinutes(Settings s, string backend)
    {
        var text = backend == NinferIni.BackendWsl ? s.NinferIdleWsl : s.NinferIdleWin;
        return int.TryParse((text ?? "").Trim(), out var m) && m > 0 ? m : 0;
    }

    // ---------------- 显存账（项目里第一次出现 nvidia-smi 判据：以前只有端口互检） ----------------

    /// <summary>nvidia-smi 问一次 memory.used / memory.total。失败返回 null（**判不了就当没这条校验**，
    /// 别拿猜出来的数拦人 —— 口径同 DEC-067「上游没给 digest 就按没这条校验通过」）。</summary>
    internal static (double UsedMib, double TotalMib)? QueryGpuMemory()
    {
        double used = 0, total = 0; bool any = false;
        foreach (var parts in RunGpuQuery("memory.used,memory.total"))
        {
            if (parts.Length < 2 || !double.TryParse(parts[0], out var u) || !double.TryParse(parts[1], out var t)) continue;
            used += u; total += t; any = true;                 // 多卡全算进来（本机单卡 4090，这一行等于原值）
        }
        return any && total > 0 ? (used, total) : null;
    }

    /// <summary>起一次 nvidia-smi 按 CSV 拿几列（`--format=csv,noheader,nounits`）。**这是全项目问这张卡的唯一入口**
    /// （起前算数与状态栏的显存/温度都走它）：返回每行切好的字段，失败/超时/没应答 = 空表（调用方按"没这条判据"处理）。
    /// DEC-061 那条纪律照旧：先挂异步读、再限时等，否则拿到的超时是假的。</summary>
    internal static int GpuQueryCallsForTest;     // 证人位：**真起了进程**几次（自检/探针那一趟必须是 0）
    internal static int GpuQueryAttemptsForTest;  // DEC-136（E-3）：**走到入口**几次 —— 在判闸之前计数，
                                                  //   于是"路走到了但被闸挡下"与"这条路根本没走过"分得开（只断上一条就是空判据）
    internal static List<string[]> RunGpuQuery(string fields, int timeoutMs = 3000)
    {
        GpuQueryAttemptsForTest++;
        // DEC-136（C-9）：这道闸以前只写在**调用点**（状态栏采样那一处），入口本身不认 = "测试态一次都不起"
        // 只覆盖了半条路（起前护栏那一路照样 spawn）。按进程收口的闸要收在唯一入口里 —— 判一条护栏有没有生效，
        // 要看它挡的是不是整条路（DEC-111② 那条教训的原话）。
        if (GpuProbeDisabledForTest) return new List<string[]>();
        GpuQueryCallsForTest++;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("nvidia-smi")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                Arguments = $"--query-gpu={fields} --format=csv,noheader,nounits",
            });
            if (p is null) return new List<string[]>();
            var outT = p.StandardOutput.ReadToEndAsync();          // DEC-061：先挂异步读再限时等
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return new List<string[]>(); }
            var text = outT.GetAwaiter().GetResult();
            var rows = new List<string[]>();
            foreach (var raw in text.Split('\n'))
            {
                var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;
                rows.Add(parts.Select(x => x.Trim()).ToArray());
            }
            return rows;
        }
        catch { return new List<string[]>(); }
    }

    /// <summary>这一件起来大约要吃多少显存（GiB）= 权重（真实件字节）+ 设备池那一档。
    /// 换算表来自两条实测日志（教程 §172 与 _local\logs\shell-test-gsq.txt）：
    /// k8v4 池 55,296 token → runtime 6.76 GiB；WSL 那代 rk4v4-e8 池 262,144 → 5.8 GiB。
    /// 表外的精度档按 k8v4 那一档估。**2026-10-12 起不加任何余量**（他定；两代本机实测：
    /// WIN 20,558 MiB 对公式 20.79 GiB、WSL 23,124 MiB 对 22.71 GiB，误差都在几个百分点内 = 那 0.5 GiB 没必要）。
    /// 下拉那格 SizeText 与起前那道显存账**共用它** = 一个家。</summary>
    internal static double NinferGpuNeedGiB(long artifactBytes, long poolTokens, string kvDtype)
    {
        double weights = artifactBytes > 0 ? artifactBytes / 1073741824.0 : 14.0;
        double kibPerToken = kvDtype.Trim().Equals("rk4v4-e8", StringComparison.OrdinalIgnoreCase) ? 23.0 : 132.0;
        double pool = poolTokens > 0 ? poolTokens * kibPerToken / 1048576.0 : 1.5;
        return Math.Round(weights + pool, 1);
    }

    /// <summary>谁在占着显存：壳自己知道的进程点名，剩下的算"别的东西"（本机实测逐进程查询回 [N/A]，
    /// 所以不硬凑一份进程清单，只说得出名字的那几个）。</summary>
    string NinferOccupants()
    {
        var list = new List<string>();
        if (_process is { HasExited: false }) list.Add("Strata 服务（PID " + _process.Id + "）");
        if (_llamaProcess is { HasExited: false }) list.Add("llama-server（PID " + _llamaProcess.Id + "）");
        if (_ninferProcess is { HasExited: false } && !_ninferProcess.HasExited) list.Add("Ninfer 另一件（PID " + _ninferProcess.Id + "）");
        return string.Join("、", list);
    }

    // ---------------- 起前护栏（拒绝启动并说明；一条不达标就不起进程） ----------------

    /// <summary>Ninfer 起服务前的全部判据。返回 null = 可以起；否则 = 给用户看的第一条原因。
    /// 次序 = 便宜的判据在前（读文件/算数），最后才查显存（要拉一次 nvidia-smi）。
    /// <paramref name="argvTokens"/> = **实际要交给引擎那一把**（摘掉壳管理旗标 + 按界面值追加之后）；
    /// 判"这枚旗标在不在命令行上"必须以它为准 —— 只看段里的模板会漏掉"界面上留空 = 把模板那枚摘掉了"这一档
    /// （--max-shared-prefixes 0 缺了 = 实例打砖，正是最不能漏的那一条）。</summary>
    /// <param name="gpuVerdict">DEC-138（C-9 后半）：调用方**已经在别的线程问过**那一发 nvidia-smi 的结果
    /// （null = 够 / 判不了；非 null = 「不够」那句）。给了它，护栏就不再自己问这一条。
    /// <paramref name="gpuProbed"/> = 那一发到底问过了没有：没问过照旧在这里当场问（口径与升级前一致）。</param>
    internal string? NinferPreflight(NinferIni.Section sec, Settings s, string backend, IReadOnlyList<string>? argvTokens = null,
                                     string? gpuVerdict = null, bool gpuProbed = false)
    {
        var tokens = NinferIni.Split(sec.Args);
        var final = argvTokens ?? tokens;
        var banned = NinferIni.FindBanned(final);
        // DEC-136（C-1）：以前判的是 `tokens`（段里的模板）。附加参数挂在**实际那一把**命令行末尾（DEC-107③
        // 给它开格子的全部理由就是"护栏判的是最终 argv，进不了盲区"），所以禁用旗标也必须判 final ——
        // 否则在卡2 填一枚 `--disk-kv-size 8` 就绕过它，而那条的后果是**预分配整块文件**（实测落盘 68.6 GB）。
        if (banned.Count > 0)
            return "模板里有这条环禁用的旗标：" + string.Join(" / ", banned) +
                   "\n\n上游文档点名这些一加就出问题（--disk-cache 这一代根本不存在；--disk-kv-* 会预分配整块文件）。把段里那几枚删掉再起。";
        // ① --max-shared-prefixes 0 必须在**命令行**上（缺了 = 超池逐字节重发会把实例打砖、此后全 503）；壳不自作主张追加
        if (backend == NinferIni.BackendWin &&
            !string.Equals(NinferIni.FlagValue(final, "max-shared-prefixes"), "0", StringComparison.Ordinal))
            return "这一把命令行里没有 `--max-shared-prefixes 0`（或不是 0）。\n\n" +
                   "这条环缺了它会超池逐字节重发、把实例打砖（此后全部 503），所以壳不替你追加 —— " +
                   "请在段里补上这一枚（DEC-107 起界面上不再有这一格）。";
        // ①b WIN 那代其余三条数值上限（照**命令行**判：界面上的格子与段里透传的值都要算）
        if (backend == NinferIni.BackendWin)
        {
            var cap = NinferIni.ArgvProblemWin(final);
            if (cap is not null) return cap;
        }
        // 段里允许只写叶子名（相对引擎目录）→ 与扫描那侧走同一个解析口，先归一成绝对路径。
        // 不这么做的话 ② 会误报"找不到模型件"，而下面位置参数那条也会把叶子名喂给引擎。
        if (backend == NinferIni.BackendWin) sec.File = NinferWinArtifactPath(s, sec.File);
        // ② 件在不在（WIN 本地判；WSL 判 find 的结果，没扫过就先放行、由 find 回来后标未就绪）
        if (backend == NinferIni.BackendWin && !File.Exists(sec.File))
            return "找不到模型件：" + sec.File + "\n\n引擎目录对不对？到设置页 Ninfer 节「WIN 后端 → 引擎目录」里核对。";
        // DEC-136（C-5）：WSL 那一支以前一个字都不判（上面那条只在 WIN 分支），而 find 那趟也没有启动调用点
        // —— 件不存在要等进程起来几秒后 exit 才看得见，日志里还看不出真因。现在：VM 里扫过且没命中 = 当场拒；
        // 没扫过 = 放行并说一句（口径同 DEC-067「判不了就当没这条校验」，宁可少拦也不误拦）。
        if (backend == NinferIni.BackendWsl)
        {
            if (_ninferWslScanned && !_ninferWslFiles.Any(x => string.Equals(x.File, sec.File, StringComparison.Ordinal)))
                return "VM 里没找到这个件：" + sec.File + "\n\n设置页 Ninfer 节「WSL 后端 → 模型目录」那条路径对不对？（这份清单是启动时在本机 find 出来的）";
            if (!_ninferWslScanned) LogN("这一件的「VM 里在不在」没判（WSL 那侧还没扫过；启动后台那趟扫完就能判）");
        }
        // ③ 逐格数值校验（非法值 = 拒绝启动并说明，口径 DEC-086：护栏只换时机不换结论）
        foreach (var f in UiFlagsFor(backend))
        {
            var v = NinferUiValue(f, s, backend);
            var bad = NinferIni.ValueProblem(f, v, backend);
            if (bad is not null) return "界面上「" + f + "」这一格：" + bad;
        }
        // ④ 上下文 > 设备池 —— **这条只在 WIN 那一代成立**（DEC-098 实跑抓到的误拒）。
        // 它的理由是"这条环靠主机池补足，池子比上下文还大 = 摆位没有意义"，而 WSL 那代是**全驻留形状**：
        // 他调好的 `final-*.bat` / 旧壳档位就是 `--kv-capacity 262144` 配 `--max-context 262144`（池 = 上下文），
        // 拿 WIN 的环去判它就会把唯一那件 WSL 模型永远拦在启动前（实测：「上下文 262144 必须大于设备池 262144」）。
        if (CtxVsPoolProblem(backend, s.NinferCtx, s.NinferPool) is { } poolProblem) return poolProblem;
        // ⑤ swift 件 × 行粒度 KV = 静默答错（起前点名，别等模型开口）
        if (s.NinferModel.Contains("swift", StringComparison.OrdinalIgnoreCase)
            && NinferIni.IsRowGranularKv(s.NinferKvDtype))
            return $"件 {s.NinferModel} 不能配行粒度 KV（{s.NinferKvDtype}）：实测会**静默答错**，不是报错。" +
                   "把「KV 精度」改回 k8v4，或换一件。";
        // ⑥ 起前算数（主机池页数 + 设备池页数 ≥ 逻辑上下文页数）
        var math = NinferIni.HostPages(s.NinferCtx, s.NinferPool, s.NinferHostKib, s.NinferKvDtype);
        if (math.Checked && math.HaveHostKib < math.NeedHostKib)
            return "主机池不够：" + math.Text + "\n\n把「主机缓存」加大，或把上下文/设备池改小。";
        if (math.Checked) LogN("$ 起前算数：" + math.Text + " → 够");
        else LogN("$ 起前算数：这一档（" + s.NinferKvDtype + "）包文档没给页数换算，这条不判");
        // ⑦ WSL 选到 dflash2（件里没那个头 = 引擎 FATAL missing component，几秒就退）
        if (backend == NinferIni.BackendWsl && s.NinferSpec.Equals("dflash2", StringComparison.OrdinalIgnoreCase))
            return "WSL 那代引擎没有 dflash2 草稿头（件里缺 → FATAL missing component）。投机方式只能选 mtp / dflash。";
        // ⑧ 显存账（放最后：要拉一次 nvidia-smi）。DEC-097②：没有「不拦」这一档了 ——
        //    人点按钮这一路恒当场拒绝并点名（reject 已经覆盖"我不想排队"，off 只是给自己配一个静默 OOM 开关）。
        var need = NinferNeedGiBFor(sec.Name);
        // DEC-138（C-9 后半）：问过了就吃那份结果，没问过才在这里当场问。
        // 以前这一句是**无条件**当场问 = 手动「加载」点下去 UI 线程最长冻 3 秒（唤醒那条也排在 UiAsync 里）。
        // 两条都不许顺手改结论：`need == null`（这一件没量出来）照旧 = 这条不判（DEC-067「判不了就当没这条校验」）。
        var shortMsg = gpuProbed ? gpuVerdict : (need is null ? null : GpuShortMessage(need.Value));
        if (shortMsg is not null)
        {
            return "显存不够：" + shortMsg + "\n\n先把那一头停掉再点「启动」；不想被这条拦就别在别的模块跑着时起这一件。";
        }
        return null;
    }

    /// <summary>起服务的命令行：模板摘掉壳管理旗标 → 按界面值追加 → 壳定那三枚（host/port/model-id）
    /// → 末尾原样追加「附加参数」（DEC-107，口径同 llama 侧：按空白切分、不写进段里）。
    /// 件路径是**位置参数**（引擎按 argv[1] 读），不写进段里的 args。</summary>
    internal string BuildNinferArgvLine(NinferIni.Section sec, Settings s, string backend, int enginePort)
    {
        var edits = NinferEditsFromUi(s, backend);
        var argv = NinferIni.Build(sec.Args, UiFlagsFor(backend).ToList(), edits,
            ShellOwnedNinferFlags(sec, s, backend, enginePort));
        // DEC-107：附加参数挂在最后 —— 它进的是上面那一把"实际命令行"，起前护栏（NinferPreflight 收到的是
        // NinferIni.Split(argv)）因此看得见它，不存在"从附加参数里绕过护栏"的缝。
        var extra = s.NinferExtraArgs.Trim();
        return extra.Length > 0 ? argv + " " + extra : argv;
    }

    /// <summary>壳管的那几枚旗标（DEC-165② 起多一枚 `api-key`）：`NinferIni.Build` 先摘掉段里的同名键、
    /// 再把这里给的追加回去 —— 也就是"这几枚归壳"。**api-key 只在通用那格非空时才进名单**：
    /// 留空 = 不碰（段里手写过的 key 照旧生效），不是"删掉它"。</summary>
    static (string, string)[] ShellOwnedNinferFlags(NinferIni.Section sec, Settings s, string backend, int enginePort)
    {
        var list = new List<(string, string)>
        {
            ("model-id", sec.Name),
            ("host", backend == NinferIni.BackendWsl ? "0.0.0.0" : (s.Host.Trim() is "" or "0.0.0.0" or "::" ? "0.0.0.0" : s.Host.Trim())),
            ("port", enginePort.ToString()),
        };
        if (s.ApiKey.Trim() is { Length: > 0 } key) list.Add(("api-key", key));
        return list.ToArray();
    }

    /// <summary>五个 NINFER_KV_* 固定注入（**只 WIN 那条环需要，WSL 那代不注入** —— 实测两代日志不同形）。
    /// 缺一枚就是静默答错，所以这里给死值，不给界面格、也不进 ini（用户改了段里 args 也绕不过它）。</summary>
    internal static readonly (string Key, string Value)[] NinferRingEnv =
    {
        ("NINFER_KV_WINDOW", "16384"), ("NINFER_KV_RETRIEVE", "8192"), ("NINFER_KV_RING", "1"),
        ("NINFER_HOST_PAGEABLE", "1"), ("NINFER_KV_REUSE_HOSTBACKED", "1"),
    };

}
