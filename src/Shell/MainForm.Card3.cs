namespace MultiLauncher;

// 卡片3（模型 / 加载·卸载 / 清屏）+ 事件接线（脏状态循环与登记册）。
// 2026-10-15 从 MainForm.UiCards.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 卡片3：模型 / 加载·卸载 / 清屏 ----------------
    Control CardModel()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = CardPad * 2 + RowH,
            FillColor = UiTheme.Card,
            BorderColor = UiTheme.Border,
            Backdrop = UiTheme.Window,
            Margin = new Padding(0, 0, 0, RowGap),
        };
        _cboModel.Backdrop = UiTheme.Card;
        // 展开时**不**重扫（以前每次展开都扫一遍目录 + 读 GGUF 头，慢且刷一屏日志）；刷新点见服务/网关启停
        // 提示文案（用户 2026-10-04：废话太多）→ 只留三句；「已加载」那一枚的含义见 docs/.ai/参考-界面与参数对照.md 与 DEC-065
        Tip.Set(_cboModel, ModelDropTip);
        // 模型名 + 尾部估算占用 + 右侧「已加载」都长，列表宽度的地板留 260（其余下拉默认与控件同宽，DEC-078）
        _cboModel.MinDropWidth = 260;

        _btnModelAction.Backdrop = UiTheme.Card;
        _btnModelAction.Font = _fUi;
        _btnModelAction.Height = RowH;
        Tip.Set(_btnModelAction, "模型行只管模型：这一页有东西在跑就一直给「卸载」，点它卸的是实际在跑的那一件（不必先把下拉指过去，DEC-141）；「卸载」不停服务、不关网关，切件 = 先卸载再加载。llama 单体模式固定「重载」");
        _btnClearLog.Backdrop = UiTheme.Card;
        _btnClearLog.FillColor = UiTheme.Input;
        _btnClearLog.HoverColor = UiTheme.Border;
        _btnClearLog.TextColor = UiTheme.Text;
        _btnClearLog.Font = _fSmall;
        _btnClearLog.Height = RowH;
        _btnClearLog.Width = BtnW85("清屏");
        Tip.Set(_btnClearLog, "清空日志显示（不影响已写入的日志文件）");

        var lblModel = Lbl("模型", _fSmall, UiTheme.TextMuted, UiTheme.Card);
        card.Controls.Add(lblModel);
        card.Controls.Add(_cboModel);
        card.Controls.Add(_btnModelAction);
        card.Controls.Add(_btnClearLog);

        void Layout()
        {
            int y = CardPad;
            lblModel.Location = new Point(CardPad, y + UiTheme.S(this, 5));
            int x = CardPad + lblModel.Width + UiTheme.S(this, 8);
            int right = card.Width - CardPad;
            _btnClearLog.SetBounds(right - _btnClearLog.Width, y, _btnClearLog.Width, RowH);
            // 文案随选中后端与运行状态变：加载 / 卸载 / 重载 / 忙态（"加载中…"）。
            // 颜色：卸载红（原型 .btn.danger）、加载与重载绿；忙态深色表示进行中（点击会被忽略）
            var actionText = ModelActionText();
            _btnModelAction.Text = actionText;
            bool unload = actionText == "卸载";
            bool busy = actionText.EndsWith("…", StringComparison.Ordinal);
            _btnModelAction.FillColor = busy ? UiTheme.Input : unload ? UiTheme.Danger : UiTheme.Brand;
            _btnModelAction.HoverColor = busy ? UiTheme.Input : unload ? UiTheme.DangerHover : UiTheme.BrandHover;
            _btnModelAction.TextColor = busy ? UiTheme.TextMuted : unload ? Color.White : UiTheme.BrandOn;
            _btnModelAction.Width = BtnW(actionText);
            _btnModelAction.SetBounds(_btnClearLog.Left - RowGap - _btnModelAction.Width, y, _btnModelAction.Width, RowH);
            _cboModel.SetBounds(x, y, Math.Max(UiTheme.S(this, 120), _btnModelAction.Left - RowGap - x), RowH);
        }
        LayoutHook = Layout;
        card.Layout += (_, _) => Layout();
        Layout();
        return card;
    }

    Action? LayoutHook;

    // ---------------- 事件接线 ----------------
    // DEC-138（M6 缺口 3）：脏判定事件的登记册 —— 上面那六组循环每挂一枚记一笔。
    // 自检拿**界面上的控件树**对着它穷举：新加一格参数控件却忘了挂事件，当场红
    //（AGENTS 那句「新加主窗参数控件要挂进那两组循环」以前只能靠人记得，第一次有了机器判据 = DEC-098/100 那一族的根）。
    readonly HashSet<Control> _dirtyWired = new();
    internal IEnumerable<Control> DirtyWiredForTest => _dirtyWired;
    /// <summary>卡1 / 卡2 / 卡3 与高级区里所有"能当参数格子"的控件（三型：输入框 / 下拉 / 开关）。</summary>
    internal IEnumerable<Control> ParamControlsForTest()
    {
        foreach (var top in new Control?[] { _cardStart, _cardParams, _cardModel, _advHost })
            foreach (var c in DescendantsOf(top))
                if (c is RoundedTextBox or DarkSelect or ToggleSwitch) yield return c;
    }
    // 界面上确实在卡片里、但**按口径不参与脏判定**的件（自检的例外名单从这里取，别在两边各写一套）：
    //   _cboModel  = 模型下拉 = 导航件（选中的是那一件，不是参数；ParamFields 里标 Freeze=false 那一族）
    internal IReadOnlyList<Control> DirtyExemptForTest => new Control[] { _cboModel };
    static IEnumerable<Control> DescendantsOf(Control? parent)
    {
        if (parent is null) yield break;
        foreach (Control c in parent.Controls)
        {
            yield return c;
            foreach (var d in DescendantsOf(c)) yield return d;
        }
    }

    void WireEvents()
    {
        // 端口/监听地址改动时：API 地址栏、内部端口自动跟、状态卡（地址/端口两行）都要立刻反映（用户反馈：状态要跟随实际）
        _txtPort.TextChanged += (_, _) => { UpdateApiLabel(); SyncInternalPort(); RefreshRunInfo(); WarnPortClash(); };
        // llama 页「监听端口」（2026-10-02 从设置页挪出）与公开端口同款手感：改值即写回当前配置（随下次落盘持久化），
        // 状态卡/API 地址即时刷新（写回后 LlamaPortValue / WarnPortClash 读到的就是新值）
        _txtPortLlama.TextChanged += (_, _) =>
        {
            _config.Current.LlamaInternalPort = _txtPortLlama.Text.Trim();
            UpdateApiLabel();
            RefreshRunInfo();
            WarnPortClash();
        };
        _txtHost.TextChanged += (_, _) => { UpdateApiLabel(); RefreshRunInfo(); };
        // 「保存」按钮脏状态（2026-10-04 DEC-074）：主窗这批参数控件任一被**手动**改动 → 重算按钮；
        // 程序化推视图不走这些事件收口（它们各自走 MarkParamsSaved：ApplySettings / 切页 / 关窗 / 保存 / 设置页保存）。
        // DEC-138（M6 缺口 3）：下面这六组循环每挂一枚都登记进 `_dirtyWired` —— 自检按界面上的控件树穷举比对，
        // 于是 AGENTS 那句「新加主窗参数控件要挂进那两组循环」第一次有了机器判据（以前只能靠人记得）。
        foreach (var t in new[]
        {
            _txtHost, _txtPort, _txtPortLlama, _txtModelName, _txtContext, _txtExtra,
            _txtGpu, _txtLayerSplit, _txtSpec, _txtSpecMinP, _txtExpertCache, _txtPrefill, _txtVramReserve, _txtArenaPin,
            _txtTemperature, _txtTopP, _txtTopK, _txtMinP, _txtPresencePenalty, _txtRepeatPenalty, _txtSpecDraftNMax,
        })
        {
            t.TextChanged += (_, _) => RefreshSaveButton();
            _dirtyWired.Add(t);
        }
        foreach (var s in new[] { _cboKv, _cboKvLlama, _cboVision, _cboReasoning, _cboReasoningEffort, _cboSpecType, _cboMmproj })
        {
            s.SelectedIndexChanged += (_, _) => RefreshSaveButton();
            _dirtyWired.Add(s);
        }
        foreach (var s in new[] { _swKvRes, _swHeadLate, _swFlashAttn })
        {
            s.CheckedChanged += (_, _) => RefreshSaveButton();
            _dirtyWired.Add(s);
        }
        // DEC-096：Ninfer 这一页的格子同样挂进这两组循环（AGENTS 的硬口径：新加主窗参数控件必须挂进来，
        //  否则「保存」按钮对该页永远是灰的 = 用户改了没人告诉他还没落盘）。
        // DEC-107：投机方式 / 投机深度改成输入框（挂 TextChanged）；共享前缀上限整格去掉。
        foreach (var t in new[]
        {
            _txtPortNinfer, _txtNinferMaxTokens, _txtNinferTemperature, _txtNinferTopP, _txtNinferTopK, _txtNinferPresence,
            _txtNinferConcurrency, _txtNinferSlots, _txtNinferPending, _txtNinferPendingSec,
            _txtNinferPoolWsl, _txtNinferDraftWsl, _txtNinferPrefillWsl, _txtNinferSpec, _txtNinferDraft,
        })
        {
            t.TextChanged += (_, _) => RefreshSaveButton();
            _dirtyWired.Add(t);
        }
        foreach (var s in new[]
        {
            _cboNinferReasoning, _cboKvNinfer, _cboNinferPool, _cboNinferPrefill, _cboNinferHostKib,
        })
        {
            s.SelectedIndexChanged += (_, _) => RefreshSaveButton();
            _dirtyWired.Add(s);
        }
        foreach (var s in new[] { _swNinferLmHead, _swNinferVision, _swNinferPreserve })
        {
            s.CheckedChanged += (_, _) => RefreshSaveButton();
            _dirtyWired.Add(s);
        }
        // 端口那一格：与另两页同款手感（API 地址 / 状态卡即时跟），并且**失焦 = DEC-091 第一条收口**
        _txtPortNinfer.TextChanged += (_, _) => { UpdateApiLabel(); RefreshRunInfo(); WarnPortClash(); };
        _txtPortNinfer.Leave += (_, _) => SettleNinferPort("改完端口");
        // 卡1 左侧那枚后端方块：点一下 = 请求轮换（服务在跑先弹确认，取消就一个字都不动 —— DEC-087 四联动）
        // 它垫哪层色**不在这里设**：DEC-182 起它住在行容器 `_card1Row` 里，底色由 `Card1Row()` 按父层现值给
        //（原来这行写死卡片色，搬层之后就把圆角外垫出一圈方角 —— 自检那条「卡1 每一格垫的色 = 父层现值」抓的就是它）。
        _backendToggle.SwapRequested += OnNinferBackendSwap;
        // 模型下拉：切换选中 = 载入该模型自己的参数，并把选中记到激活模块名下（B 段：两个模块各记各的）
        _cboModel.SelectedIndexChanged += (_, _) =>
        {
            if (_applying) return;
            var tag = SelectedTag();
            if (tag.Length == 0) return;
            ActiveView.SetTag(_config.Current, tag);   // 第 0 轮 DEC-094：选中记到"本模块名下"那一格（各模块各记各的）
            LoadKnobsFromModel(tag);
            OnModelChanged();
            ShowAdvancedForActiveModule();
            RefreshStatusUi();      // 灯/PID/端口/启动按钮与模型行按钮文案都随激活模块走
        };
        _btnSave.Click += (_, _) => SaveParameterEdits();
        _btnAdv.Click += (_, _) => ToggleAdvanced();
        _btnClearLog.Click += (_, _) => ClearActiveLog();      // B 段：清屏按页（不动历史与文件）
        _btnModelAction.Click += (_, _) => ModelAction();
        // 页头模块分段（B 段）：点格切页（切页不动服务）
        _segModules.ValueChanged += (_, _) =>
        {
            var keys = EnabledModuleKeys();
            int i = _segModules.ActiveIndex;
            if (i >= 0 && i < keys.Count) SwitchModule(keys[i]);
        };
    }
}
