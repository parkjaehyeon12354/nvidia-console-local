// NVIDIA 코딩 콘솔 (로컬판) — 서버도 계정도 웹페이지도 없이 도는 Windows 프로그램.
//
// 화면은 WinForms 로 직접 그린다. 둥근 모서리·호버·눌림·플레이스홀더는 WinForms 가
// 주지 않으므로 Ui/RoundPanel/RoundButton 에서 직접 칠한다.
// NVIDIA 호출도 프로그램이 직접 하고(Nvidia.cs), 대화와 키는 사용자 폴더에 둔다(Store.cs).
//
//   NvidiaConsole.exe                      창을 연다
//   NvidiaConsole.exe --selftest           화면 없이 자체 점검만 하고 끝낸다 (빌드 후 확인용)
//   NvidiaConsole.exe --shot a.png [설정 [번호]]  창을 그림 한 장으로 떠서 끝낸다 (화면 확인용)
//
// --shot 은 화면을 캡처하지 않고 창이 스스로를 그린다. 다른 창이 앞에 있든 상관없고,
// 남의 화면이 찍힐 일도 없다.

using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace NvidiaConsole;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest")
        {
            Environment.Exit(SelfTest.Run(args.Length > 1 ? args[1] : "probe"));
            return;
        }
        ApplicationConfiguration.Initialize();
        if (args.Length > 1 && args[0] == "--shot")
        {
            Environment.Exit(Shot(args[1], args.Length > 2 && args[2] == "설정",
                args.Length > 3 && int.TryParse(args[3], out var n) ? n : 0));
            return;
        }
        Application.Run(new MainForm());
    }

    static int Shot(string path, bool settings, int section)
    {
        var form = new MainForm();
        form.Show();
        Settle();
        if (settings) { form.ShowSettings(section); Settle(); }

        // 설정이 열려 있으면 창 전체를 덮는 막이 곧 화면이다. 폼째로 뜨면 자식 그리는 순서가
        // 뒤집혀 막이 도로 가려지므로, 그때는 막을 그린다.
        var target = settings && form.Overlay is not null ? form.Overlay : (Control)form;
        using var bmp = new Bitmap(target.Width, target.Height);
        target.DrawToBitmap(bmp, new Rectangle(0, 0, target.Width, target.Height));
        bmp.Save(path);
        Console.WriteLine("저장: " + path + "  " + target.Width + "x" + target.Height);
        return 0;
    }

    static void Settle()
    {
        for (var i = 0; i < 25; i++) { Application.DoEvents(); Thread.Sleep(40); }
    }
}

// ── 색·글꼴·그리기 ──────────────────────────────────────────
// 회색은 전부 같은 계열(따뜻한 쪽)로 맞춘다. 찬 회색과 섞으면 화면이 지저분해진다.
static class Ui
{
    public static readonly Color Bg = Color.FromArgb(0x26, 0x26, 0x24);   // 대화 영역
    public static readonly Color Side = Color.FromArgb(0x1f, 0x1e, 0x1d);   // 왼쪽 기둥
    public static readonly Color Card = Color.FromArgb(0x30, 0x30, 0x2e);   // 입력칸·버튼
    public static readonly Color CardHi = Color.FromArgb(0x3c, 0x3b, 0x38);   // 호버
    public static readonly Color CardDn = Color.FromArgb(0x45, 0x44, 0x40);   // 눌림
    public static readonly Color Line = Color.FromArgb(0x3d, 0x3c, 0x39);
    public static readonly Color Fg = Color.FromArgb(0xf2, 0xf0, 0xe9);
    public static readonly Color UserFg = Color.FromArgb(0xd4, 0xd1, 0xc8);
    public static readonly Color Muted = Color.FromArgb(0x99, 0x96, 0x8e);
    public static readonly Color Accent = Color.FromArgb(0xd9, 0x77, 0x57);
    public static readonly Color AccentHi = Color.FromArgb(0xe3, 0x8b, 0x6d);
    public static readonly Color OnAccent = Color.FromArgb(0x26, 0x11, 0x07);
    public static readonly Color CodeBg = Color.FromArgb(0x1a, 0x19, 0x18);

    public static readonly Font Body = new("Malgun Gothic", 10F);
    public static readonly Font Meta = new("Malgun Gothic", 8.5F);
    public static readonly Font Strong = new("Malgun Gothic", 10F, FontStyle.Bold);
    public static readonly Font Big = new("Malgun Gothic", 15F, FontStyle.Bold);
    public static readonly Font Arrow = new("Malgun Gothic", 12F);
    public static readonly Font Mono = new("Consolas", 10F);

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Paint(Graphics g, Rectangle r, int radius, Color fill, Color border)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(r, radius);
        using var b = new SolidBrush(fill);
        g.FillPath(b, path);
        if (border.A > 0)
        {
            using var p = new Pen(border);
            g.DrawPath(p, path);
        }
    }
}

static class Native
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? sub, string? id);

    // 제목 표시줄이 밝으면 어두운 창 위에 흰 띠가 얹힌 꼴이 된다. 20 은 최신, 19 는 옛 빌드.
    public static void DarkTitleBar(IntPtr hwnd)
    {
        var on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    // 스크롤 막대도 어둡게 — 지원하지 않는 빌드면 아무 일도 일어나지 않는다
    public static void DarkScrollBars(Control c)
    {
        if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
    }
}

sealed class RoundPanel : Panel
{
    public int Radius = 12;
    public Color Border = Ui.Line;
    public Color Outer = Ui.Bg;   // 둥근 모서리 바깥으로 비치는 바탕

    // 바탕이 단색이 아니라 그림(흐린 화면)일 때, 모서리 밖으로 그 그림이 비쳐야 한다.
    // 부모와 같은 크기의 그림이라 내 위치만큼 잘라 그리면 정확히 이어진다.
    public Image? Backdrop;

