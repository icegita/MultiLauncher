using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 常驻网关的界面侧接线（网关本体在 Gateway.cs）：
//   勾「请求唤醒」= 壳监听公开端口、Strata 改跑内部端口；请求到达时按 model 唤醒或换模型；
//   「空闲停服(分钟)」填了就在那么久没有请求之后停掉服务释放显存（网关不动，下次请求再唤醒）。
// 上游的拉起/停止/映射都留在这半边：Gateway 只做转发，不知道界面也不知道 Strata。
public partial class MainForm
{
    // 第 0 轮（DEC-094）：壳网关从**单例**改成「一模块一份」。
    // 网关本体（Gateway.cs）只吃委托、本来就不认识 Strata；界面这一侧也不再写"上游就是 Strata 服务"那套：
    // 拉起 / 停止 / 取模型 / 取内部端口这些上游动作全从模块表那一行的 GatewayHooks 取（见 MainForm.ModuleViews.cs）。
    // 今天只有 Strata 挂了 hooks（llama.cpp 的"路由"由引擎自己的 --models-preset / models-max 实现），
    // 所以字典里最多一枚 —— 但**公开端口只有一个**：起任何一个模块的网关之前，先把别的模块那一份停掉。
    readonly Dictionary<string, Gateway> _gateways = new();
    internal Gateway? GatewayFor(string key) => _gateways.TryGetValue(key, out var g) ? g : null;
    /// Strata 自己那一份网关（Strata 侧的状态文案 / 端口行 / 启停按钮都问这个，别问"有没有任何网关在跑"）
    Gateway? StrataGateway => GatewayFor(ModelEntry.BackendStrata);
    /// 任何一模块的网关（公开端口只有一个，所以最多一枚在听）——端口避让与"这口是网关的"那类校验用它
    internal Gateway? AnyGateway { get { foreach (var g in _gateways.Values) return g; return null; } }
    internal Gateway? ActiveGateway => GatewayFor(ActiveModuleKey);
    /// <summary>这个口是不是**某一枚在听的网关**占着的（是就返回那个口，0 = 没有）。
    /// DEC-137（A-3）：网关从单例变成"一模块一份"（DEC-100③ 起各守各的口可以并存）之后，
    /// `AnyGateway` 只按字典顺序报第一枚 —— 端口避让拿它当判据就会漏掉另一枚，挑出来的口正撞在别人身上。</summary>
    internal int GatewayPortOn(int port)
    {
        lock (_gateways) return _gateways.Values.Any(g => g.Port == port) ? port : 0;
    }

    TaskCompletionSource<bool>? _readyTcs;   // 服务打印「ready:」的信号，网关等它决定何时可以转发
    volatile string _runningTag = "";        // 当前服务进程跑的是哪个模型 tag
    string _autoInternalPort = "";           // 上一次自动填的内部端口：用户手改过之后就不再跟着公开端口走
    // DEC-061：唤醒退避**按模型各一份**。以前是一整份全局的：模型 A 起不来会把 5 秒内对模型 B 的请求一起挡掉，
    // 而且回吐的是 A 的失败原因（客户端看着像「B 也坏了」）。
    // 第 0 轮：键里带上模块名 —— 两个模块里同名的 tag 各退各的，互不牵连。
    sealed record WakeBackoff(long Ticks, string Error);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, WakeBackoff> _wakeBackoff = new();

    // DEC-061：唤醒单飞门。每个连接一个 task，两个首请求同时到就会各自走「没在跑 → WakeAsync → StartService」，
    // 第二个命中 StartService 的「服务已经在运行。」被当异常回给客户端（503）；更坏的一支是在「切模型」那条路上
    // 把前一个请求刚起好的服务再停一遍。同模型的并发请求应当一起等 ready（DEC-003 第 5 条），而不是各起各的。
    // 第 0 轮：**一模块一份门**（与跳过位/取消位各一份同一条理由：两条流程不许互相停掉对方的动作）。
    readonly Dictionary<string, SemaphoreSlim> _wakeGates = new();
    SemaphoreSlim WakeGateFor(string key)
    {
        lock (_wakeGates)
        {
            if (!_wakeGates.TryGetValue(key, out var g)) _wakeGates[key] = g = new SemaphoreSlim(1, 1);
            return g;
        }
    }

    // 退避查询：命中返回 true 并给出原因；过期顺手清掉
    bool WakeBackedOff(string moduleKey, string tag, out string error)
    {
        error = "";
        var k = moduleKey + "/" + tag;
        if (!_wakeBackoff.TryGetValue(k, out var b)) return false;
        if (DateTime.UtcNow.Ticks - b.Ticks >= TimeSpan.FromSeconds(5).Ticks) { _wakeBackoff.TryRemove(k, out _); return false; }
        error = b.Error;
        return true;
    }

    // ---------------- 跨线程小工具 ----------------

    // 在界面线程上取值（BeginInvoke：不阻塞界面，也不会死锁）
    Task<T> UiAsync<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { BeginInvoke(new Action(() => { try { tcs.TrySetResult(f()); } catch (Exception ex) { tcs.TrySetException(ex); } })); }
        catch (Exception ex) { tcs.TrySetException(ex); }
        return tcs.Task;
    }

    void UiPost(Action a)
    {
        try { BeginInvoke(a); } catch { /* 窗口已销毁 */ }
    }
}
