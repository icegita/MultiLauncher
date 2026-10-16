namespace MultiLauncher;

// 自检用：把新界面的排布导成文本（左栏顺序、三张卡片顺序、两套高级项、设置窗四个 Tab），
// 只建控件树、不开窗口、不占单实例互斥体。
// 存在的意义：界面顺序是用户反复指定的，写成断言后以后改代码不会悄悄挪错。
public partial class MainForm
{
    internal List<string> DumpLayoutRows()
    {
        var rows = new List<string>();

        // 左栏（B 段：只剩启停一个动作按钮 + 运行状态卡 + 模式条 + API 地址 + 底部「模块状态」）
        rows.Add("left-nav: " + _btnStartStop.Text);
        rows.Add("left-kv: " + string.Join(" | ", _kv.Keys));
        // 2026-10-04 DEC-076 候选：卡1 回归（监听地址｜监听端口｜思考深度｜保存），页头回到两枚按钮
        rows.Add("pagehead: " + string.Join(" | ", _segModules.ItemsForTest) + " | 打开配置 | 设置");
        rows.Add("left-col: " + DumpChildren(_sideCol!));
        rows.Add("card1: " + DumpChildren(_cardStart!));
        rows.Add("card2: " + DumpChildren(_cardParams!));
        rows.Add("card3: " + DumpChildren(_cardModel!));
        rows.Add("adv-strata: " + DumpChildren(_advStrata));
        rows.Add("adv-llama: " + DumpChildren(_advLlama));
        // 第 0 轮 DEC-094：这里报的是"当前激活模块的键"（模块表那一行），不再是一个 bool
        rows.Add("adv-backend: " + ActiveModuleKey);
        rows.Add("lights: " + string.Join(" | ", _lightRows.Select(r => r.Key)));
        // ④ 底部状态栏（DEC-038）：文本 + 是否在场 + 高度（自检据此断"只在服务在跑时出现"和 DPI 换算）
        rows.Add("statusbar: " + (StatusBarShownForTest ? "显示" : "隐藏") +
                 " | h=" + StatusBarHeightForTest + " | " + StatusBarTextForTest);
        return rows;
    }

    // 按视觉位置（上 -> 下、左 -> 右）列出直接子控件的文本。
    // 排序用「行带」而不是裸 Top：同一行里标签比输入框低几像素，裸 Top 会把标签排到输入框后面。
    // B 段：布局会把"该模块不参与"的控件挪出画外（Left 很负），这些不进 dump。
    internal static string DumpChildren(Control parent)
    {
        int band = Math.Max(8, parent.Font.Height);
        return string.Join(" | ", parent.Controls.Cast<Control>()
            .Where(c => c.Left > -1000)
            .OrderBy(c => (int)Math.Round((double)c.Top / band)).ThenBy(c => c.Left)
            .Select(TextOf).Where(t => t.Length > 0));
    }

    static string TextOf(Control c) => c switch
    {
        Label l => l.Text,
        FlatButton b => b.Text,
        StackedToggle t => t.ActiveText,       // DEC-090：左栏模式条换成叠放控件了，dump 里仍要说得出那句话
        DarkSelect => "下拉",
        RoundedTextBox => "文本框",
        ToggleSwitch => "开关",
        RoundedPanel or HairLine => "",
        // 普通 Panel 是分组容器：递归取里面的文字，让左栏的运行状态/标题块也能被断言
        _ => c.Controls.Count > 0
            ? string.Join("+", c.Controls.Cast<Control>().Select(TextOf).Where(s => s.Length > 0))
            : "",
    };

    // 自检用：把监听地址设成给定值，返回界面上「API 地址」框的内容（验证 0.0.0.0 -> 局域网 IP 的联动）
    internal string ApiAddressForTest(string host)
    {
        _txtHost.Text = host;
        UpdateApiLabel();
        return _txtApi.Text;
    }

    // ---- 自检用：本轮（第四轮）几条界面口径的可读出口 ----

    // 卡片之间的实际间距（原型 .main { gap: 12px }；靠 Dock=Top 的 spacer 撑开）。
    // 2026-10-04 DEC-076 候选：卡1 回归——量「卡1→卡2」与「卡2→卡3」两处。
    internal (int card1to2, int card2to3) CardGapsForTest()
    {
        int Gap(Control? below, Control? above) => below is not null && above is not null ? below.Top - above.Bottom : -1;
        return (Gap(_cardParams, _cardStart), Gap(_cardModel, _cardParams));
    }

