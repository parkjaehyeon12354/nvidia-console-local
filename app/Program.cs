// NVIDIA 코딩 콘솔 (로컬판) — 서버도 계정도 없이 이 PC 안에서만 도는 데스크톱 앱.
//
// EXE 옆 web 폴더를 127.0.0.1 의 빈 포트로 띄우고(LocalServer.cs), 그 페이지를 창 안 WebView2 로 연다.
// NVIDIA 호출도 같은 로컬 서버가 중계한다 — 브라우저에서 NVIDIA 를 직접 부르면 CORS 에 막힌다.
//
// 웹사이트는 EXE 안에 넣지 않았다. web\index.html 을 고치고 EXE 를 다시 켜면 그대로 반영된다.
// NVIDIA 키와 대화는 EXE 옆 브라우저 프로필에만 남는다.

using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NvidiaConsole;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

sealed class MainForm : Form
{
    static readonly Color Background = Color.FromArgb(0x1b, 0x1b, 0x1a);

    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    LocalServer? server;

    public MainForm()
    {
        Text = "NVIDIA 코딩 콘솔";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(1280, 860);
        MinimumSize = new Size(420, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        Controls.Add(web);
        Shown += async (_, _) => await StartAsync();
        FormClosed += (_, _) => server?.Dispose();
    }

    async Task StartAsync()
    {
        var root = FindWebRoot();
        if (root is null)
        {
            MessageBox.Show(this,
                "web 폴더를 찾지 못했습니다.\n\nEXE 옆에 web\\index.html 이 있어야 합니다:\n" + AppContext.BaseDirectory + "web\\index.html",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        try
        {
            server = new LocalServer(root);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "로컬 서버를 시작하지 못했습니다: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, DataFolder());
            await web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Microsoft Edge WebView2 런타임이 필요합니다.\nhttps://developer.microsoft.com/microsoft-edge/webview2/ 에서 설치해주세요.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = web.CoreWebView2;
        core.NavigationStarting += OnNavigationStarting;
        // target=_blank 같은 새 창은 앱 안에 띄우지 않고 기본 브라우저로 넘긴다
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenInBrowser(e.Uri); };
        web.Source = new Uri(server.Origin);
    }

    // 1) EXE 옆 web  2) 레포에서 바로 실행할 때를 위해 위로 올라가며 찾는다
    static string? FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "web");
            if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
        }
        return null;
    }

    // C 드라이브를 아끼려고 브라우저 프로필을 EXE 옆에 둔다. 쓰기 금지 폴더면 사용자 폴더로.
    static string DataFolder()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "NvidiaConsole.WebView2");
        try
        {
            Directory.CreateDirectory(beside);
            var probe = Path.Combine(beside, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return beside;
        }
        catch (Exception)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NvidiaConsole", "WebView2");
        }
    }

    // 로컬 서버 밖 주소는 앱 안에서 열지 않고 기본 브라우저로 넘긴다
    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (server is null || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

        if (uri.Authority != new Uri(server.Origin).Authority)
        {
            e.Cancel = true;
            OpenInBrowser(e.Uri);
        }
    }

    // http/https 만 연다. file: 이나 사용자 정의 프로토콜을 셸로 넘기면 로컬 프로그램이 실행될 수 있다.
    static void OpenInBrowser(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true });
        }
    }
}