    public RoundPanel(Color fill, Color outer)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = fill;
        Outer = outer;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Backdrop is not null)
            e.Graphics.DrawImage(Backdrop, new Rectangle(0, 0, Width, Height),
                new Rectangle(Left, Top, Width, Height), GraphicsUnit.Pixel);
        else
            e.Graphics.Clear(Outer);

        Ui.Paint(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Radius, BackColor, Border);
    }
}

// Button 을 상속해 키보드·포커스·Click 은 그대로 얻고 그리기만 가져온다.
sealed class RoundButton : Button
{
    public int Radius = 10;
    public Color Fill = Ui.Card;
    public Color Hover = Ui.CardHi;
    public Color Down = Ui.CardDn;
    public Color Border = Ui.Line;

    bool hot, pressed;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Ui.Fg;
        Font = Ui.Body;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Ui.Bg);

        var body = new Rectangle(0, 0, Width - 1, Height - 1);
        var fill = !Enabled ? Ui.Card : pressed ? Down : hot ? Hover : Fill;
        Ui.Paint(g, body, Radius, fill, Border);

        // 키보드로 온 포커스는 보여야 한다 — 마우스만 쓰는 사람 기준으로 지우면 접근성이 깨진다
        if (Focused)
        {
            using var ring = new Pen(Ui.Accent);
            using var path = Ui.Round(Rectangle.Inflate(body, -2, -2), Math.Max(Radius - 2, 2));
            g.DrawPath(ring, path);
        }

        var left = TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft;
        TextRenderer.DrawText(g, Text, Font,
            left ? new Rectangle(body.X + 14, body.Y, body.Width - 18, body.Height) : body,
            Enabled ? ForeColor : Ui.Muted,
            (left ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter)
            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// 모델 고르기는 ComboBox 대신 버튼+메뉴다. DropDownList 의 테두리와 드롭다운 단추는
// 시스템이 그려 색을 바꿀 수 없어, 어두운 화면에서 그 부분만 회색으로 튄다.
sealed class DarkMenu : ToolStripProfessionalRenderer
{
    public DarkMenu() : base(new Palette()) { }

    sealed class Palette : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Ui.Card;
        public override Color MenuItemSelected => Ui.CardHi;
        public override Color MenuItemSelectedGradientBegin => Ui.CardHi;
        public override Color MenuItemSelectedGradientEnd => Ui.CardHi;
        public override Color MenuItemBorder => Ui.CardHi;
        public override Color MenuBorder => Ui.Line;
        public override Color ImageMarginGradientBegin => Ui.Card;
        public override Color ImageMarginGradientMiddle => Ui.Card;
        public override Color ImageMarginGradientEnd => Ui.Card;
    }
}

// 시스템이 그린 네이티브 컨트롤 위에 덧칠하는 자리. 화면은 WM_PAINT 로, 화면 없는
// 캡처(DrawToBitmap)는 WM_PRINTCLIENT 로 오므로 둘 다 받아야 같은 그림이 나온다.
static class Over
{
    const int WmPaint = 0x000F;
    const int WmPrint = 0x0317;
    const int WmPrintClient = 0x0318;

    public static void Draw(System.Windows.Forms.Message m, IntPtr handle, Action<Graphics> paint)
    {
        if (m.Msg == WmPaint)
        {
            using var g = Graphics.FromHwnd(handle);
            paint(g);
        }
        else if ((m.Msg == WmPrintClient || m.Msg == WmPrint) && m.WParam != IntPtr.Zero)
        {
            using var g = Graphics.FromHdc(m.WParam);
            paint(g);
        }
    }
}