    // 模型卡 → 日志卡的间距：提示行空着时应与其他卡片一致（用户反馈"中间空隙太大"）
    internal int ModelToLogGapForTest
        => _cardModel is not null && _cardLog is not null ? _cardLog.Top - _cardModel.Bottom : -1;

    // 自检用：日志卡高度与高级展开开关（用户反馈：高级展开不撑窗口、临时侵占日志区）
    internal int LogCardHeightForTest => _cardLog?.Height ?? -1;
    internal void ToggleAdvancedForTest() => ToggleAdvanced();

    // 左栏三个动作按钮文字靠左（原型 .navitem 无 text-align），「启动」是 .primary 仍居中
    // 左栏动作按钮（B 段起只剩「启动」；「打开网页」在参数卡右侧，「更新」在设置页）
    internal bool[] NavTextLeftForTest => new[] { _btnStartStop.TextLeft };

    // 设置按钮：矢量齿轮 + 纯文字（字体里的 ⚙ 会被当 emoji 字形画）
    internal (bool gear, string text) SettingsButtonForTest => (_btnSettings.ShowGear, _btnSettings.Text);

    // 2026-10-04（DEC-071）：「运行状态」大行合并进「状态」键值行——自检用出口（核对同一事实源 + 字体尺寸）
    internal (string Text, Color Color) ActiveStatusTextForTest => ActiveView.StatusText();
    internal (string Text, Color Color, float FontSize) RunStateCellForTest =>
        (_kv["状态"].Text, _kv["状态"].ForeColor, _kv["状态"].Font.Size);

    // 思考深度（2026-10-04）：界面 <-> Settings 往返出口（下拉标签即值）
    internal void SetReasoningForTest(string v) => SetReasoning(v);
    internal string ReasoningCollectedForTest => CollectSettings().ReasoningEffort;

