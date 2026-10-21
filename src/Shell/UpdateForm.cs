namespace MultiLauncher;

// 更新窗（DEC-058：用户批复「不同模块分开，各自弹」）：一个模块一个窗，托盘点「检查更新」或设置页点「更新…」都开它，
// 开窗即自动开查——治的是"主窗藏着时点检查更新完全没反馈"（结果以前只进日志与设置窗那行，窗没开就等于没落点）。
//
// 摆位口径（用户逐轮拍定，别再自行调整）：
//   · 三行各带分隔线；状态文字左边缘落在**值列**上，与上方「当前版本」同列；
//   · 按钮恒在最右侧，**每行一枚**，只换字不换位：
//       第一行（版本）末尾 = 跳过 / 取消跳过；
//       第二行（检查更新）末尾 = 查看更新内容（有新版本时）/ 检查更新（没有时）；
//       第三行末尾 = 更新。
//   · 进度条、小 X、阶段名**只在第三行**，不再跑到第二行去（同一个控件只有一个家）。
//   · 说明性文字一律不写（红框 2 那段按用户要求删了，README 里有）。
//
// 关窗不停流程：升级在半路时关掉窗，流程继续跑（进度与结果仍写日志），托盘那条读「升级中…」并置灰。
//
// DEC-107（用户：「之前说好是 2 个模块分开做，各自的弹窗」）：Ninfer 从 DEC-097③ 的"一窗两段"改回
// **一后端一窗**（设计稿支点 F 原本就写的这条，本轮按用户的把执行偏掉的改回来）——
// side="win" = 今天这套三行窗（Ninfer 那窗主段标题仍是「WIN 后端」，只是不再带第二段）；
// side="wsl" = 一扇小窗：标题「WSL 后端」+ 一行「版本｜值｜更新…」+（忙时）阶段行 + 关闭。
// Strata / llama 走的就是 side="win" 且没有第二段 = 形状与上一版一字不差。
internal sealed class UpdateForm : Form
{
    readonly MainForm _main;
    // DEC-202：以前这里钉死 `MainForm.ModuleView`（模块表那一行）。「本程序」是第 4 条链、不在模块表里，
    // 所以放宽成 IUpdatePane（窗真正用到的那几项）。ModuleView 显式实现它 → 现有三条链一字不变。
    readonly IUpdatePane _v;
    readonly string _side;                // "win" / "wsl"（DEC-107）
    readonly Label _grp, _verTitle, _ver, _actTitle, _status, _phase;
    readonly FlatButton _btnSkip, _btnDetail, _btnUpgrade, _btnClose;
    readonly ThinProgress _bar;
    readonly TinyX _x;
    readonly Panel _row1, _row2, _row3;
    readonly HairLine _h1, _h2, _h3;   // 行间一条、行间一条、页脚前一条（与设置窗/弹窗同一口径）
    // DEC-107：WSL 窗那一行（版本｜值｜更新…）。win 那一支这三位是 null = 形状与 DEC-097③ 之前的主段一字不差。
    readonly Label? _secVer;
    readonly FlatButton? _btnSec;
    // 意图值：窗没显示时 Control.Visible 恒 false（这个坑本项目一天踩过三次），自检与出图一律读这四个
    bool _skipShown, _detailShown, _upgradeShown, _barShown, _xShown;
    bool _memoShown;                    // DEC-110③：WSL 窗那一行闲时的结果回显出不出面（意图位，不读 Visible）
    Action? _refresh;                   // 挂进 MainForm 的刷新广播（关窗时摘掉）

    const int PadX = 18, LabelW = 92, RowH = 44, Gap = 10;

    // 旧签名保留（DEC-202）：现有三条链与自检那一批 `new UpdateForm(main, "strata")` 全走它，行为一字不变。
    // 键照模块表认（View(key) 认不出的键当场抛，不会"不认识的一律走 Strata 那条链"）。
    public UpdateForm(MainForm main, string key, string side = "win") : this(main, main.View(key), side) { }

