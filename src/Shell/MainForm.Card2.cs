namespace MultiLauncher;

// 卡片2（通用项：模型名/上下文 ／ KV 精度/附加参数）+ 右侧按钮列。
// 2026-10-15 从 MainForm.UiCards.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 卡片2：通用项（模型名/上下文 ／ KV 精度/附加参数）+ 右侧按钮列（打开网页 + 高级） ----------------
    // 2026-10-04 DEC-076 候选（原型）：地址/端口回卡1 后，本卡回到两行——第一行 模型名（吃余量）｜上下文；
    // 第二行 KV 精度｜附加参数（吃余量）。
    Control CardParams()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = CardPad * 2 + RowH * 2 + RowGap,
            FillColor = UiTheme.Card,
            BorderColor = UiTheme.Border,
            Backdrop = UiTheme.Window,
            Margin = new Padding(0, 0, 0, RowGap),
        };

        foreach (var b in new[] { _txtModelName, _txtContext, _txtExtra, _txtGpu })
        {
            b.Backdrop = UiTheme.Card;
            b.Inner.Font = _fMono;
        }
        _txtExtra.Inner.Font = _fSmall;
        _cboKvLlama.Backdrop = UiTheme.Card;   // llama 侧 KV 精度（DEC-079 起是可编辑的组合下拉，与 _cboKv 同格互斥）
        _cboKv.Backdrop = UiTheme.Card;        // KV 精度住卡2 通用行（B 段：由高级区移入）
        // （监听地址 / 监听端口 已回卡1，DEC-076 候选——本卡不再管它们的样式与提示）
        RefreshCommonTips();      // 通用几格的说明随激活模块切换，文案见下
        // 右侧按钮列（B 段）：「打开网页」在上、「高级」在下
        foreach (var b in new[] { _btnOpenWeb, _btnAdv })
        {
            b.Backdrop = UiTheme.Card;
            b.FillColor = UiTheme.Input;
            b.HoverColor = UiTheme.Border;
            b.BorderColor = UiTheme.Border;
            b.TextColor = UiTheme.Text;
            b.Font = _fSmall;
            b.Height = RowH;
        }
        Tip.Set(_btnOpenWeb, "用浏览器打开激活模块的网页（Strata = 网关/服务端口；llama = WebUI 内部端口）");
        Tip.Set(_btnAdv, "展开后随激活模块切换内容：Strata 一套项、llama.cpp 另一套项");

        BuildAdvancedItems();

        // KV 精度（Strata = 下拉 _cboKv；llama = 下拉 _cboKvLlama（DEC-079），同位置互斥可见）。
        // 2026-10-04 DEC-076 候选（原型）：卡2 两行——第一行 模型名｜上下文；第二行 KV 精度｜附加参数。
        // 宽度弹性且**两行都正好铺满**：模型名与附加参数各吃本行余量（33 字符长段名 / 长旗标串都放得下），
        // 上下文与 KV 按实值精确适配（样本进 GridFitForTest 断言）。
        var gridLabels = new List<Label>();
        var gridCtls = new List<Control>();
        foreach (var (label, ctl) in new (string, Control)[]
        {
            ("模型名称", _txtModelName), ("上下文", _txtContext),
            ("KV 精度", _cboKv), ("附加参数", _txtExtra),
        })
        {
            var lb = Lbl(label, _fSmall, UiTheme.TextMuted, UiTheme.Card);
            card.Controls.Add(lb);
            gridLabels.Add(lb);
            gridCtls.Add(ctl);
            ctl.BackColor = UiTheme.Card;
            card.Controls.Add(ctl);
        }
        card.Controls.Add(_cboKvLlama);       // 与 _cboKv 同格（Bounds 在 Layout 里对齐）
        _cboKvLlama.BackColor = UiTheme.Card;
        // DEC-096：同格那几只下拉**从模块表补齐**（第三个模块的 _cboKvNinfer 以前根本没进控件树 = 那一格空着）。
        // _cboKv 是 gridCtls[2] 本身、上面已加过；其余每一行的 KvBox 只要还没爹就补进来，摆位与互斥由 Layout 那段管。
        foreach (var v in _views)
        {
            var kv = v.KvBox();
            if (kv.Parent is not null) continue;
            card.Controls.Add(kv);
            kv.BackColor = UiTheme.Card;
        }
        var advLine = new HairLine { LineColor = UiTheme.Border, Backdrop = UiTheme.Card, Visible = false };
        card.Controls.Add(advLine);
        card.Controls.Add(_btnOpenWeb);
        card.Controls.Add(_btnAdv);
        _advHost.BackColor = UiTheme.Card;
        card.Controls.Add(_advHost);
        _advHost.Visible = false;

        void Layout()
        {
            int content = Math.Max(UiTheme.S(this, 320), card.Width - CardPad * 2);
            int sideW = UiTheme.S(this, 76);     // 侧列 86→76（DEC-074 腾宽用）：「打开网页」4 字 8.5pt 约占 70，76 够用
            int gap = UiTheme.S(this, 12);
            int colGap = UiTheme.S(this, 12);
            int gridW = Math.Max(UiTheme.S(this, 240), content - sideW - gap);
            int Need(string t, Font f) => TextRenderer.MeasureText(t, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            int ColOf(string label, int netW) => LabelW(label) + UiTheme.S(this, 8) + netW + UiTheme.S(this, 16);
            // 弹性配宽（DEC-076 候选）：上下文 / KV 按实值精确适配，模型名与附加参数各吃本行余量——
            // 两行都**正好铺满** gridW；样本按实值：262144 / int8 / 33 字符长段名 / 带空格的长旗标串。
            // 上下文 +10%（用户 2026-10-04 第④条）：加在**净宽**上，标签与内边不动；模型名照旧吃本行余量
            int wCtx = ColOf("上下文", (int)Math.Round((Need("262144", _fMono) + UiTheme.S(this, 8)) * 1.1));
            // KV 精度：净宽要兜住**这一页最长的那个取值** —— 样本由模块那一行给（DEC-079 定的是 llama 页的
            // 「q8_0 / q8_0」= 11 字符；Ninfer 页最长是 rk4v4-e8，白占 18 逻辑就没地方放 22 字的 model-id 了）。
            // 余量按出图实测给足：GDI 绘制带内部留白，只留 8px 会被省略号截断（sb9 那张图抓出来的）。
            // DEC-108（他批"KV 精度宽度也要一致"）：这一格的宽度三页**一字不差** —— 样本把两页最长的都量一遍
            // 取最大（llama 页的「q8_0 / q8_0」与 Ninfer 页的「rk4v4-e8」），别按当前页的样本算
            //（那样 Ninfer 页会宽出一截，切页跳格）。模型名称与附加参数吃各自那行的余量，跟着一起对齐。
            int kvNeed = Math.Max(Need("q8_0 / q8_0", ActiveView.KvBox().Font),
                                  Need("rk4v4-e8", ActiveView.KvBox().Font));
            int wKv = ColOf("KV 精度", Math.Max(UiTheme.S(this, 90), kvNeed + UiTheme.S(this, 40)));
            // DEC-107：三页统一两行四格（Ninfer 从"一行三格"改回与另两页同形）——「模型名称」与「上下文」
            // 的宽度算式从此三页一字不差（他批的"使前面 2 个参数局部与其他页完全一致"），附加参数也归 Ninfer 了。
            int wModel = Math.Max(UiTheme.S(this, 160), gridW - colGap - wCtx);        // 吃第一行余量
            int wExtra = Math.Max(UiTheme.S(this, 160), gridW - colGap - wKv);         // 吃第二行余量
            int Place(int x, int y, int li, int w)
            {
                gridLabels[li].Location = new Point(x, y + UiTheme.S(this, 5));
                int ix = x + gridLabels[li].Width + UiTheme.S(this, 8);
                gridCtls[li].SetBounds(ix, y, Math.Max(UiTheme.S(this, 40), x + w - ix), RowH);
                return x + w + colGap;
            }
            int y1 = CardPad;
            int x = Place(CardPad, y1, 0, wModel);
            Place(x, y1, 1, wCtx);
            int y2 = CardPad + RowH + RowGap;
            gridLabels[3].Visible = true;
            gridCtls[3].Visible = true;
            x = Place(CardPad, y2, 2, wKv);
            Place(x, y2, 3, wExtra);
            // KV 格：激活模块那只摆进格位，其余模块的挪出画外（不进 dump、不参与命中）
            // 第 0 轮 DEC-094：这里遍历模块表，加第三个模块不用再改这段
            // kvSlot 必须在 parking **之前**取（_cboKv 自己就是那一格的占位控件，parking 之后它的 Bounds 是 -2000，
            // 再拿它当格位就会把激活那只一起挪出画外 —— DEC-096 出图抓到：Ninfer 页那一格空着）
            var kvSlot = _cboKv.Bounds;
            foreach (var v in _views)
                if (ReferenceEquals(v, ActiveView)) v.KvBox().SetBounds(kvSlot.X, kvSlot.Y, kvSlot.Width, RowH);
                else v.KvBox().SetBounds(-2000, -2000, kvSlot.Width, RowH);

            // 右侧按钮列：「打开网页」在 Ninfer 那一族不放（引擎没有网页界面）—— DEC-107 起**那一格留空占位**：
            // 只把按钮藏起来，「高级」仍在下面那一格（不占位的话 Ninfer 页两枚按钮整体上浮、与另两页对不齐）。
            if (ActiveView.WebButtonShown) { _btnOpenWeb.Visible = true; _btnOpenWeb.SetBounds(CardPad + content - sideW, CardPad, sideW, RowH); }
            else { _btnOpenWeb.Visible = false; _btnOpenWeb.SetBounds(-2000, -2000, sideW, RowH); }
            _btnAdv.SetBounds(CardPad + content - sideW, CardPad + RowH + UiTheme.S(this, 8), sideW, RowH);

            int rowsBottom = CardPad + RowH * 2 + RowGap;
            int advH = _advShown ? AdvHeight() : 0;
            _advHost.SetBounds(CardPad, rowsBottom + RowGap, content, advH);
            advLine.SetBounds(CardPad, rowsBottom + RowGap / 2, content, 1);
            advLine.Visible = _advShown;
            card.Height = rowsBottom + (_advShown ? advH + RowGap : 0) + CardPad;
        }
        HeightChanged = Layout;
        card.Layout += (_, _) => Layout();
        Layout();
        return card;
    }

    int AdvHeight() => AdvPanelVisible().Height;

    // 高级区的一套项 = 一个模块一份（DEC-096：第三个模块进来之后不能再按 `host == _advStrata ? 甲 : 乙` 分岔）
    (List<AdvItem> Items, List<(string Label, ToggleSwitch Sw, string Tip, Func<bool>? Shown)> Toggles) AdvSet(Panel host) =>
        ReferenceEquals(host, _advStrata) ? (_advStrataItems, _advStrataToggles)
        : ReferenceEquals(host, _advLlama) ? (_advLlamaItems, _advLlamaToggles)
        : ReferenceEquals(host, _advNinfer) ? (_advNinferItems, _advNinferToggles)
        : throw new InvalidOperationException("这块高级面板不在模块的名单里（加模块时忘了补一套项）");

    // 第 0 轮 DEC-094：这里不再问"是不是 llama 后端"，高级区那一套由模块表给
    Control AdvPanelVisible() => ActiveView.AdvPanel();

    // 通用几格的说明跟随激活模块：实现搬进 MainForm.ModuleViews.cs 那张表（StrataTips / LlamaTips 成对），
    // 入口仍是 RefreshCommonTips()。DEC-079 起 llama 侧的模型名仍是只读展示（它是 ini 的段名 = 身份键），
    // 但上下文 / KV / 高级那批都**可编辑并写回该模型那一段**，文案由 RefreshLlamaInheritanceTips 统一负责。

    // 「继承要看得见」（DEC-079 / 交接文档 §2 第 5 条、§10.2 第 2 条）：值是从 `[*]`/文件头拿来的，
    // 就在 tooltip 里点明——用 tooltip 不用标签后缀，因为标签宽度被自检的列宽算式钉死（DEC-078），加后缀会动版式。
    // 这十三个格子的文案只有这一个家：每次推 llama 视图（换模型 / 切页 / 保存后）都重算一遍。
    // ini 键 ↔ 界面上那一格（tooltip 文案与自检都从这一张家来，别在两处各写一遍控件名）
    internal Control? LlamaCtlForIni(string key) => key switch
    {
        "ctx-size" => _txtContext,
        "cache-type-k" or "cache-type-v" => _cboKvLlama,
        "flash-attn" => _swFlashAttn,
        "temperature" => _txtTemperature,
        "top-p" => _txtTopP,
        "top-k" => _txtTopK,
        "min-p" => _txtMinP,
        "presence-penalty" => _txtPresencePenalty,
        "repeat-penalty" => _txtRepeatPenalty,
        "spec-draft-n-max" => _txtSpecDraftNMax,
        "spec-type" => _cboSpecType,
        "mmproj-offload" => _cboMmproj,
        "reasoning-effort" => _cboReasoningEffort,
        _ => null,
    };

    void RefreshLlamaInheritanceTips()
    {
        var tag = SelectedModel()?.Tag ?? _config.Current.LlamaModel;
        string Note(string key)
        {
            if (!_llamaInherited.Contains(key)) return "";
            var v = _llamaEffective.TryGetValue(key, out var e) ? e : "";
            return v.Length > 0
                ? $"当前值 {v} 是全局的（这一段没写），改了才写进 [{tag}]"
                : "这一段与全局都没写 = 用引擎默认";
        }
        void Set(string key, Control ctl, string baseTip)
        {
            var n = Note(key);
            Tip.Set(ctl, n.Length > 0 ? baseTip + "；" + n : baseTip);
        }
        // KV 那一格管两键：两键的继承状态一样才提（一个段里写、另一个靠全局 = 少见，就按"没继承"处理别瞎说）
        string KvNote() => _llamaInherited.Contains("cache-type-k") && _llamaInherited.Contains("cache-type-v")
            ? Note("cache-type-k") : "";
        void SetKv(Control ctl, string baseTip) { var n = KvNote(); Tip.Set(ctl, n.Length > 0 ? baseTip + "；" + n : baseTip); }

        const string W = "改了写进选中模型那一段（留空 = 从这一段删掉 = 跟随全局）";
        Tip.Set(_txtModelName, "llama.cpp 侧：ini 的段名就是模型名（客户端请求时填的 model）；只展示，不可编辑");
        Set("ctx-size", _txtContext, "llama.cpp 侧 = 这一段的 ctx-size；" + W);
        SetKv(_cboKvLlama, "llama.cpp 侧 = 这一段的 cache-type-k / cache-type-v（一项写两键）；" + W);
        Tip.Set(_txtExtra, "原样追加到 llama-server 命令行末尾（按空白切分），用于引擎/壳都未提供的 flag；留空即不追加、不碰 ini");
        Set("flash-attn", _swFlashAttn, "写进选中模型那一段的 flash-attn；量化 KV 要靠它才省显存");
        Set("temperature", _txtTemperature, "写进这一段（留空 = 删掉这个键）");
        Set("top-p", _txtTopP, "写进这一段（留空 = 删掉这个键）");
        Set("top-k", _txtTopK, "写进这一段（留空 = 删掉这个键）");
        Set("min-p", _txtMinP, "写进这一段（留空 = 删掉这个键）");
        Set("presence-penalty", _txtPresencePenalty, "写进这一段（留空 = 删掉这个键）");
        Set("repeat-penalty", _txtRepeatPenalty, "写进这一段（留空 = 删掉这个键）");
        Set("spec-draft-n-max", _txtSpecDraftNMax, "写进这一段（留空 = 删掉这个键）");
        Set("spec-type", _cboSpecType, "写进这一段（空白 = 删掉这个键）");
        // DEC-145：「视觉」照 Strata 那格的口径 —— 这一件没配 mmproj 路径就置灰（_llamaHasMmproj 在 ApplyLlamaRecord 收口）
        _cboMmproj.Enabled = _llamaHasMmproj;
        Set("mmproj-offload", _cboMmproj, _llamaHasMmproj
            ? "写进这一段：GPU 提速 / CPU 省显存（空白 = 删掉这个键）"
            : "这一件没配 mmproj（段里没有 mmproj 路径）——放置项不可用");
        Set("reasoning-effort", _cboReasoningEffort, "写进这一段（空白 = 删掉这个键）");
        // 上面 AddAdv 里那批"留空 = …"的初始文案到这里被覆盖成段级口径 —— 唯一家就是本函数（llama 页）
    }

    // 自检用：高级区里全部圆角输入框的背景色必须是卡片色——Backdrop 默认是窗口色（深一档），
    // 圆角外露的一圈会变成"黑角"（用户反馈 2026-10-01：高级展开后圆角输入框外面有黑色边角）
    internal List<Color> AdvancedInputBackdropsForTest()
    {
        var list = new List<Color>();
        foreach (var it in _advStrataItems.Concat(_advLlamaItems).Concat(_advNinferItems))
            if (it.Ctl is RoundedTextBox tb) list.Add(tb.Backdrop);
        return list;
    }

}
