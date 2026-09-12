// NVIDIA 코딩 콘솔 (로컬판) — 서버도 계정도 웹페이지도 없이 도는 Windows 프로그램.
//
// 화면은 WinForms 로 직접 그린다. 둥근 모서리·호버·눌림·플레이스홀더는 WinForms 가
// 주지 않으므로 Ui/RoundPanel/RoundButton 에서 직접 칠한다.
// NVIDIA 호출도 프로그램이 직접 하고(Nvidia.cs), 대화와 키는 사용자 폴더에 둔다(Store.cs).
//
//   NvidiaConsole.exe              창을 연다
//   NvidiaConsole.exe --selftest   화면 없이 자체 점검만 하고 끝낸다 (빌드 후 확인용)

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
        Application.Run(new MainForm());
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

    public RoundPanel(Color fill, Color outer)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = fill;
        Outer = outer;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
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
    readonly TextBox keyBox = new();
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

        // Fill 을 먼저 넣어야 남은 자리를 가져간다 (도킹은 뒤에 넣은 것부터 자리를 뗀다)
        Controls.Add(BuildMain());
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Ui.Line });
        Controls.Add(BuildSidebar());

        keyBox.Text = Store.LoadKey();
        RefreshList();
        DrawChat();
        if (keyBox.Text.Length > 0) LoadModels();
        else status.Text = "API 키를 넣으면 모델을 불러옵니다";
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
            RowCount = 5,
            BackColor = Ui.Side,
            Padding = new Padding(12, 14, 12, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

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

        root.Controls.Add(new Label
        {
            Text = "NVIDIA API 키 — 이 PC 에만 저장",
            Dock = DockStyle.Fill,
            ForeColor = Ui.Muted,
            Font = Ui.Meta,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(2, 0, 0, 4),
        }, 0, 2);

        var keyCard = new RoundPanel(Ui.Card, Ui.Side)
        {
            Dock = DockStyle.Fill,
            Radius = 9,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 0, 0, 8),
        };
        keyBox.Dock = DockStyle.Fill;
        keyBox.UseSystemPasswordChar = true;
        keyBox.BackColor = Ui.Card;
        keyBox.ForeColor = Ui.Fg;
        keyBox.BorderStyle = BorderStyle.None;
        keyBox.TextChanged += (_, _) => Store.SaveKey(keyBox.Text.Trim());
        keyBox.Leave += (_, _) => { if (keyBox.Text.Trim().Length > 0 && !HasModels()) LoadModels(); };
        keyCard.Controls.Add(keyBox);
        root.Controls.Add(keyCard, 0, 3);

        var folder = new Label
        {
            Text = Store.Folder,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(0x6b, 0x69, 0x63),
            Font = Ui.Meta,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(2, 0, 0, 0),
        };
        new ToolTip().SetToolTip(folder, "대화와 키가 저장되는 곳\n" + Store.Folder);
        root.Controls.Add(folder, 0, 4);

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
        var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Ui.Bg, Padding = new Padding(24, 0, 18, 0) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.Dock = DockStyle.Fill;
        heading.Font = Ui.Strong;
        heading.ForeColor = Ui.Fg;
        heading.TextAlign = ContentAlignment.MiddleLeft;
        heading.AutoEllipsis = true;
        head.Controls.Add(heading, 0, 0);
        status.AutoSize = false;
        status.Width = 320;
        status.Dock = DockStyle.Fill;
        status.Font = Ui.Meta;
        status.ForeColor = Ui.Muted;
        status.TextAlign = ContentAlignment.MiddleRight;
        head.Controls.Add(status, 1, 0);
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
        Append(keyBox.Text.Trim().Length == 0
            ? "왼쪽 아래에 NVIDIA API 키를 넣으면 모델을 불러옵니다.\n"
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
    bool HasModels() => modelMenu.Items.Count > 0;

    void PickModel(string id)
    {
        model = id;
        modelBtn.Text = id + "   ▾";
    }

    async void LoadModels()
    {
        var key = keyBox.Text.Trim();
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
        var key = keyBox.Text.Trim();
        if (key.Length == 0) { status.Text = "왼쪽 아래에 API 키를 먼저 넣어주세요"; keyBox.Focus(); return; }
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
                chat.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