    /// <summary>DEC-202：任何实现 <see cref="IUpdatePane"/> 的那一面都能开这扇窗（「本程序」= <see cref="AppUpdateCard"/>）。</summary>
    public UpdateForm(MainForm main, IUpdatePane pane, string side = "win")
    {
        _main = main;
        _side = side == "wsl" ? "wsl" : "win";
        _v = pane;
        var sec = _v.Secondary;
        if (_side == "wsl" && sec is null)
            throw new InvalidOperationException("这条模块表行没有第二段（WSL 后端），开不了 WSL 窗：" + _v.Key);
        // 标题带后端名（一模块两窗时窗口标题栏自己说清开的是哪一扇；单段模块 = 与上一版一字不差）
        Text = "更新 · " + _v.DisplayName + (_v.Secondary is not null
            ? (_side == "wsl" ? " · WSL 后端" : " · WIN 后端") : "");
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ShowInTaskbar = false;
        // 位置跟其他窗同一口径：弹窗族与设置窗都是 CenterParent（在父窗内部居中），不自己独树一帜。
        // 从托盘开（主窗藏着）时由 OpenUpdateWindow 按设置窗那条既有办法改 CenterScreen。
        StartPosition = FormStartPosition.CenterParent;
        // 标题栏统一走弹窗族那一串（DEC-058 首版漏了 Apply，拍出来是系统浅色标题栏；
        // DEC-063 起这一档 = 与设置窗同一口径：只开系统暗色、不上自定义色）
        CreateControl();
        UiTheme.CaptureDpi(Handle);
        UiDark.ApplyDialog(this);
        ClientSize = new Size(UiTheme.S(this, 560), UiTheme.S(this, 232));

        _grp = MkLabel(_side == "wsl" ? sec!.Title : (_v.PrimarySectionTitle ?? _v.DisplayName), 9.5f, UiTheme.Brand, bold: true);
        _row1 = MkRow(); _row2 = MkRow(); _row3 = MkRow();
        _h1 = new HairLine { Backdrop = UiTheme.Window, Height = 1 };
        _h2 = new HairLine { Backdrop = UiTheme.Window, Height = 1 };
        _h3 = new HairLine { Backdrop = UiTheme.Window, Height = 1 };

        _verTitle = MkLabel("版本", 9.5f, UiTheme.Text);
        _ver = MkLabel("", 9f, UiTheme.TextMuted);
        _btnSkip = Secondary("跳过");

        _actTitle = MkLabel("检查更新", 9.5f, UiTheme.Text);
        _status = MkLabel("", 9f, UiTheme.TextMuted);
        _btnDetail = Secondary("检查更新");

        _bar = new ThinProgress { BackColor = UiTheme.Window, Visible = false };
        _x = new TinyX { Backdrop = UiTheme.Window, Size = new Size(UiTheme.S(this, 20), UiTheme.S(this, 20)), Visible = false };
        _phase = MkLabel("", 8.5f, UiTheme.TextMuted);
        _btnUpgrade = new FlatButton
        {
            Text = "更新", Backdrop = UiTheme.Window, FillColor = UiTheme.Brand,
            HoverColor = UiTheme.BrandHover, TextColor = UiTheme.BrandOn,
            Font = new Font("Microsoft YaHei UI", 9f), Height = UiTheme.S(this, 30),
        };
        _btnClose = Secondary("关闭");
        _btnClose.Click += (_, _) => Close();

        if (_side == "wsl")
        {
            // DEC-107：WSL 窗 = 一行「版本｜值｜更新…」+（忙时）阶段行。
            // 硬口径照旧：值落在值列、按钮恒最右一枚、行间分隔线；结果与进度落在按钮字态 + toast/日志。
            // DEC-110③：那一行闲时改放**结果回显**（已是最新 / 检查失败 / 已升级…）—— 另三扇窗都有这一行，
            // 这条链以前只写日志与 toast。忙时仍是进度条 + 阶段名（同一行、同一个家，不并存）。
            _secVer = MkLabel("", 9f, UiTheme.TextMuted);
            _btnSec = Secondary(sec!.ActionLabel);
            _btnSec.Click += (_, _) => sec.OnAction();
            _row1.Controls.AddRange(new Control[] { _verTitle, _secVer, _btnSec });
            _row3.Controls.AddRange(new Control[] { _bar, _phase, _status });
            Controls.AddRange(new Control[] { _grp, _row1, _h1, _row3, _h3, _btnClose });
            Layout += (_, _) => LayoutRows();
            _refresh = RefreshUi;
            _main.AddUpdateUiRefresh(_refresh);
            FormClosed += (_, _) => { if (_refresh is not null) _main.RemoveUpdateUiRefresh(_refresh); };
            RefreshUi();
            sec.OnWindowOpened();       // 开 WSL 窗那一下顺手读该读的（只在启用那一侧、且没缓存时才进 VM；判据在 Ensure... 里）
            return;
        }

        _row1.Controls.AddRange(new Control[] { _verTitle, _ver, _btnSkip });
        _row2.Controls.AddRange(new Control[] { _actTitle, _status, _btnDetail });
        _row3.Controls.AddRange(new Control[] { _bar, _x, _phase, _btnUpgrade });
        // DEC-107：win 这一支不再拼第二段（Ninfer 的 WSL 那一支搬去自己的窗了）—— 三个控件控制台列表
        // 与 DEC-097③ 之前的主段一字不差，另两模块那一窗的形状因此原样不变。
        Controls.AddRange(new Control[] { _grp, _row1, _h1, _row2, _h2, _row3, _h3, _btnClose });

        // 语义全部沿用设置页那三枚（同一批 MainForm 出口，不另写一套流程）
        _btnSkip.Click += (_, _) => { if (_btnSkip.Text == "跳过") Skip(); else Unskip(); };
        _btnDetail.Click += (_, _) => { if (_btnDetail.Text == "检查更新") Check(); else Detail(); };
        _btnUpgrade.Click += (_, _) => Upgrade();
        _x.Click += (_, _) => { _x.Enabled = false; _v.RequestCancelUpdate(); };

        Layout += (_, _) => LayoutRows();
        _refresh = RefreshUi;
        _main.AddUpdateUiRefresh(_refresh);
        FormClosed += (_, _) => { if (_refresh is not null) _main.RemoveUpdateUiRefresh(_refresh); };
        RefreshUi();
    }