// 대화 목록. 선택·호버를 둥근 알약으로 그리고, 가리킨 줄에만 × 를 띄운다.
sealed class ChatList : ListBox
{
    // 목록이 비면 DrawItem 이 한 번도 안 불려 검은 공백만 남는다
    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        base.WndProc(ref m);
        if (Items.Count == 0) Over.Draw(m, Handle, Empty);
    }

    void Empty(Graphics g) =>
        TextRenderer.DrawText(g, "아직 대화가 없습니다", Ui.Meta, new Rectangle(0, 16, Width, 20),
            Color.FromArgb(0x6b, 0x69, 0x63), TextFormatFlags.HorizontalCenter);

    public event EventHandler<int>? DeleteClicked;

    int hover = -1;
    const int XZone = 30;

    public ChatList()
    {
        DoubleBuffered = true;
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        ItemHeight = 36;
        BackColor = Ui.Side;
        ForeColor = Ui.Fg;
        Font = Ui.Body;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = IndexFromPoint(e.Location);
        if (i != hover) { hover = i; Invalidate(); }
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hover != -1) { hover = -1; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var i = IndexFromPoint(e.Location);
        // × 는 선택보다 먼저 가로챈다. base 를 부르면 삭제하면서 그 줄을 열어버린다.
        if (i >= 0 && e.Button == MouseButtons.Left && e.X >= Width - XZone)
        {
            DeleteClicked?.Invoke(this, i);
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && SelectedIndex >= 0) DeleteClicked?.Invoke(this, SelectedIndex);
        base.OnKeyDown(e);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var g = e.Graphics;
        using (var bg = new SolidBrush(Ui.Side)) g.FillRectangle(bg, e.Bounds);

        var picked = (e.State & DrawItemState.Selected) != 0;
        var over = e.Index == hover;
        var row = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);
        if (picked || over) Ui.Paint(g, row, 9, picked ? Ui.CardHi : Ui.Card, Color.Transparent);

        var text = new Rectangle(row.X + 10, row.Y, row.Width - (over ? XZone + 6 : 16), row.Height);
        TextRenderer.DrawText(g, Items[e.Index]?.ToString() ?? "", Ui.Body, text,
            picked ? Ui.Fg : Ui.UserFg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (over)
        {
            var x = new Rectangle(row.Right - XZone, row.Y, XZone, row.Height);
            TextRenderer.DrawText(g, "×", Ui.Body, x, Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

sealed class MainForm : Form
{
    readonly ChatList list = new();
    readonly RichTextBox view = new();
    readonly TextBox input = new();
    readonly RoundButton modelBtn = new();
    readonly ContextMenuStrip modelMenu = new();
    string model = "";
    string apiKey = "";
    readonly TextBox keyBox = new();
    readonly TextBox rootBox = new();
    readonly List<Panel> sections = new();
    readonly List<RoundButton> navs = new();
    Panel? scrim;
    readonly RoundButton updateBtn = new();
    readonly Label updateNote = new();
    RoundPanel? sheet;
    Bitmap? frost;
    Action center = () => { };
    readonly FlowLayoutPanel codeBar = new();
    readonly Label status = new();
    readonly Label heading = new();
    readonly RoundButton send = new();

    // 항목이 하나도 없으면 시스템이 흰 상자를 그린다. 안내 문구를 기본 항목으로 두면 색도 맞고 이유도 보인다.
    const string NoModel = "모델 없음 — 키를 넣으세요";

    List<Chat> chats = Store.Load();
    Chat current = new();
    CancellationTokenSource? streaming;
    bool syncing;   // 목록을 다시 채우는 중 — 선택 이벤트를 무시한다

    public MainForm()
    {
        Text = "NVIDIA 코딩 콘솔";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(1180, 820);
        MinimumSize = new Size(880, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Bg;
        ForeColor = Ui.Fg;
        Font = Ui.Body;

        KeyPreview = true;   // 설정이 열려 있을 때 Esc 를 폼이 먼저 본다
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) CloseSettings(); };

        // Fill 을 먼저 넣어야 남은 자리를 가져간다 (도킹은 뒤에 넣은 것부터 자리를 뗀다)
        Controls.Add(BuildMain());
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Ui.Line });
        Controls.Add(BuildSidebar());

        apiKey = Store.LoadKey();
        Nvidia.Root = Nvidia.Normalize(Store.LoadRoot());
        RefreshList();
        DrawChat();
        if (apiKey.Length > 0) LoadModels();
        else status.Text = "왼쪽 아래 설정에서 API 키를 넣으세요";

        Shown += async (_, _) => await CheckUpdate(false);   // 창이 뜬 뒤 조용히 살펴본다
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle);
        Native.DarkScrollBars(view);
        Native.DarkScrollBars(list);
    }

    // ── 화면 ────────────────────────────────────────────────
    Control BuildSidebar()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 268,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Ui.Side,
            Padding = new Padding(12, 14, 12, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var newChat = new RoundButton
        {
            Text = "＋   새 대화",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        newChat.Click += (_, _) => { list.ClearSelected(); current = new Chat(); DrawChat(); input.Focus(); };
        root.Controls.Add(newChat, 0, 0);

        list.Dock = DockStyle.Fill;
        list.Margin = new Padding(0, 0, 0, 12);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (syncing || list.SelectedIndex < 0 || list.SelectedIndex >= chats.Count) return;
            current = chats[list.SelectedIndex];
            DrawChat();
        };
        list.DeleteClicked += (_, i) => Delete(i);
        root.Controls.Add(list, 0, 1);

        var settings = new RoundButton
        {
            Text = "⚙   설정",
            Dock = DockStyle.Fill,
            Font = Ui.Meta,
            ForeColor = Ui.UserFg,
            Border = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        settings.Click += (_, _) => OpenSettings();
        root.Controls.Add(settings, 0, 2);

        return root;
    }

    Control BuildMain()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Ui.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));

        // 머리줄 — 어느 대화를 보고 있는지, 지금 무슨 일이 일어나는지
        var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Ui.Bg, Padding = new Padding(24, 0, 18, 0) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.Dock = DockStyle.Fill;
        heading.Font = Ui.Strong;
        heading.ForeColor = Ui.Fg;
        heading.TextAlign = ContentAlignment.MiddleLeft;
        heading.AutoEllipsis = true;
        head.Controls.Add(heading, 0, 0);

        // 새 버전을 받아 두면 나타난다. 누르면 끄고 바꿔치운 뒤 다시 켠다.
        updateBtn.Visible = false;
        updateBtn.AutoSize = false;
        updateBtn.Size = new Size(220, 28);
        updateBtn.Anchor = AnchorStyles.None;
        updateBtn.Font = Ui.Meta;
        updateBtn.Radius = 8;
        updateBtn.Fill = Ui.Accent;
        updateBtn.Hover = Ui.AccentHi;
        updateBtn.Down = Ui.Accent;
        updateBtn.Border = Color.Transparent;
        updateBtn.ForeColor = Ui.OnAccent;
        updateBtn.Click += (_, _) => Updater.InstallAndRestart();
        head.Controls.Add(updateBtn, 1, 0);

        status.AutoSize = false;
        status.Width = 320;
        status.Dock = DockStyle.Fill;
        status.Font = Ui.Meta;
        status.ForeColor = Ui.Muted;
        status.TextAlign = ContentAlignment.MiddleRight;
        head.Controls.Add(status, 2, 0);
        root.Controls.Add(head, 0, 0);

        root.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Ui.Line }, 0, 1);

        // 대화 — RichTextBox 에는 여백이 없으니 감싼 판에 준다
        var pad = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(24, 16, 14, 8) };
        view.Dock = DockStyle.Fill;
        view.ReadOnly = true;
        view.BackColor = Ui.Bg;
        view.ForeColor = Ui.Fg;
        view.BorderStyle = BorderStyle.None;
        view.Font = Ui.Body;
        view.DetectUrls = false;
        view.TabStop = false;
        pad.Controls.Add(view);
        root.Controls.Add(pad, 0, 2);

        codeBar.Dock = DockStyle.Fill;
        codeBar.AutoSize = true;
        codeBar.WrapContents = true;
        codeBar.BackColor = Ui.Bg;
        codeBar.Padding = new Padding(24, 0, 14, 2);
        root.Controls.Add(codeBar, 0, 3);

        // 입력 — 카드 하나 안에 입력칸·모델·보내기를 모은다
        var well = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(24, 6, 18, 16) };
        var card = new RoundPanel(Ui.Card, Ui.Bg) { Dock = DockStyle.Fill, Radius = 14, Padding = new Padding(14, 12, 12, 10) };

        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Ui.Card };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        input.Dock = DockStyle.Fill;
        input.Multiline = true;
        input.BackColor = Ui.Card;
        input.ForeColor = Ui.Fg;
        input.BorderStyle = BorderStyle.None;
        input.ScrollBars = ScrollBars.None;
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                Send();
            }
        };
        rows.Controls.Add(input, 0, 0);
        rows.SetColumnSpan(input, 3);

        modelMenu.Renderer = new DarkMenu();
        modelMenu.BackColor = Ui.Card;
        modelMenu.ForeColor = Ui.Fg;
        modelMenu.Font = Ui.Meta;
        modelMenu.ShowImageMargin = false;

        modelBtn.Text = NoModel;
        modelBtn.Font = Ui.Meta;
        modelBtn.Dock = DockStyle.Fill;
        modelBtn.Margin = new Padding(0, 4, 0, 0);
        modelBtn.Radius = 8;
        modelBtn.ForeColor = Ui.UserFg;
        modelBtn.Border = Color.Transparent;
        modelBtn.TextAlign = ContentAlignment.MiddleLeft;
        modelBtn.Click += (_, _) => modelMenu.Show(modelBtn, new Point(0, modelBtn.Height + 2));
        rows.Controls.Add(modelBtn, 0, 1);

        send.Text = "↑";
        send.Font = Ui.Arrow;
        send.Dock = DockStyle.Fill;
        send.Margin = new Padding(0, 2, 0, 0);
        send.Radius = 9;
        send.Fill = Ui.Accent;
        send.Hover = Ui.AccentHi;
        send.Down = Ui.Accent;
        send.Border = Color.Transparent;
        send.ForeColor = Ui.OnAccent;
        send.Click += (_, _) => Send();
        new ToolTip().SetToolTip(send, "보내기 (Enter)");
        rows.Controls.Add(send, 2, 1);

        card.Controls.Add(rows);
        well.Controls.Add(card);
        root.Controls.Add(well, 0, 4);
        return root;
    }

    RoundButton Chip(string text)
    {
        var b = new RoundButton
        {
            Text = text,
            Font = Ui.Meta,
            Height = 28,
            Radius = 8,
            Margin = new Padding(0, 0, 8, 6),
            ForeColor = Ui.UserFg,
        };
        b.Width = TextRenderer.MeasureText(text, Ui.Meta).Width + 26;
        return b;
    }

    // ── 대화 그리기 ─────────────────────────────────────────
    void Append(string text, Font font, Color fore, Color back)
    {
        view.SelectionStart = view.TextLength;
        view.SelectionLength = 0;
        view.SelectionFont = font;
        view.SelectionColor = fore;
        view.SelectionBackColor = back;
        view.AppendText(text);
    }

    void DrawChat()
    {
        view.Clear();
        view.SelectionAlignment = HorizontalAlignment.Left;
        heading.Text = current.Messages.Count == 0 ? "새 대화" : current.Title;

        if (current.Messages.Count == 0)
        {
            DrawWelcome();
            codeBar.Controls.Clear();
            return;
        }

        foreach (var m in current.Messages)
        {
            var mine = m.Role == "user";
            Append(mine ? "나\n" : "AI\n", Ui.Meta, mine ? Ui.Muted : Ui.Accent, Ui.Bg);
            if (mine)
            {
                Append(m.Content.Trim() + "\n\n", Ui.Body, Ui.UserFg, Ui.Bg);
                continue;
            }
            foreach (var b in Fences.Parse(m.Content))
            {
                if (b.IsCode)
                {
                    Append(Boxed(b.Text) + "\n", Ui.Mono, Ui.Fg, Ui.CodeBg);
                    Append("\n", Ui.Body, Ui.Fg, Ui.Bg);
                }
                else
                {
                    var t = b.Text.Trim();
                    if (t.Length > 0) Append(t + "\n\n", Ui.Body, Ui.Fg, Ui.Bg);
                }
            }
        }
        BuildCodeButtons();
        ScrollToEnd();
    }

    // 빈 화면은 검은 구멍으로 두지 않는다 — 다음에 뭘 해야 하는지 적어 둔다
    void DrawWelcome()
    {
        view.SelectionAlignment = HorizontalAlignment.Center;
        Append("\n\n\n\n", Ui.Body, Ui.Fg, Ui.Bg);
        Append("무엇을 만들어 볼까요?\n\n", Ui.Big, Ui.Fg, Ui.Bg);
        Append(apiKey.Length == 0
            ? "왼쪽 아래 설정에서 NVIDIA API 키를 넣으면 모델을 불러옵니다.\n"
            : "아래에 하고 싶은 걸 적고 Enter 를 누르세요.\n", Ui.Body, Ui.UserFg, Ui.Bg);
        Append("코드가 오면 복사 버튼이 생기고, HTML 이면 브라우저로 바로 열 수 있습니다.\n", Ui.Meta, Ui.Muted, Ui.Bg);
        view.SelectionAlignment = HorizontalAlignment.Left;
    }

    // RichTextBox 의 배경색은 글자 길이만큼만 칠해져 계단처럼 들쭉날쭉해진다.
    // 줄 끝을 공백으로 채워 네모로 보이게 한다 (고정폭 글꼴이라 어긋나지 않는다).
    static string Boxed(string code)
    {
        var lines = code.TrimEnd('\n').Replace("\t", "    ").Split('\n');
        var width = Math.Min(lines.Max(l => l.TrimEnd().Length) + 2, 120);
        return string.Join("\n", lines.Select(l => " " + l.TrimEnd().PadRight(width)));
    }

    void ScrollToEnd()
    {
        view.SelectionStart = view.TextLength;
        view.ScrollToCaret();
    }

    // 마지막 답변의 코드 블록마다 복사 버튼을 만든다. HTML 이면 브라우저로 열어볼 수도 있다.
    void BuildCodeButtons()
    {
        codeBar.Controls.Clear();
        var last = current.Messages.LastOrDefault(m => m.Role == "assistant");
        if (last is null) return;

        var n = 0;
        foreach (var b in Fences.Parse(last.Content).Where(b => b.IsCode))
        {
            n++;
            var code = b.Text;
            var label = "⧉  코드 " + n + (b.Lang.Length > 0 ? " · " + b.Lang : "");
            var copy = Chip(label);
            copy.Click += (_, _) =>
            {
                Clipboard.SetText(code);
                status.Text = label.Replace("⧉  ", "") + " 복사됨";
            };
            codeBar.Controls.Add(copy);

            if (b.Lang is "html" or "svg" || code.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                var open = Chip("▷  브라우저로 열기");
                open.Click += (_, _) => OpenInBrowser(code);
                codeBar.Controls.Add(open);
            }
        }
    }

    static void OpenInBrowser(string html)
    {
        var path = Path.Combine(Path.GetTempPath(), "nvidia-console-" + Guid.NewGuid().ToString("n")[..8] + ".html");
        File.WriteAllText(path, html, Encoding.UTF8);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // ── 목록 ────────────────────────────────────────────────
    void RefreshList()
    {
        syncing = true;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var c in chats) list.Items.Add(c.Title);
        var i = chats.FindIndex(c => c.Id == current.Id);
        list.SelectedIndex = i;   // -1 이면 선택 없음 (새 대화)
        list.EndUpdate();
        syncing = false;
    }

    void Delete(int index)
    {
        if (index < 0 || index >= chats.Count) return;
        var target = chats[index];
        chats.Remove(target);
        if (current.Id == target.Id) current = new Chat();
        SaveChats();
        RefreshList();
        DrawChat();
    }

    void SaveChats()
    {
        try
        {
            Store.Save(chats);
        }
        catch (Exception ex)
        {
            // 조용히 넘기면 창을 닫는 순간 대화가 사라진다
            status.Text = "저장 실패: " + ex.Message;
        }
    }

    // ── 모델 ────────────────────────────────────────────────
    // ── 설정 ────────────────────────────────────────────────
    // 뒤를 가리지 않고 흐리게 남긴다. WinForms 자식 컨트롤은 형제 위에 반투명하게 얹힐 수 없으므로,
    // 지금 화면을 한 장 떠서 흐리게 만든 뒤 그걸 막으로 깐다(그래서 뒤 화면은 멈춰 있다).
    static Bitmap Frost(Bitmap shot)
    {
        var small = new Bitmap(Math.Max(1, shot.Width / 9), Math.Max(1, shot.Height / 9));
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(shot, 0, 0, small.Width, small.Height);
        }

        var blurred = new Bitmap(shot.Width, shot.Height);
        using (var g = Graphics.FromImage(blurred))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;   // 없으면 가장자리가 잘려 테두리가 생긴다
            g.DrawImage(small, 0, 0, blurred.Width, blurred.Height);
            using var shade = new SolidBrush(Color.FromArgb(140, 0x14, 0x13, 0x12));
            g.FillRectangle(shade, 0, 0, blurred.Width, blurred.Height);
        }
        small.Dispose();
        return blurred;
    }

    void Blurred()
    {
        try
        {
            // DrawToBitmap 은 제목줄과 테두리까지 그린다 — 그대로 쓰면 배경이 그만큼 밀린다
            using var whole = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            DrawToBitmap(whole, new Rectangle(0, 0, whole.Width, whole.Height));   // 막은 아직 숨어 있다

            var edge = (Width - ClientSize.Width) / 2;
            var caption = Height - ClientSize.Height - edge;
            using var shot = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            using (var g = Graphics.FromImage(shot))
                g.DrawImage(whole, new Rectangle(0, 0, shot.Width, shot.Height),
                    new Rectangle(edge, caption, shot.Width, shot.Height), GraphicsUnit.Pixel);

            var next = Frost(shot);
            frost?.Dispose();
            frost = next;
            scrim!.BackgroundImage = frost;
            scrim.BackgroundImageLayout = ImageLayout.Stretch;
            if (sheet is not null) sheet.Backdrop = frost;
        }
        catch (Exception)
        {
            // 한 장 뜨는 데 실패하면 단색 막으로 물러선다 — 설정은 열려야 한다
            scrim!.BackgroundImage = null;
            if (sheet is not null) sheet.Backdrop = null;
        }
    }

    internal void ShowSettings(int section) { OpenSettings(); ShowSection(section); }
    internal Control? Overlay => scrim;

    // ── 업데이트 ────────────────────────────────────────────
    // 켤 때 한 번 조용히 살펴본다. 새 것이 있으면 미리 받아 두고 머리줄에 단추만 띄운다 —
    // 쓰던 중에 프로그램이 저 혼자 꺼졌다 켜지면 곤란하다.
    async Task CheckUpdate(bool asked)
    {
        if (Updater.Ready is not null) { updateNote.Text = "다시 시작하면 적용됩니다"; return; }
        try
        {
            if (asked) updateNote.Text = "확인하는 중…";
            var found = await Updater.CheckAsync();
            if (found is null)
            {
                if (asked) updateNote.Text = "최신 버전입니다";
                return;
            }

            if (asked) updateNote.Text = found.Version + " 받는 중…";
            Updater.Ready = await Updater.DownloadAsync(found);
            updateBtn.Text = "새 버전 " + found.Version + " — 다시 시작";
            updateBtn.Visible = true;
            updateNote.Text = "새 버전 " + found.Version + " 을 받았습니다";
        }
        catch (Exception ex)
        {
            // 업데이트가 안 된다고 프로그램을 못 쓸 이유는 없다 — 물어봤을 때만 알린다
            if (asked) updateNote.Text = "확인 실패: " + ex.Message;
        }
    }

    void OpenSettings()
    {
        if (scrim is null) BuildSettings();
        keyBox.Text = apiKey;
        rootBox.Text = Nvidia.Root == Nvidia.DefaultRoot ? "" : Nvidia.Root;
        ShowSection(0);
        Blurred();                                                // 비활성화 전에 떠야 원래 모습이 남는다
        foreach (Control c in Controls) c.Enabled = c == scrim;   // 뒤로 포커스가 새지 않게
        scrim!.Bounds = ClientRectangle;
        center();                       // Resize 가 아직 안 왔을 수 있다
        scrim.Visible = true;
        scrim.BringToFront();
        keyBox.Focus();
    }

    void CloseSettings()
    {
        if (scrim is null || !scrim.Visible) return;
        scrim.Visible = false;
        foreach (Control c in Controls) c.Enabled = true;
        input.Focus();

        var nextRoot = Nvidia.Normalize(rootBox.Text);
        var rootChanged = nextRoot != Nvidia.Root;
        if (rootChanged)
        {
            Nvidia.Root = nextRoot;
            Store.SaveRoot(nextRoot == Nvidia.DefaultRoot ? "" : nextRoot);
        }

        var next = keyBox.Text.Trim();
        if (next == apiKey && !rootChanged) return;

        apiKey = next;
        Store.SaveKey(apiKey);
        if (apiKey.Length > 0)
        {
            LoadModels();
        }
        else
        {
            modelMenu.Items.Clear();
            model = "";
            modelBtn.Text = NoModel;
            status.Text = "설정에서 API 키를 넣으면 모델을 불러옵니다";
        }
        if (current.Messages.Count == 0) DrawChat();   // 안내 문구를 지금 상태에 맞춘다
    }

    void ShowSection(int i)
    {
        for (var n = 0; n < sections.Count; n++)
        {
            sections[n].Visible = n == i;
            navs[n].Fill = n == i ? Ui.Card : Ui.Bg;
            navs[n].ForeColor = n == i ? Ui.Fg : Ui.UserFg;
            navs[n].Invalidate();
        }
    }

    void BuildSettings()
    {
        // Dock 을 쓰면 안 된다 — BringToFront 가 z 순서를 바꾸는 순간 도킹 순서도 바뀌어
        // 먼저 자리를 뗀 형제들에게 공간을 다 빼앗기고 크기가 0 이 된다. 앵커는 자리를 다투지 않는다.
        scrim = new Panel
        {
            Bounds = ClientRectangle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(0x17, 0x16, 0x15),
            Visible = false,
        };
        scrim.Click += (_, _) => CloseSettings();   // 카드 바깥을 누르면 닫는다
        Controls.Add(scrim);

        sheet = new RoundPanel(Ui.Bg, Color.FromArgb(0x17, 0x16, 0x15)) { Radius = 14 };
        scrim.Controls.Add(sheet);
        center = () =>
        {
            var w = Math.Min(880, Math.Max(520, scrim.Width - 180));
            var h = Math.Min(540, Math.Max(360, scrim.Height - 160));
            sheet.SetBounds((scrim.Width - w) / 2, (scrim.Height - h) / 2, w, h);
        };
        scrim.Resize += (_, _) => center();

        // 시트 안쪽 여백이 없으면 네모난 자식 패널이 둥근 모서리를 덮어 각져 보인다
        sheet.Padding = new Padding(10);

        var nav = new Panel { Dock = DockStyle.Left, Width = 166, BackColor = Ui.Bg, Padding = new Padding(14, 6, 8, 14) };
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(8, 6, 28, 20) };
        var header = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Ui.Bg };

        // 도킹은 나중에 넣은 쪽이 먼저 자리를 뗀다 — 머리줄이 가로 전체를 먹어야 하므로 마지막에 넣는다
        sheet.Controls.Add(body);
        sheet.Controls.Add(nav);
        sheet.Controls.Add(header);

        sections.Add(SectionKey());
        sections.Add(SectionFolder());
        sections.Add(SectionAbout());
        foreach (var s in sections) body.Controls.Add(s);

        // Dock=Top 은 나중에 넣은 쪽이 위로 간다 — 순서를 뒤집어 넣는다
        var names = new[] { "API 키", "저장 위치", "정보" };
        for (var i = names.Length - 1; i >= 0; i--)
        {
            var n = i;
            var b = new RoundButton
            {
                Text = names[i],
                Dock = DockStyle.Top,
                Height = 34,
                Font = Ui.Meta,
                Radius = 8,
                Border = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, 4),
            };
            b.Click += (_, _) => ShowSection(n);
            nav.Controls.Add(b);
            navs.Insert(0, b);
        }

        header.Controls.Add(new Label
        {
            Text = "설정",
            Font = Ui.Strong,
            ForeColor = Ui.Fg,
            BackColor = Ui.Bg,
            Bounds = new Rectangle(14, 12, 120, 24),
        });

        var close = new RoundButton
        {
            Text = "✕",
            Size = new Size(30, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(header.Width - 38, 6),
            Font = Ui.Meta,
            Radius = 8,
            Fill = Ui.Bg,
            Border = Color.Transparent,
            ForeColor = Ui.Muted,
        };
        close.Click += (_, _) => CloseSettings();
        header.Controls.Add(close);
    }

    Panel SectionKey()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Visible = false };
        p.Controls.Add(Note("NVIDIA API 키", Ui.Strong, Ui.Fg, 0, 0, 300, 24));
        p.Controls.Add(Note("이 PC 에만 둡니다. Windows 계정으로 암호화해 저장하므로 다른 계정이나 다른 PC 에서는 풀리지 않습니다.",
            Ui.Meta, Ui.Muted, 0, 28, 470, 36));

        var card = new RoundPanel(Ui.Card, Ui.Bg)
        {
            Bounds = new Rectangle(0, 74, 470, 40),
            Radius = 9,
            Padding = new Padding(12, 10, 12, 10),
        };
        keyBox.Dock = DockStyle.Fill;
        keyBox.UseSystemPasswordChar = true;
        keyBox.BackColor = Ui.Card;
        keyBox.ForeColor = Ui.Fg;
        keyBox.BorderStyle = BorderStyle.None;
        card.Controls.Add(keyBox);
        p.Controls.Add(card);

        // 붙여넣은 키가 맞는지 볼 방법이 없으면 오타를 찾을 길이 없다
        var peek = new RoundButton { Text = "키 보기", Bounds = new Rectangle(0, 124, 90, 28), Font = Ui.Meta, Radius = 8 };
        peek.Click += (_, _) =>
        {
            keyBox.UseSystemPasswordChar = !keyBox.UseSystemPasswordChar;
            peek.Text = keyBox.UseSystemPasswordChar ? "키 보기" : "키 가리기";
        };
        p.Controls.Add(peek);

        var get = new RoundButton { Text = "키 발급받기", Bounds = new Rectangle(98, 124, 110, 28), Font = Ui.Meta, Radius = 8 };
        get.Click += (_, _) => Open("https://build.nvidia.com");
        p.Controls.Add(get);

        p.Controls.Add(Note("보낼 곳 (Base URL)", Ui.Strong, Ui.Fg, 0, 174, 300, 24));
        p.Controls.Add(Note("비워 두면 NVIDIA 로 갑니다. OpenAI 호환 주소면 다른 곳도 됩니다.",
            Ui.Meta, Ui.Muted, 0, 200, 470, 20));

        var rootCard = new RoundPanel(Ui.Card, Ui.Bg)
        {
            Bounds = new Rectangle(0, 226, 470, 40),
            Radius = 9,
            Padding = new Padding(12, 10, 12, 10),
        };
        rootBox.Dock = DockStyle.Fill;
        rootBox.BackColor = Ui.Card;
        rootBox.ForeColor = Ui.Fg;
        rootBox.BorderStyle = BorderStyle.None;
        rootBox.PlaceholderText = Nvidia.DefaultRoot;
        rootCard.Controls.Add(rootBox);
        p.Controls.Add(rootCard);
        return p;
    }

    Panel SectionFolder()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Visible = false };
        p.Controls.Add(Note("저장 위치", Ui.Strong, Ui.Fg, 0, 0, 300, 24));
        p.Controls.Add(Note("대화(chats.json)와 키(key.dat)가 있는 곳입니다. 지우면 처음 상태로 돌아갑니다.",
            Ui.Meta, Ui.Muted, 0, 28, 470, 36));
        p.Controls.Add(Note(Store.Folder, Ui.Meta, Ui.UserFg, 0, 74, 470, 20));

        var open = new RoundButton { Text = "폴더 열기", Bounds = new Rectangle(0, 104, 100, 28), Font = Ui.Meta, Radius = 8 };
        open.Click += (_, _) => Open(Store.Folder);
        p.Controls.Add(open);
        return p;
    }

    Panel SectionAbout()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Visible = false };
        p.Controls.Add(Note("NVIDIA 코딩 콘솔 (로컬판)", Ui.Strong, Ui.Fg, 0, 0, 300, 24));
        p.Controls.Add(Note("버전 " + Updater.Current, Ui.Meta, Ui.UserFg, 0, 28, 300, 20));

        var check = new RoundButton { Text = "업데이트 확인", Bounds = new Rectangle(0, 60, 120, 28), Font = Ui.Meta, Radius = 8 };
        check.Click += async (_, _) => await CheckUpdate(true);
        p.Controls.Add(check);

        updateNote.SetBounds(130, 60, 340, 28);
        updateNote.Font = Ui.Meta;
        updateNote.ForeColor = Ui.Muted;
        updateNote.BackColor = Ui.Bg;
        updateNote.TextAlign = ContentAlignment.MiddleLeft;
        p.Controls.Add(updateNote);
        return p;
    }

    static Label Note(string text, Font font, Color fore, int x, int y, int w, int h) => new()
    {
        Text = text,
        Font = font,
        ForeColor = fore,
        BackColor = Ui.Bg,
        Bounds = new Rectangle(x, y, w, h),
    };

    static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    void PickModel(string id)
    {
        model = id;
        modelBtn.Text = id + "   ▾";
    }

    async void LoadModels()
    {
        var key = apiKey;
        if (key.Length == 0) return;
        status.Text = "모델 목록 받는 중…";
        try
        {
            var ids = await Nvidia.ModelsAsync(key);
            modelMenu.Items.Clear();
            foreach (var id in ids)
            {
                var item = modelMenu.Items.Add(id);
                item.ForeColor = Ui.Fg;
                item.Click += (_, _) => PickModel(id);
            }
            var pick = Nvidia.PickDefault(ids);
            PickModel(pick.Length > 0 ? pick : ids[0]);
            status.Text = "모델 " + ids.Count + "개";
            if (current.Messages.Count == 0) DrawChat();   // 안내 문구를 다음 단계로 바꾼다
        }
        catch (Exception ex)
        {
            modelMenu.Items.Clear();
            model = "";
            modelBtn.Text = NoModel;
            status.Text = "모델 목록 실패: " + ex.Message;
        }
    }

    // ── 보내기 ──────────────────────────────────────────────
    async void Send()
    {
        var text = input.Text.Trim();
        if (text.Length == 0) return;
        var key = apiKey;
        if (key.Length == 0) { status.Text = "설정에서 API 키를 먼저 넣어주세요"; OpenSettings(); return; }
        if (model.Length == 0) { status.Text = "모델을 먼저 골라주세요"; return; }

        input.Clear();
        // 답변을 받는 도중 다른 대화를 열어도 답변은 시작한 대화에 붙는다
        var chat = current;
        chat.Messages.Add(new Message { Role = "user", Content = text });
        if (chat.Messages.Count == 1) chat.Title = text.Length > 30 ? text[..30] : text;
        DrawChat();

        send.Enabled = false;
        status.Text = "답변 받는 중…";
        Append("AI\n", Ui.Meta, Ui.Accent, Ui.Bg);

        var acc = new StringBuilder();
        streaming = new CancellationTokenSource();
        try
        {
            await Nvidia.ChatAsync(key, model, chat.Messages, delta =>
            {
                acc.Append(delta);
                BeginInvoke(() => { Append(delta, Ui.Body, Ui.Fg, Ui.Bg); ScrollToEnd(); });
            }, streaming.Token);

            if (acc.ToString().Trim().Length == 0)
            {
                Append("\n(빈 응답) 추론 모델이면 사고 토큰이 max_tokens 를 다 먹었을 수 있습니다. 다른 모델로 바꿔보세요.\n", Ui.Body, Ui.Muted, Ui.Bg);
                status.Text = "빈 응답";
            }
            else
            {
                chat.Messages.Add(new Message { Role = "assistant", Content = acc.ToString() });
                chats.RemoveAll(c => c.Id == chat.Id);
                chats.Insert(0, chat);
                SaveChats();
                RefreshList();
                if (current.Id == chat.Id) DrawChat();   // 코드 블록 서식과 복사 버튼을 붙인다
                status.Text = "완료";
            }
        }
        catch (Exception ex)
        {
            Append("\n실패: " + ex.Message + "\n", Ui.Body, Ui.Accent, Ui.Bg);
            status.Text = "실패";
        }
        finally
        {
            send.Enabled = true;
            streaming = null;
        }
    }
}

