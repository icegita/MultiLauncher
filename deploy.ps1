# 编译 → 发布单文件 → 推送到实跑目录（默认 D:\MultiLauncher，2026-10-03 用户改址；旧址 D:\strata），并核对哈希
param(
    [switch]$NoDeploy,
    [switch]$NoSelfTest,          # 跳过推送后那条实跑自检（默认跑）
    [string[]]$Targets = @('D:\MultiLauncher')
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root
try {
    Write-Host '[1/3] dotnet publish ...'
    # 更名前（2026-10-02，DEC-032）的发布残留先清掉：publish/ 里只留新名产物 MultiLauncher.*
    # 2026-10-22：管道直接喂 Remove-Item（不给 -LiteralPath）会让本机的 PowerShell 安全壳**整份拒跑**
    #（报 `Remove-Item: missing path operand`，脚本一行都没执行）—— 改成逐个点名路径，行为一字不变。
    Get-ChildItem (Join-Path $root 'publish') -Filter 'StrataLauncher.*' -File -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish --nologo
    if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

    $exe = Join-Path $root 'publish\MultiLauncher.exe'
    if (-not (Test-Path $exe)) { throw "missing $exe" }
    $src = Get-Item $exe
    $srcHash = (Get-FileHash $exe -Algorithm SHA256).Hash
    Write-Host ("      {0}  {1:N0} bytes  sha256 {2}" -f $src.Name, $src.Length, $srcHash.Substring(0, 16))

    if ($NoDeploy) { Write-Host '[3/3] -NoDeploy：跳过推送'; return }
    # DEC-138（D-5）：推送前先点名「场上有没有正在跑的外壳」。AGENTS 那条「推送前先退出正在运行的外壳」以前只写在文档里，
    # 真栽过一次：Copy-Item 报「文件正由另一进程使用」，推一半失败、界面还留着旧版，从日志里看不出是被谁占着。
    # 判据 = **目标目录里那只 exe 被进程占着**（按 Path 全等对号），不是「机器上有没有同名进程」——
    # 从 bin\ 或别处跑的实例不占这一只，就不该拦这次推送。
    $busy = @()
    foreach ($t in $Targets) {
        $tp = Join-Path $t 'MultiLauncher.exe'
        if (-not (Test-Path -LiteralPath $tp)) { continue }
        $tpFull = (Get-Item -LiteralPath $tp).FullName
        $busy += @(Get-Process -Name 'MultiLauncher' -ErrorAction SilentlyContinue |
                   Where-Object { try { $_.Path -eq $tpFull } catch { $false } })
    }
    if ($busy.Count -gt 0) {
        throw ('实跑目录里的外壳正在运行，先退出它再推送（PID ' + (($busy | ForEach-Object { $_.Id }) -join ', ') + '）')
    }
    Write-Host "[2/3] 推送到 $($Targets -join ', ')"
    foreach ($t in $Targets) {
        if (-not (Test-Path $t)) { New-Item -ItemType Directory -Path $t | Out-Null }
        Copy-Item -LiteralPath $exe -Destination (Join-Path $t 'MultiLauncher.exe') -Force
        # DEC-114：调试符号改成内嵌（csproj 的 DebugType=embedded），publish 不再产出旁边的 .pdb，
        # 所以这里也不再拷 —— 崩溃栈里的行号跟着 exe 走，总体积反而少 116 KB。
        $dst = Join-Path $t 'MultiLauncher.exe'
        $dstHash = (Get-FileHash $dst -Algorithm SHA256).Hash
        if ($dstHash -ne $srcHash) { throw "哈希不一致：$dst" }
        # 旧名产物清掉：同一个壳只留一个 exe，免得双击到旧版（2026-10-02 更名后）
        foreach ($old in @('StrataLauncher.exe', 'StrataLauncher.pdb')) {
            $p = Join-Path $t $old
            if (Test-Path $p) { Remove-Item -LiteralPath $p -Force; Write-Host "      已移除旧名产物：$p" }
        }
        # DEC-114：符号改成内嵌之后，目标目录里那份旧 pdb 就没有归属了（行号已经在 exe 里），一并收掉
        $stalePdb = Join-Path $t 'MultiLauncher.pdb'
        if (Test-Path $stalePdb) { Remove-Item -LiteralPath $stalePdb -Force; Write-Host "      已移除内嵌后不再需要的符号文件：$stalePdb" }
        # DEC-097① + DEC-118：家底补种与归位。配置本体现在住 <目标>\data\config\<模块>\，不再躺 exe 同目录。
        # 两步都在推送脚本里做，因为壳自己那一步搬家在 --selftest 里是**关着的**（不许动他东西）——
        # 不然推送完立刻跑自检，界面会报"找不到 ninfer_models.ini"，那是假警报。
        # 规矩与壳内那一步一字不差：Move 不是复制（老位置不留第二份）、两处都有 = 都不动并点名。
        #
        # DEC-138（D-5）：这一趟只补一格 ——「种子与老位置各有两个名字」：仓库 config\ 里 llama 那一支实际只有旧名
        # router_models.ini（DEC-084 改名之前那份，而且 /config/ 整层不入库），以前只找新名 = 新装机的 llama 补种
        # 这一步一直落空。次序与壳内 EnsureRouterIniPresent 一致：**先新名、后旧名，落地的名字永远是新名**；
        # 目标根目录里躺着的那一份（不管新旧名）也先归位到新落点，免得补种出来的是第二份家底（同 D-4 那一族）。
        foreach ($trip in @(@('llama', 'llama_models.ini', 'router_models.ini'), @('ninfer', 'ninfer_models.ini', ''))) {
            $mod = $trip[0]; $name = $trip[1]; $legacy = $trip[2]
            $dstDir = Join-Path (Join-Path $t 'data\config') $mod
            $dp = Join-Path $dstDir $name
            $rootNames = @($name, $legacy) | Where-Object { $_ }
            foreach ($rn in $rootNames) {                     # ① 归位：目标根目录里那一份（新名或旧名）搬到新落点
                $src = Join-Path $t $rn
                if (-not (Test-Path -LiteralPath $src)) { continue }
                if (Test-Path -LiteralPath $dp) {
                    Write-Host "      两处都有一份（$src 与 $dp）：都不动（界面按 $dp 走），请自己定去留"
                } else {
                    New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
                    Move-Item -LiteralPath $src -Destination $dp
                    Write-Host "      已归位家底：$src -> $dp（Move 不是复制，老位置不留第二份）"
                }
            }
            if (-not (Test-Path -LiteralPath $dp)) {          # ② 缺才拷，已有就一个字不动
                $sp = $null
                foreach ($rn in $rootNames) {
                    $cand = Join-Path $root ('config\' + $rn)
                    if (Test-Path -LiteralPath $cand) { $sp = $cand; break }
                }
                if (-not $sp) { Write-Host "      缺 $name：仓库 config\ 里两种名字都没有种子（新名 / 旧名），跳过这一步"; continue }
                New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
                Copy-Item -LiteralPath $sp -Destination $dp
                Write-Host "      已补种家底：$dp（副本取自 $sp，源文件一个字没动）"
            }
        }
        Write-Host "      已推送并核对一致：$dst"
    }
    # DEC-138（E-10 / 缺口 10）：推送完在**实跑目录**跑一次 --selftest，并把退出码吃进来。
    # 以前"基线 N 条全绿"这件事只活在文档里，而 DEC-126/BUG-052 那条已经 sixth 次应验：
    # 开发目录绿 != 他机器上绿（产物缺家底、DPI 不同、他那份 ini 里开着什么，都不一样）。
    # 报告落在 shots-m1\（登记过不入库的那格），末行与 TOTAL 那行回显到控制台，红就 throw。
    if (-not $NoSelfTest) {
        Write-Host '[3/3] 在实跑目录跑 --selftest（BUG-052 那条纪律：推完必须在这里再跑一次）'
        foreach ($t in $Targets) {
            $stDir = Join-Path $root 'shots-m1'
            if (-not (Test-Path $stDir)) { New-Item -ItemType Directory -Path $stDir | Out-Null }
            $st = Join-Path $stDir ('st-deploy-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' +
                                    (Split-Path $t -Leaf) + '.txt')
            # 2026-10-22（他报「自检经常弹『无法验证发布者』」）：`Start-Process` 走的是 ShellExecute，
            # 未签名的 exe 在某些机器上会被 shell 弹一次安全警告（文件本身没有 Zone.Identifier）。
            # 改成调用运算符（直接 CreateProcess，不经过 shell）+ 自己等它退出：不弹框，语义一字不变。
            $exePath = Join-Path $t 'MultiLauncher.exe'
            & $exePath --selftest $st
            # `&` 对 GUI 程序不阻塞：先等报告文件出现，再等进程退出。
            # 退出码取不到（不是本进程 Start 的实例，`Process.ExitCode` 读出来是空）—— 所以判定走**报告**：
            # ALL PASS 且没有 FAIL 行。项目里报告本来就是判据（BUG-052 那条纪律吃的也是它）。
            # 2026-10-23（审查 E 条）：等报告那一圈从 30 秒放宽到 120 秒。自检实测 37 秒（联网那一条 10 秒档
            # + 界面布局 13 秒 + 版本引擎更新 14 秒），原来那 30 秒已经被压在脚下；一旦机器更慢，
            # 或下面 `Get-Process` 因为读不到 Path 被筛空（那时就不等退出了），就会在一个本该全绿的自检上报
            # 「自检没落报告」的假红。等得久一点不会误判，等不够才会。
            for ($i = 0; $i -lt 240 -and -not (Test-Path -LiteralPath $st); $i++) { Start-Sleep -Milliseconds 500 }
            $proc = @(Get-Process -Name 'MultiLauncher' -ErrorAction SilentlyContinue |
                      Where-Object { try { $_.Path -eq $exePath } catch { $false } })
            foreach ($pp in $proc) { try { $pp.WaitForExit() } catch { } }
            if (-not (Test-Path -LiteralPath $st)) { throw "自检没落报告：$st" }
            $lines = @(Get-Content -LiteralPath $st -Encoding UTF8)
            $tail = $lines | Where-Object { $_ -match '^(TOTAL|ALL PASS|FAIL |\d+ FAILURE)' } | Select-Object -Last 4
            $tail | ForEach-Object { Write-Host "      $_" }
            $fails = @($lines | Where-Object { $_ -match '^FAIL ' })
            $passed = @($lines | Where-Object { $_ -match '^ALL PASS' })
            if ($fails.Count -gt 0 -or $passed.Count -eq 0) {
                throw ("实跑目录的自检没过（FAIL " + $fails.Count + " 条、ALL PASS " + $passed.Count + " 条），报告：" + $st)
            }
            Write-Host "      实跑自检通过：$st"
        }
    } else {
        Write-Host '[3/3] -NoSelfTest：跳过推送后的实跑自检'
    }
}
finally { Pop-Location }