    // ---------------- 行工厂 ----------------

    static Label MkLabel(string text, float pt, Color color, bool bold = false) => new()
    {
        Text = text, ForeColor = color, AutoSize = true, BackColor = UiTheme.Window,
        Font = new Font("Microsoft YaHei UI", pt, bold ? FontStyle.Bold : FontStyle.Regular),
    };

    Panel MkRow() => new() { BackColor = UiTheme.Window };

    FlatButton Secondary(string text) => new()
    {
        Text = text, Backdrop = UiTheme.Window, FillColor = UiTheme.Input,
        HoverColor = Color.FromArgb(0x33, 0x33, 0x33), TextColor = UiTheme.Text,
        BorderColor = UiTheme.Border, Font = new Font("Microsoft YaHei UI", 9f),
        Height = UiTheme.S(this, 30),
    };

    int W(string text) => TextRenderer.MeasureText(text, _btnSkip.Font, new Size(int.MaxValue, int.MaxValue),
        TextFormatFlags.NoPadding).Width + UiTheme.S(this, 22);

    void LayoutRows()
    {
        int pad = UiTheme.S(this, PadX), lw = UiTheme.S(this, LabelW), gap = UiTheme.S(this, Gap);
        int rh = UiTheme.S(this, RowH), right = ClientSize.Width - pad;

        if (_side == "wsl") { LayoutWslRows(pad, lw, gap, rh, right); return; }

        // DEC-061：按钮宽度要在**定位之前**定好。以前放在末尾，「跳过」→「取消跳过」、「检查更新」→「查看更新内容」
        // 这种变宽的字，第一次摆位用的是旧宽度 → 右端超出内缩边距，要等下一次广播重排才自愈（状态文字的最大宽同理）。
        foreach (var b in new[] { _btnSkip, _btnDetail, _btnUpgrade, _btnClose }) b.Width = W(b.Text);

        _grp.Location = new Point(pad, UiTheme.S(this, 13));
        void PlaceRow(Panel row, int y, bool visible)
        {
            row.SetBounds(0, y, ClientSize.Width, rh);
            row.Visible = visible;
        }
        int y1 = _grp.Bottom + UiTheme.S(this, 6);
        PlaceRow(_row1, y1, true);
        _h1.SetBounds(pad, _row1.Bottom, ClientSize.Width - pad * 2, 1);
        PlaceRow(_row2, _h1.Bottom + UiTheme.S(this, 10), true);
        _h2.SetBounds(pad, _row2.Bottom, ClientSize.Width - pad * 2, 1);
        // 第三行收起时它上面那条分隔线也要收起，否则"检查更新"下面凭空多一道线 + 一段空白
        _h2.Visible = _row3.Visible;
        PlaceRow(_row3, _h2.Bottom + UiTheme.S(this, 10), _row3.Visible);

        // 第一行：值落在值列，按钮靠最右
        CenterV(_verTitle, _row1);
        _ver.MaximumSize = new Size(Math.Max(UiTheme.S(this, 120), right - (_verTitle.Left + lw) - W(_btnSkip.Text) - gap * 2), 0);
        _ver.Location = new Point(_verTitle.Left + lw, _verTitle.Top);
        _btnSkip.SetBounds(right - _btnSkip.Width, (_row1.Height - _btnSkip.Height) / 2, _btnSkip.Width, _btnSkip.Height);

        // 第二行：状态文字与上方当前版本**同列**，按钮靠最右
        CenterV(_actTitle, _row2);
        int stRight = right - (_detailShown ? _btnDetail.Width : 0) - gap;
        _status.MaximumSize = new Size(Math.Max(UiTheme.S(this, 100), stRight - (_verTitle.Left + lw)), 0);
        _status.Location = new Point(_verTitle.Left + lw, _actTitle.Top);
        _btnDetail.SetBounds(right - _btnDetail.Width, (_row2.Height - _btnDetail.Height) / 2, _btnDetail.Width, _btnDetail.Height);

        // 第三行：进度条 + 小 X + 阶段名靠左（同值列），更新按钮靠最右
        int bx = _verTitle.Left + lw;
        _bar.SetBounds(bx, _row3.Height / 2 - UiTheme.S(this, 3), UiTheme.S(this, 150), UiTheme.S(this, 6));
        _x.SetBounds(_bar.Right + UiTheme.S(this, 2), _row3.Height / 2 - UiTheme.S(this, 10), UiTheme.S(this, 20), UiTheme.S(this, 20));
        _phase.Location = new Point(_xShown ? _x.Right + UiTheme.S(this, 4) : _bar.Right + UiTheme.S(this, 10),
            (_row3.Height - _phase.Height) / 2);
        _btnUpgrade.SetBounds(right - _btnUpgrade.Width, (_row3.Height - _btnUpgrade.Height) / 2, _btnUpgrade.Width, _btnUpgrade.Height);

        // 第三行收起时（没查过 / 已是最新），页脚那条线要跟到第二行下面，别留一段空白
        // 第三行收起时不再画页脚那条线——第二行下面那条已经够了，画两条会挤成一道双线
        int footTop = _row3.Visible ? _row3.Bottom : _row2.Bottom + UiTheme.S(this, 10);
        // DEC-107：win 这一支不再有"第二段"（Ninfer 的 WSL 那一支有自己的窗了）—— 页脚直接跟第三行走。
        _h3.Visible = _row3.Visible;
        _h3.SetBounds(pad, footTop + UiTheme.S(this, 10), ClientSize.Width - pad * 2, 1);
        _btnClose.SetBounds(right - _btnClose.Width, _h3.Bottom + UiTheme.S(this, 10), _btnClose.Width, _btnClose.Height);
        ClientSize = new Size(ClientSize.Width, Math.Max(UiTheme.S(this, 180), _btnClose.Bottom + UiTheme.S(this, 14)));
    }