    // 2026-10-04（DEC-074）：「保存」按钮脏状态与卡1 撤卡后的排布出口
    internal bool SaveButtonEnabledForTest => _btnSave.Enabled;
    internal void MarkParamsSavedForTest() => MarkParamsSaved();
    internal string ParamBaselineForTest => _paramBaseline;
    // 监听端口格 / 思考深度格此刻在卡1 里的是哪一只（随激活模块换，其余模块的挪出画外）。
    // 第 0 轮 DEC-094：按模块表问"谁在画内"，标签由各模块那一行给（Strata 那格是公开口 → "public"）。
    internal string PortSlotForTest
    {
        get { foreach (var v in _views) if (v.PortBox().Left > -1000) return v.PortSlotLabel; return "none"; }
    }
    internal string ReasoningSlotForTest
    {
        get { foreach (var v in _views) if (v.ReasonBox().Left > -1000) return v.ReasonSlotLabel; return "none"; }
    }
    // 两个思考深度下拉的选项表（断言"空选项在最前、能回空白"与 llama 的官方取值表）
    internal List<string> StrataReasoningOptionsForTest => _cboReasoning.Items.Select(i => i.Text).ToList();
    internal List<string> LlamaReasoningOptionsForTest => _cboReasoningEffort.Items.Select(i => i.Text).ToList();
    internal void SetLlamaReasoningForTest(string v) => SetLlamaReasoning(v);
    internal string LlamaReasoningCollectedForTest => CollectSettings().LlamaReasoningEffort;
    internal Rectangle PageHeadBoundsInClient() => ((Form)this).RectangleToClient(_pageHead!.RectangleToScreen(new Rectangle(Point.Empty, _pageHead.Size)));
    internal Rectangle CardStartBoundsInClient() => ((Form)this).RectangleToClient(_cardStart!.RectangleToScreen(new Rectangle(Point.Empty, _cardStart.Size)));
    // DEC-182 甲：出图要框的是**那一行**（方块卡 + 缝 + 卡1），只框卡1 会把他圈红框的那半张图裁掉
    internal Rectangle Card1RowBoundsInClient() => ((Form)this).RectangleToClient(_card1Row!.RectangleToScreen(new Rectangle(Point.Empty, _card1Row.Size)));
    /// <summary>自检用：卡1 上每一格（含住在行容器里的后端方块）"垫的那层色"对不对得上各自的父层。
    /// DEC-182 起因有两条：① `CardStart()` 的摆位循环只设了 `BackColor`，而它对 UserPaint 自绘控件无效
    /// → ninfer 那两只格子（监听端口 / 思考深度）圆角外一直垫着一圈窗口色的方角；② 后端方块从卡1 搬进
    /// 行容器后，父层由卡片色变成窗口色，不跟着换就反向垫出一圈。判据比的是**父层现值**，不写死色名。</summary>
    internal (string What, Color Got, Color Want)[] Card1HousingForTest()
    {
        var list = new List<(string What, Color Got, Color Want)>
        {
            ("监听地址", BoxBackdropOf(_txtHost), UiTheme.Card),
            ("保存", BoxBackdropOf(_btnSave), UiTheme.Card),
            ("后端方块（住行容器）", BoxBackdropOf(_backendToggle), _card1Row?.BackColor ?? UiTheme.Window),
        };
        foreach (var v in _views)
        {
            list.Add((v.Key + " 端口", BoxBackdropOf(v.PortBox()), UiTheme.Card));
            list.Add((v.Key + " 思考深度", BoxBackdropOf((Control)v.ReasonBox()), UiTheme.Card));
        }
        return list.ToArray();
    }
    internal Rectangle CardParamsBoundsInClient() => ((Form)this).RectangleToClient(_cardParams!.RectangleToScreen(new Rectangle(Point.Empty, _cardParams.Size)));
    // DEC-138（M6 缺口 11）：`HintRowShownForTest` 这个接缝**删掉了** —— 全仓没有任何调用点，而它读的还是 `Visible`
    //（无头恒 false = BUG-043 那一族）。"没人调用的判据接缝"本身就是隐患：下一次有人拿它当判据，测到的是空气。
    // 提示行在不在场这件事由布局那条（模型卡→日志卡间距）直接量几何，不需要这里旁证。
    // 卡2 三行「值放得下吗」：内层 TextBox 宽 vs 典型文本宽（同样的 NoPadding 量法，供自检与出图核对）。
    // 样本取实值/最坏值：地址 = 长 IP、端口 = 18082、上下文 = 262144、模型名 = 33 字符长段名（llama 页会显示）、
    // KV = llama 页的只读显示（q8_0 / q8_0）、附加参数 = 一条带空格的 flag。
    // DEC-078 补三只下拉：量的是收起框里文字实际可用的宽（TextRoomForTest，与绘制同一条算式）vs 最长取值。
    internal string GridFitForTest()
    {
        string Fit(RoundedTextBox b, string sample)
        {
            int need = TextRenderer.MeasureText(sample, b.Inner.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            return $"{b.Inner.Width}/{need}";
        }
        string Sel(DarkSelect s, string sample)
        {
            int need = TextRenderer.MeasureText(sample, s.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            return $"{s.TextRoomForTest()}/{need}";
        }
        // DEC-107：API 地址框的字号从 7.5pt 提到 8pt 之后，"保持能完整显示"也要量 —— 样本按最坏一档
        //（11 字符 IP + 五位端口；四位口那个样本还要更宽松，见 ApiFontCandidatesForTest 的候选表）
        int apiNeed = TextRenderer.MeasureText("http://192.168.1.9:18082/v1", _txtApi.Inner.Font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        return $"host={Fit(_txtHost, "255.255.255.255")} port={Fit(_txtPort, "18082")} ctx={Fit(_txtContext, "262144")}" +
               $" model={Fit(_txtModelName, "Qwen3.6-35B-A3B-MTP-APEX-I-Balanced")}" +
               $" extra={Fit(_txtExtra, "--flash-attn on --threads 16")}" +
               $" api={_txtApi.Inner.Width}/{apiNeed}" +
               $" kvSel={Sel(_cboKv, "k8v4")} kvLlamaSel={Sel(_cboKvLlama, "q8_0 / q8_0")}" +
               $" reasonSel={Sel(ActiveView.ReasonBox(), ActiveView.ReasonFitSample)}" +
               $" advSel={Sel(_cboVision, "关闭")}" +
               $" specSel={Sel(_cboSpecType, "draft-dspark")} mmprojSel={Sel(_cboMmproj, "GPU")}";
    }

    // 下拉的弹列表宽 vs 控件宽（DEC-078：菜单要与输入框对得上；模型下拉有 260 逻辑宽的地板，只断"不窄于控件"）。
    // 思考深度只量**当前页那只**——另一只可能从没摆过（Width=0，量出来是假数字）。KV 也是两只（DEC-079 起 llama 侧也是下拉）。
    internal string DropWidthsForTest() =>
        $"kv={_cboKv.PopupWidthForTest()}/{_cboKv.Width} kvLlama={_cboKvLlama.PopupWidthForTest()}/{_cboKvLlama.Width} " +
        $"reason={ActiveView.ReasonBox().PopupWidthForTest()}/{ActiveView.ReasonBox().Width}" +
        $" adv={_cboVision.PopupWidthForTest()}/{_cboVision.Width}" +
        $" spec={_cboSpecType.PopupWidthForTest()}/{_cboSpecType.Width} mmproj={_cboMmproj.PopupWidthForTest()}/{_cboMmproj.Width}" +
        $" model={_cboModel.PopupWidthForTest()}/{_cboModel.Width}";

    // 卡1 三格当前宽度（出报告用；"谁吃余量"看 Card1GrowSlotsForTest）
    internal (int host, int port, int reason) Card1WidthsForTest() => (_txtHost.Width, _txtPort.Width, _cboReasoning.Width);

    // 出图用（DEC-078）：把某只下拉真的展开，拍"弹列表宽度 vs 输入框宽度"——这两只窗是叠在上面的独立窗，
    // PrintWindow 拍不到合成结果，所以探针走屏幕取图（同 toast 场景图那条路）。
    DarkSelect? _dropProbe;
    internal void OpenDropDownForTest(string which)
    {
        _dropProbe = which switch
        {
            // KV 那一格每个模块各一只下拉：探针按当前页展开对应的那只（DEC-079 起 llama 页也是下拉）
            "kv" => ActiveView.KvBox(),
            "reason" => ActiveView.ReasonBox(),
            _ => _cboModel,
        };
        _dropProbe.OpenDropDownForTest();
    }
    internal Form? DropPopupForTest => _dropProbe?.DropForTest;
    /// <summary>出图诊断（DEC-107 补账：展开态五张一直拍不出来，要能说出是哪条判据挡的）。</summary>
    internal string DropProbeStateForTest()
    {
        if (_dropProbe is null) return "无探针";
        var chain = new List<string>();
        for (Control? c = _dropProbe; c is not null; c = c.Parent) chain.Add($"{c.GetType().Name}:{c.Visible}");
        return $"dropProbe Enabled={_dropProbe.Enabled} Visible={_dropProbe.Visible} 链={string.Join(" < ", chain)}";
    }
    internal Rectangle DropDownBoxBoundsScreenForTest() =>
        _dropProbe is null ? Rectangle.Empty : _dropProbe.RectangleToScreen(_dropProbe.ClientRectangle);
    internal void CloseDropDownForTest() { _dropProbe?.CloseDropDownForTest(); _dropProbe = null; }

    // 卡1 里"谁在吃余量"（DEC-078 第③条）：把卡临时加宽 extraLogical，只有吃余量那格会跟着变宽。
    // 先解除 Dock——停靠中的控件 Width 会被布局引擎按父宽顶回去（同 MinCardWidthForTest 的坑）。
    internal (int hostD, int portD, int reasonD, int cardW0, int cardW1) Card1GrowSlotsForTest(int extraLogical = 60)
    {
        var keep = _cardStart!.Bounds; var dock = _cardStart.Dock;
        var d = (0, 0, 0, 0, 0);
        _cardStart.Dock = DockStyle.None;
        try
        {
            // 解停靠会让 WinForms 把 Width 退回"用户宽"（默认 200），所以先把卡顶回真实宽度再取基线
            _cardStart.Width = keep.Width;
            _cardStart.PerformLayout();
            int w0 = _cardStart.Width, hw = _txtHost.Width, pw = _txtPort.Width, qw = _cboReasoning.Width;
            _cardStart.Width = w0 + UiTheme.S(this, extraLogical);
            _cardStart.PerformLayout();
            d = (_txtHost.Width - hw, _txtPort.Width - pw, _cboReasoning.Width - qw, w0, _cardStart.Width);
        }
        finally { _cardStart.Dock = dock; _cardStart.Bounds = keep; _cardStart.PerformLayout(); }
        return d;
    }

    // 附加参数（DEC-075）：llama 侧可编辑性与收集出口
    internal void SetExtraForTest(string v) => _txtExtra.Text = v;
    internal string ExtraCollectedForTest => CollectSettings().LlamaExtraArgs;
    internal bool ExtraReadOnlyForTest => _txtExtra.Inner.ReadOnly;

    // 出图用（--uiprobe sidebar）：高级区 / 左栏在客户区里的矩形，供 CaptureCrop 裁切
    internal Rectangle AdvHostBoundsInClient() => ((Form)this).RectangleToClient(_advHost.RectangleToScreen(new Rectangle(Point.Empty, _advHost.Size)));
    internal Rectangle SideColumnBoundsInClient() => ((Form)this).RectangleToClient(_sideCol!.Parent!.RectangleToScreen(new Rectangle(Point.Empty, _sideCol.Parent.Size)));

}