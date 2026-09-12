// NVIDIA 코딩 콘솔 (로컬판) — 서버도 계정도 웹페이지도 없이 도는 Windows 프로그램.
//
// 화면은 WinForms 로 직접 그리고, NVIDIA 호출도 프로그램이 직접 한다(Nvidia.cs).
// 대화는 EXE 옆 chats.json, 키는 같은 폴더에 Windows 계정으로 암호화해 둔다(Store.cs).
//
//   NvidiaConsole.exe              창을 연다
//   NvidiaConsole.exe --selftest   화면 없이 자체 점검만 하고 끝낸다 (빌드 후 확인용)

using System.Diagnostics;
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

sealed class MainForm : Form
{
    static readonly Color Bg = Color.FromArgb(0x1b, 0x1b, 0x1a);
    static readonly Color PanelBg = Color.FromArgb(0x23, 0x23, 0x22);
    static readonly Color CodeBg = Color.FromArgb(0x11, 0x14, 0x1a);
    static readonly Color Fg = Color.FromArgb(0xe8, 0xe6, 0xe3);
    static readonly Color DimFg = Color.FromArgb(0x91, 0x8e, 0x88);
    static readonly Color Accent = Color.FromArgb(0xd9, 0x77, 0x57);

    readonly Font uiFont = new("Malgun Gothic", 10F);
    readonly Font labelFont = new("Malgun Gothic", 8.5F, FontStyle.Bold);
    readonly Font monoFont = new("Consolas", 10F);

    readonly ListBox list = new();
    readonly RichTextBox view = new();
    readonly TextBox input = new();
    readonly ComboBox models = new();
    readonly TextBox keyBox = new();
    readonly FlowLayoutPanel codeBar = new();
    readonly Label status = new();
    readonly Button send = new();

    List<Chat> chats = Store.Load();
    Chat current = new();
    CancellationTokenSource? streaming;

    public MainForm()
    {
        Text = "NVIDIA 코딩 콘솔";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(1180, 800);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Fg;
        Font = uiFont;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 260,
            BackColor = Bg,
            FixedPanel = FixedPanel.Panel1,
        };
        Controls.Add(split);
        split.Panel1.Controls.Add(BuildSidebar());
        split.Panel2.Controls.Add(BuildMain());