    /// <summary>WSL 窗的摆位（DEC-107）：一行「版本｜值｜更新…」+（忙时）阶段行 + 关闭。
    /// 硬口径与主窗同一套：值落在值列、按钮恒最右一枚、行间一条分隔线（摆位数出自同一个 lw/gap）。</summary>
    void LayoutWslRows(int pad, int lw, int gap, int rh, int right)
    {
        _btnSec!.Width = W(_btnSec.Text);
        _btnClose.Width = W(_btnClose.Text);
        _grp.Location = new Point(pad, UiTheme.S(this, 13));
        _row1.SetBounds(0, _grp.Bottom + UiTheme.S(this, 6), ClientSize.Width, rh);
        _row1.Visible = true;
        _h1.SetBounds(pad, _row1.Bottom, ClientSize.Width - pad * 2, 1);
        CenterV(_verTitle, _row1);
        _secVer!.MaximumSize = new Size(Math.Max(UiTheme.S(this, 120),
            right - (_verTitle.Left + lw) - _btnSec.Width - gap * 2), 0);
        _secVer.Location = new Point(_verTitle.Left + lw, _verTitle.Top);
        _btnSec.SetBounds(right - _btnSec.Width, (_row1.Height - _btnSec.Height) / 2, _btnSec.Width, _btnSec.Height);

        // 第三行：忙时 = 进度条 + 阶段名；闲时若有结果 = 那一行状态回显（DEC-110③，两者不并存，同一个家）。
        // 显隐判据 = "这一条链在忙" 或 "有一条查过的结果要交代"。
        _row3.Visible = _barShown || _memoShown;
        _row3.SetBounds(0, _h1.Bottom + UiTheme.S(this, 10), ClientSize.Width, rh);
        int bx = _verTitle.Left + lw;
        _bar.SetBounds(bx, _row3.Height / 2 - UiTheme.S(this, 3), UiTheme.S(this, 150), UiTheme.S(this, 6));
        _phase.Location = new Point(_bar.Right + UiTheme.S(this, 10), (_row3.Height - _phase.Height) / 2);
        // 状态文字左边缘落在值列上（与上方版本值同列 = 硬口径，同 win 窗第二行的状态行一个规矩）
        _status.MaximumSize = new Size(Math.Max(UiTheme.S(this, 100), right - bx), 0);
        _status.Location = new Point(bx, (_row3.Height - _status.Height) / 2);
        _status.Visible = _memoShown && !_barShown;
        _bar.Visible = _barShown;
        int footTop = _row3.Visible ? _row3.Bottom : _row1.Bottom;
        _h3.Visible = _row3.Visible;
        _h3.SetBounds(pad, footTop + UiTheme.S(this, 10), ClientSize.Width - pad * 2, 1);
        _btnClose.SetBounds(right - _btnClose.Width, _h3.Bottom + UiTheme.S(this, 10), _btnClose.Width, _btnClose.Height);
        ClientSize = new Size(ClientSize.Width, Math.Max(UiTheme.S(this, 120), _btnClose.Bottom + UiTheme.S(this, 14)));
    }

