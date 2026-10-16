namespace MultiLauncher;

// 用例 0 + ⑤ + 0b：两个后端的只读数据 —— 模型扫描与估算大小、llama.cpp 的 ini 解析与版本、
// 分片模型把兄弟块一起数、KV 公式对着 DEC-007 四个实测值复算。要真件（某几段没有 gguf 就自己跳过）。
// 2026-10-15 从 RunBody 整体搬出（块内一行未改；跨块的 models/target/localVersion 见 SelfTest.State.cs）。
internal static partial class SelfTest
{
    internal static void ScanAndSizes()
    {
        // 拆分收尾账（2026-10-15）：target 提升成字段后，编译器不再跨方法跟踪"RunBody 已判过非空"
        //（那条 `target is null` 早退在 RunBody 里），于是这里出现 CS8602。给一个本地非空别名把可空性
        // 交回编译器 —— 零行为改动（判据仍只认 RunBody 那一条早退），只是把编译器的账平掉。
        var target = SelfTest.target!;
            // --- 用例 0：两个后端的只读数据（估算大小 / llama.cpp 的 ini 解析与版本） ---
            foreach (var m in models) W($"  估算 {m.Tag} [{m.Backend}] size={m.SizeText} ready={m.Ready}");
            Check("Strata 模型能算出估算大小", models.Any(m => m.SizeText.Length > 0));
            var def = new Settings();
            // DEC-079：家底路径固定 = exe 同目录（实跑就是 D:\MultiLauncher\llama_models.ini）。
            // 开发目录里没这份，就按壳启动那条**缺才拷**的逻辑取种子（① 旧 ini 记着的老路径 ② 仓库 config\ 那份）；
            // 真拿不到就跳过"要真件"的用例 —— 它们是"用户家底读得出多少模型"的回归，不是壳的逻辑。
            MainForm.EnsureRouterIniPresent("", out var iniSeed);
            routerLive = MainForm.RouterIniPath();
            haveLive = File.Exists(routerLive);
            W("llama_models.ini（固定 = exe 同目录）: " + routerLive + (haveLive ? "" : "  —— 不在，相关用例跳过"));
            var llamaModels = MainForm.ScanLlamaModels(routerLive);
            W("llama.cpp ini: " + routerLive + " -> " + llamaModels.Count + " 个模型");
            foreach (var m in llamaModels) W($"  {m.Tag}  ready={m.Ready}  size={m.SizeText}  {m.ConfigPath}");
            if (haveLive)
            {
                Check("解析 llama_models.ini 得到模型列表", llamaModels.Count > 0);
                Check("llama.cpp 模型能算出估算大小", llamaModels.Any(m => m.SizeText.Length > 0));
                Check("家底路径固定在 data\\config\\llama（DEC-118 归位；不再来自 [Current]，DEC-084 之后是新名 llama_models.ini）",
                    MainForm.DefaultRouterIniPath() == Path.Combine(AppContext.BaseDirectory,
                        "data", "config", "llama", "llama_models.ini")
                    && def.RouterIni.Length == 0, $"默认值 RouterIni=\"{def.RouterIni}\"｜{MainForm.DefaultRouterIniPath()}");
                // 自检期间这条路径必须已经改道（否则上面那些"点保存"的用例会写进他的真件）
                Check("⓪ 自检期间 llama 家底已改道（RouterIniPath 指在 data\\tmp\\ 的副本窝里）",
                    MainForm.LlamaHomeOverriddenForTest
                    && MainForm.RouterIniPath().StartsWith(MainForm.TempDir(), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(MainForm.RouterIniPath(), MainForm.DefaultRouterIniPath(), StringComparison.OrdinalIgnoreCase),
                    MainForm.RouterIniPath());
            }
            // DEC-138（E-7）：软跳过必须留下一条**看得见**的断言。以前这里只 `W(...)` 写一行说明，
            // 于是少跑两条、条数静默下降，末行照样 ALL PASS —— 与 DEC-126 第③条"空判据"是同一件事的两面。
            else Check("DEC-138（E-7）用例前提：llama 家底副本在（不在 = 上面那两条『解析模型列表 / 估算大小』根本没跑，条数不许静默少）",
                       false, "副本读不到：" + MainForm.RouterIniPath());

            // --- ⑤（2026-10-03 用户反馈）：分片模型要把兄弟块一起数进来 ---
            // 本机现象：swift 那份 --native/--ple-gguf 都只指 00001（317 个张量），00002 里还有 907 个张量没被数到，
            // 于是显示 44.6 vs 83.8（实际只差 10%）。这里用临时目录合成一套三片模型来断言补齐与去重。
            var shardDir = MainForm.Scratch("selftest-shard-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                Directory.CreateDirectory(shardDir);
                File.WriteAllBytes(Path.Combine(shardDir, "m-00001-of-00003.gguf"), new byte[1000]);
                File.WriteAllBytes(Path.Combine(shardDir, "m-00002-of-00003.gguf"), new byte[2000]);
                File.WriteAllBytes(Path.Combine(shardDir, "m-00003-of-00003.gguf"), new byte[3000]);
                File.WriteAllBytes(Path.Combine(shardDir, "mmproj-m-F16.gguf"), new byte[9]);      // 不该被当成兄弟
                var first = Path.Combine(shardDir, "m-00001-of-00003.gguf");
                long all3 = ModelSize.ModelBytes(new[] { first });
                Check("⑤ 分片补齐：三片都数到、mmproj 不误收", all3 == 6000, "实算 " + all3);
                Check("⑤ 分片去重：点名两片也只数三份的总量",
                    ModelSize.ModelBytes(new[] { first, Path.Combine(shardDir, "m-00002-of-00003.gguf") }) == 6000);
                Check("⑤ 非分片文件名原样返回（不去凑兄弟）",
                    ModelSize.ModelBytes(new[] { Path.Combine(shardDir, "mmproj-m-F16.gguf") }) == 9);
            }
            finally { try { Directory.Delete(shardDir, true); } catch { /* 临时目录删不掉不影响结论 */ } }
            W("⑤ 两个 Strata 模型的估算：" + string.Join(" / ", models.Where(m => m.SizeText.Length > 0).Select(m => m.Tag + "=" + m.SizeText)));

            // --- 用例 0b：KV 公式对着 DEC-007 的四个 llama.cpp 实测值复算（要求误差 0；要真件 + 真 gguf） ---
            var entries = LlamaIni.ReadModels(routerLive);
            if (haveLive)
            foreach (var (name, want) in new (string, long)[]
                     { ("OvisOCR2-BF16", 768), ("MiniCPM5-2B-F16", 5376), ("Qwen3.8-27B-GSQ-IQ3_S", 8704), ("Qwen3.6-35B-A3B-UD-Q6_K_XL", 1360) })
            {
                var e = entries.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (e is null) { Check("DEC-007 复算 " + name, false, "ini 里没有这一节"); continue; }
                var g = GgufMeta.Read(e.GgufPath);
                long ctx = long.TryParse(e.CtxSize, out var c) ? c : 0;
                long gotMib = g is not null ? ModelSize.KvBytes(g, ctx, e.CacheTypeK, e.CacheTypeV) >> 20 : -1;
                Check($"DEC-007 复算 {name} = {want} MiB", gotMib == want,
                    $"实算 {gotMib} MiB (kvHeadsΣ={g?.KvHeadsTotal()} block={g?.BlockCount} ctx={ctx} {e.CacheTypeK})");
            }
            var llamaVer = LlamaIni.DirVersion(def.LlamaDir);
            W("llama.cpp 版本: " + llamaVer + "  (目录 " + def.LlamaDir + ")");
            Check("从 llama.cpp 目录名取到 build 号", llamaVer.StartsWith('b'), llamaVer);
            Check("目录名里没有 build 号时返回空", LlamaIni.DirVersion(@"D:\not-a-llama-dir") == "");
            // 内置默认目录只是出厂值：用户升过级之后那个版本目录可能已经不在了
            // （2026-10-03 实测：默认写 b11259，机器上已经换成 b11370），所以探不到就退回同父目录里任意一份真在的
            var probeDir = def.LlamaDir;
            var rtVer = LlamaIni.RuntimeVersion(probeDir);
            if (!rtVer.StartsWith('v'))
            {
                // 挨个试同父目录里其它真在跑的 llama.cpp（新 build 优先）：老目录可能缺运行库、探不出版本
                var parent = Path.GetDirectoryName(probeDir) ?? "";
                foreach (var d in (Directory.Exists(parent)
                             ? Directory.EnumerateDirectories(parent, "llama-b*-bin-win-*")
                                 .Where(x => File.Exists(Path.Combine(x, "llama-server.exe")))
                                 .OrderByDescending(LlamaIni.DirVersion)
                             : Enumerable.Empty<string>()))
                {
                    var v = LlamaIni.RuntimeVersion(d);
                    if (!v.StartsWith('v')) continue;
                    probeDir = d; rtVer = v; break;
                }
            }
            W("llama.cpp 运行时版本: " + rtVer + "  (llama-server --version, 目录 " + probeDir + ")");
            Check("llama 运行时版本（探 --version）形如 v<版本>-b<build>", rtVer.StartsWith('v') && rtVer.Contains("-b"), rtVer);
            Check("版本解析：0.5.0-dev + build 11259 -> v0.5.0-b11259",
                LlamaIni.FormatRuntimeVersion("version: 0.5.0-dev (build 11259, commit d280808f5)", "") == "v0.5.0-b11259",
                LlamaIni.FormatRuntimeVersion("version: 0.5.0-dev (build 11259, commit d280808f5)", ""));
            Check("版本解析：只有 build -> 退回 b 号", LlamaIni.FormatRuntimeVersion("", "b11259") == "b11259");
            Check("版本解析：只有版本无 build -> v0.5.0", LlamaIni.FormatRuntimeVersion("version: 0.5.0", "") == "v0.5.0");
            var nativeGguf = Value(BaseArgs(target.ConfigPath), "--native") ?? "";
            var meta = nativeGguf.Length > 0 ? GgufMeta.Read(nativeGguf) : null;
            W($"GGUF 元数据 {Path.GetFileName(nativeGguf)}: arch={meta?.Architecture} block={meta?.BlockCount} " +
              $"kvHeadsΣ={meta?.KvHeadsTotal()} kLen={meta?.KeyLength} vLen={meta?.ValueLength} interval={meta?.FullAttentionInterval}" +
              (meta is null && GgufMeta.LastError.Length > 0 ? "  ERR=" + GgufMeta.LastError : ""));
            Check("读到 GGUF 元数据（算 KV 用）", meta is not null && meta.KvHeadsTotal() > 0 && meta.KeyLength > 0 && meta.ValueLength > 0);

    }
}