        RefreshList();
        DrawChat();
        keyBox.Text = Store.LoadKey();
        if (keyBox.Text.Length > 0) LoadModels();
        else status.Text = "왼쪽 아래에 NVIDIA API 키를 넣으세요";
    }

    // ── 화면 ────────────────────────────────────────────────
    Control BuildSidebar()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Bg, Padding = new Padding(8) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        var newChat = Flat("＋ 새 대화");
        newChat.Dock = DockStyle.Fill;
        newChat.Click += (_, _) => { current = new Chat(); list.ClearSelected(); DrawChat(); };
        panel.Controls.Add(newChat, 0, 0);

        list.Dock = DockStyle.Fill;
        list.BackColor = PanelBg;
        list.ForeColor = Fg;
        list.BorderStyle = BorderStyle.None;
        list.IntegralHeight = false;
        list.ItemHeight = 24;
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < chats.Count)
            {
                current = chats[list.SelectedIndex];
                DrawChat();
            }
        };
        panel.Controls.Add(list, 0, 1);

        var del = Flat("선택한 대화 삭제");
        del.Dock = DockStyle.Fill;
        del.Click += (_, _) => DeleteSelected();
        panel.Controls.Add(del, 0, 2);

        var keyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Bg };
        var keyLabel = new Label { Text = "NVIDIA API 키 (이 PC 에만 저장)", Dock = DockStyle.Fill, ForeColor = DimFg, Font = labelFont };
        keyBox.Dock = DockStyle.Fill;
        keyBox.UseSystemPasswordChar = true;
        keyBox.BackColor = PanelBg;
        keyBox.ForeColor = Fg;
        keyBox.BorderStyle = BorderStyle.FixedSingle;
        keyBox.TextChanged += (_, _) => Store.SaveKey(keyBox.Text.Trim());
        keyBox.Leave += (_, _) => { if (keyBox.Text.Trim().Length > 0 && models.Items.Count == 0) LoadModels(); };
        keyPanel.Controls.Add(keyLabel, 0, 0);
        keyPanel.Controls.Add(keyBox, 0, 1);
        panel.Controls.Add(keyPanel, 0, 3);

        return panel;
    }

    Control BuildMain()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Bg, Padding = new Padding(8) };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));

        view.Dock = DockStyle.Fill;
        view.ReadOnly = true;
        view.BackColor = Bg;
        view.ForeColor = Fg;
        view.BorderStyle = BorderStyle.None;
        view.Font = uiFont;
        view.DetectUrls = false;
        panel.Controls.Add(view, 0, 0);

        codeBar.Dock = DockStyle.Fill;
        codeBar.AutoSize = true;
        codeBar.WrapContents = true;
        codeBar.BackColor = Bg;
        panel.Controls.Add(codeBar, 0, 1);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Bg };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        input.Dock = DockStyle.Fill;
        input.Multiline = true;
        input.BackColor = PanelBg;
        input.ForeColor = Fg;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.ScrollBars = ScrollBars.Vertical;
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                Send();
            }
        };
        bottom.Controls.Add(input, 0, 0);

        send.Text = "전송";
        send.Dock = DockStyle.Fill;
        Style(send);
        send.BackColor = Accent;
        send.ForeColor = Color.FromArgb(0x2a, 0x0f, 0x06);
        send.Click += (_, _) => Send();
        bottom.Controls.Add(send, 1, 0);

        status.Dock = DockStyle.Fill;
        status.ForeColor = DimFg;
        status.Font = labelFont;
        status.TextAlign = ContentAlignment.MiddleLeft;
        bottom.Controls.Add(status, 0, 1);

        models.Dock = DockStyle.Fill;
        models.DropDownStyle = ComboBoxStyle.DropDownList;
        models.BackColor = PanelBg;
        models.ForeColor = Fg;
        models.FlatStyle = FlatStyle.Flat;
        bottom.Controls.Add(models, 1, 1);

        panel.Controls.Add(bottom, 0, 2);
        return panel;
    }

    Button Flat(string text)
    {
        var b = new Button { Text = text };
        Style(b);
        return b;
    }

    void Style(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = PanelBg;
        b.ForeColor = Fg;
        b.FlatAppearance.BorderColor = Color.FromArgb(0x33, 0x33, 0x31);
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
        foreach (var m in current.Messages)
        {
            var mine = m.Role == "user";
            Append(mine ? "나\n" : "AI\n", labelFont, mine ? DimFg : Accent, Bg);
            if (mine)
            {
                Append(m.Content + "\n\n", uiFont, DimFg, Bg);
                continue;
            }
            foreach (var b in Fences.Parse(m.Content))
            {
                if (b.IsCode) Append(b.Text.TrimEnd('\n') + "\n\n", monoFont, Fg, CodeBg);
                else Append(b.Text + "\n\n", uiFont, Fg, Bg);
            }
        }
        BuildCodeButtons();
        ScrollToEnd();
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
            var label = "⧉ 코드 " + n + (b.Lang.Length > 0 ? " (" + b.Lang + ")" : "");
            var copy = Flat(label);
            copy.AutoSize = true;
            copy.Click += (_, _) =>
            {
                Clipboard.SetText(code);
                status.Text = label + " 복사됨";
            };
            codeBar.Controls.Add(copy);

            if (b.Lang is "html" or "svg" || code.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                var open = Flat("▷ 브라우저로 열기");
                open.AutoSize = true;
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
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var c in chats) list.Items.Add(c.Title);
        list.EndUpdate();
    }

    void DeleteSelected()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= chats.Count) return;
        var target = chats[list.SelectedIndex];
        chats.Remove(target);
        SaveChats();
        RefreshList();
        if (current.Id == target.Id) { current = new Chat(); DrawChat(); }
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
    async void LoadModels()
    {
        var key = keyBox.Text.Trim();
        if (key.Length == 0) return;
        status.Text = "모델 목록 받는 중…";
        try
        {
            var ids = await Nvidia.ModelsAsync(key);
            models.Items.Clear();
            foreach (var id in ids) models.Items.Add(id);
            var pick = Nvidia.PickDefault(ids);
            if (pick.Length > 0) models.SelectedItem = pick;
            status.Text = ids.Count + "개 모델";
        }
        catch (Exception ex)
        {
            status.Text = "모델 목록 실패: " + ex.Message;
        }
    }

    // ── 보내기 ──────────────────────────────────────────────
    async void Send()
    {
        var text = input.Text.Trim();
        if (text.Length == 0) return;
        var key = keyBox.Text.Trim();
        if (key.Length == 0) { MessageBox.Show(this, "API 키를 먼저 넣어주세요.", Text); return; }
        if (models.SelectedItem is not string model || model.Length == 0) { MessageBox.Show(this, "모델을 먼저 골라주세요.", Text); return; }

        input.Clear();
        // 답변을 받는 도중 다른 대화를 열어도 답변은 시작한 대화에 붙는다
        var chat = current;
        chat.Messages.Add(new Message { Role = "user", Content = text });
        if (chat.Messages.Count == 1) chat.Title = text.Length > 30 ? text[..30] : text;
        DrawChat();

        send.Enabled = false;
        status.Text = "답변 받는 중…";
        Append("AI\n", labelFont, Accent, Bg);

        var acc = new StringBuilder();
        streaming = new CancellationTokenSource();
        try
        {
            await Nvidia.ChatAsync(key, model, chat.Messages, delta =>
            {
                acc.Append(delta);
                BeginInvoke(() => { Append(delta, uiFont, Fg, Bg); ScrollToEnd(); });
            }, streaming.Token);

            if (acc.ToString().Trim().Length == 0)
            {
                Append("\n(빈 응답) 추론 모델이면 사고 토큰이 max_tokens 를 다 먹었을 수 있습니다. 다른 모델로 바꿔보세요.\n", uiFont, DimFg, Bg);
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
            }
        }
        catch (Exception ex)
        {
            Append("\n실패: " + ex.Message + "\n", uiFont, Accent, Bg);
        }
        finally
        {
            send.Enabled = true;
            status.Text = Store.Folder;
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