    void CenterV(Label l, Panel row) => l.Location = new Point(UiTheme.S(this, PadX), (row.Height - l.Height) / 2);

    // ---------------- 刷新（MainForm 各环节广播过来） ----------------

    internal void RefreshUi()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(RefreshUi)); } catch { } return; }
        var offer = _v.Offer();
        var (memo, memoColor) = _v.StatusMemo();
        bool busy = _v.UpdateBusy();                    // 两条链互斥共用一个忙态：按钮置灰认它（DEC-106）
        bool secBusy = _v.Secondary?.Busy?.Invoke() ?? false;              // WSL 那条链自己在忙（DEC-107 的判据位）

        if (_side == "wsl")
        {
            // DEC-107：WSL 窗只认自己那条链 —— 版本、按钮字态、忙时阶段行；结果落 toast/日志（DEC-106 口径不变）。
            // DEC-110③：改成都要落 —— 这一行闲时显示查出来的结果（已是最新 / 检查失败 / 已升级…），
            // 与另三扇窗的 memo 那一行同口径；忙时让位给进度条与阶段名。
            _secVer!.Text = _v.Secondary!.Version();
            var secText = _v.Secondary.ButtonText?.Invoke() ?? _v.Secondary.ActionLabel;
            if (_btnSec!.Text != secText) _btnSec.Text = secText;
            _btnSec.Enabled = !busy;                    // 两条链互斥：另一条在忙也点不动
            _barShown = secBusy;
            _bar.Fraction = -1;                          // WSL 链没有百分比，只走不确定态
            _bar.Visible = _barShown;
            _phase.Text = secBusy ? offer.Phase : "";    // 阶段名（"检查 WSL 更新（git fetch）…"/"重编 WSL 引擎（…）"）
            _phase.Visible = _phase.Text.Length > 0;
            var (wslMemo, wslMemoColor) = _v.Secondary.StatusMemo?.Invoke() ?? ("", UiTheme.TextMuted);
            _status.Text = wslMemo;
            _status.ForeColor = wslMemoColor;
            _memoShown = wslMemo.Length > 0;
            PerformLayout();
            return;
        }
        bool ownBusy = busy && !secBusy;                // win 窗：另一条链忙 = 这里一个字都不多写

        _ver.Text = _v.CurrentVersion();
        if (offer.Show)
        {
            // 有新版本（或被跳过那一版）：三行都在，第一枚读「跳过 / 取消跳过」，第二枚换成「查看更新内容」
            _btnSkip.Text = offer.SkipLabel; _skipShown = true;
            _btnDetail.Text = "查看更新内容"; _detailShown = true;
            _status.Text = ownBusy ? "升级到 " + offer.Version
                : (offer.SkipLabel == "跳过" ? "发现新版本 " + offer.Version : offer.Version + "（你跳过一次）");
            _status.ForeColor = UiTheme.Brand;
            _upgradeShown = true;
        }
        else
        {
            // 没查过 / 已是最新 / 失败：只有前两行，第二枚回到「检查更新」
            _skipShown = false;
            _btnDetail.Text = "检查更新"; _detailShown = true;
            _status.Text = memo; _status.ForeColor = memoColor;
            // 检查中还没有 offer，但第三行要出来放那条来回扫的进度条（进度条只有这一个家）
            _upgradeShown = false;
        }

        // 进度条与小 X 只认第三行；**只在本链自己在忙时**出面（DEC-107：另一条链忙 = 这里一个字都不多写）
        bool showBar = ownBusy;
        _barShown = showBar; _xShown = showBar && offer.CanCancel;
        _bar.Fraction = offer.Show ? offer.Fraction : -1;
        _x.Enabled = showBar && offer.CanCancel;
        _phase.Text = showBar && offer.Phase.Length > 0
            ? (offer.Fraction >= 0 ? offer.Phase + " " + (int)(offer.Fraction * 100) + "%" : offer.Phase)
            : (ownBusy && !offer.Show ? "正在查询最新发布…" : "");
        _phase.Visible = _phase.Text.Length > 0;
        // 显隐只在真正 Show 过之后按意图值落一次（LayoutRows 也读同一批意图值）
        _btnSkip.Visible = _skipShown; _btnDetail.Visible = _detailShown;
        _btnUpgrade.Visible = _upgradeShown; _bar.Visible = _barShown; _x.Visible = _xShown;
        _row3.Visible = _upgradeShown || _barShown;

        // 忙态：三枚原地灰置（摆位不变），只留「关闭」——两条链互斥，任一条忙都不能点出第二条
        _btnSkip.Enabled = _btnDetail.Enabled = _btnUpgrade.Enabled = !busy;
        _btnDetail.Enabled = !busy;
        PerformLayout();
    }

    // ---------------- 动作：全部转给模块表那一行（每一行再转回 MainForm 现成出口） ----------------

    void Check() => _v.CheckUpdateNow();
    void Detail() => _v.ShowUpdateDetail();
    void Upgrade() => _v.StartUpgrade();
    void Skip() => _v.SkipOffer();
    void Unskip() => _v.UnskipOffer();

    // 出图/自检用：当前三枚按钮的字与显隐（不读 Control.Visible——窗没显示时父链会把它压成 false）
    internal void SetCancelHoverForTest(bool on) { _x.HoverForTest = on; _x.Invalidate(); }
    internal FormStartPosition StartPositionForTest => StartPosition;
    internal bool ButtonsEnabledForTest() => _btnSkip.Enabled && _btnDetail.Enabled && _btnUpgrade.Enabled;
    internal string VersionTextForTest() => _ver.Text;
    /// <summary>DEC-107：本窗是不是 WSL 那扇（一后端一窗之后，窗的种类本身也是一条可断言的判据）。</summary>
    internal bool IsWslWindowForTest => _side == "wsl";
    /// <summary>DEC-097③：主段那行标题（Ninfer 是一模块两窗之一，必须自己报名开的是哪一侧）。</summary>
    internal string PrimaryTitleForTest => _grp.Text;
    /// <summary>DEC-107 自检/出图用：WSL 窗那行"标题｜版本｜按钮字"；win 窗返回空串（它没有第二段了）。</summary>
    internal string SecondaryForTest() =>
        _side != "wsl" || _secVer is null || _btnSec is null
            ? "" : _grp.Text + "｜" + _secVer.Text + "｜" + _btnSec.Text;
    /// <summary>DEC-110③：WSL 窗那一行结果回显的意图值（文本 + 出不出面 + 进度条在不在）。
    /// 一律读意图位不读 <c>Control.Visible</c>（窗没显示时那是含祖先的有效可见性 = 空判据，BUG-043 的教训）。</summary>
    internal string WslStatusForTest() => _side != "wsl" ? ""
        : _status.Text + "|" + (_memoShown ? 1 : 0) + "|" + (_barShown ? 1 : 0);
    /// <summary>DEC-106 自检用：WSL 窗那枚按钮当前可不可点（忙时置灰；灰的按钮 BM_CLICK 也点不动）。</summary>
    internal bool SecondaryEnabledForTest() => _btnSec?.Enabled ?? false;
    /// <summary>WSL 窗那一行的值有没有落在值列上（与「版本」标题按 LabelW 同列 = 硬口径，出图台量的那个数）。</summary>
    internal bool SecondaryAlignsForTest() =>
        _side == "wsl" && _secVer is not null
        && _secVer.Left == _verTitle.Left + UiTheme.S(this, LabelW);
    // DEC-061 自检用：四枚按钮的右边缘都要落在内缩边距之内（换字后不许超出；以前宽度在摆位之后才改，
    // 「跳过」→「取消跳过」这种变宽的那一帧会顶出右边距）
    internal bool ButtonsFitForTest()
    {
        int right = ClientSize.Width - UiTheme.S(this, PadX);
        foreach (var b in new Control?[] { _btnSkip, _btnDetail, _btnUpgrade, _btnClose, _btnSec })
            if (b is not null && b.Right > right) return false;
        return true;
    }
    internal string LayoutTextForTest() =>
        (_skipShown ? "[" + _btnSkip.Text + "]" : "[-]") +
        (_detailShown ? "[" + _btnDetail.Text + "]" : "[-]") +
        (_upgradeShown ? "[" + _btnUpgrade.Text + "]" : "[-]") +
        "|status=" + _status.Text + "|phase=" + _phase.Text + "|bar=" + (_barShown ? 1 : 0) + "|x=" + (_xShown ? 1 : 0);
}