// 빌드한 EXE 가 실제로 도는지 화면 없이 확인한다.
static class SelfTest
{
    public static int Run(string key)
    {
        var failed = 0;

        var blocks = Fences.Parse("설명입니다.\n```html\n<b>hi</b>\n```\n뒤 설명.\n```python\nprint(1)\n```");
        failed += Check("코드펜스 4덩이 · html · python",
            blocks.Count == 4 && blocks[1] is { IsCode: true, Lang: "html" } && blocks[3] is { IsCode: true, Lang: "python" });

        try
        {
            var before = Store.Load();
            Store.Save(before);
            failed += Check("대화 파일 왕복 (" + before.Count + "건, " + Store.Folder + ")", Store.Load().Count == before.Count);
        }
        catch (Exception ex) { failed += Check("대화 파일 왕복 — " + ex.Message, false); }

        try
        {
            var blob = System.Security.Cryptography.ProtectedData.Protect(
                Encoding.UTF8.GetBytes("check"), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var back = Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                blob, null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
            failed += Check("키 암호화 왕복 · 평문 아님", back == "check" && !Encoding.UTF8.GetString(blob).Contains("check"));
        }
        catch (Exception ex) { failed += Check("키 암호화 — " + ex.Message, false); }

        failed += Check("보낼 곳 — 빈 칸은 기본값, 끝의 / 는 뗀다",
            Nvidia.Normalize("  ") == Nvidia.DefaultRoot
            && Nvidia.Normalize("https://x.test/v1/") == "https://x.test/v1"
            && Nvidia.Normalize(" https://x.test/v1 ") == "https://x.test/v1");

        failed += Check("새 버전만 새 것으로 본다",
            Updater.Newer("v2.6.0", "2.5.0") && !Updater.Newer("v2.5.0", "2.5.0")
            && !Updater.Newer("v2.4.9", "2.5.0") && !Updater.Newer("최신", "2.5.0"));

        failed += Check("깃허브 https 주소만 내려받는다",
            Updater.TrustedUrl("https://github.com/a/b/releases/download/v1/x.exe")
            && Updater.TrustedUrl("https://objects.githubusercontent.com/x")
            && !Updater.TrustedUrl("http://github.com/a/b/x.exe")
            && !Updater.TrustedUrl("https://github.com.evil.test/x.exe"));

        try
        {
            var ids = Nvidia.ModelsAsync(key).GetAwaiter().GetResult();
            failed += Check("모델 " + ids.Count + "개 · 기본값 " + Nvidia.PickDefault(ids),
                ids.Count > 0 && !ids.Any(id => id.Contains("embed", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) { failed += Check("모델 목록 — " + ex.Message, false); }

        Console.WriteLine(failed == 0 ? "모두 통과" : failed + "개 실패");
        return failed == 0 ? 0 : 1;
    }

    static int Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  OK    " : "  실패  ") + name);
        return ok ? 0 : 1;
    }
}
