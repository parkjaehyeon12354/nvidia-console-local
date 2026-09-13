// 자동 업데이트 — 깃허브 릴리스에서 최신 버전을 보고, 새 것이면 받아 두었다가
// 프로그램을 끄는 순간 바꿔치기하고 다시 켠다.
//
// 레포가 공개라서 인증 없이 읽는다. 다시 비공개로 돌리면 404 가 오고, 이 기능만 조용히 멈춘다.

using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace NvidiaConsole;

static class Updater
{
    const string Latest = "https://api.github.com/repos/parkjaehyeon12354/nvidia-console-local/releases/latest";
    const string AssetName = "NvidiaConsole.exe";

    public static string Current => Application.ProductVersion.Split('+')[0];

    public sealed record Release(string Version, string Url);

    static HttpClient Client()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.Add("User-Agent", "NvidiaConsole");   // 없으면 깃허브가 403 을 준다
        c.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return c;
    }

    public static async Task<Release?> CheckAsync(CancellationToken ct = default)
    {
        using var http = Client();
        using var res = await http.GetAsync(Latest, ct);
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!Newer(tag, Current)) return null;
        if (!root.TryGetProperty("assets", out var assets)) return null;

        foreach (var a in assets.EnumerateArray())
        {
            if (a.TryGetProperty("name", out var n) && n.GetString() != AssetName) continue;
            var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            if (TrustedUrl(url)) return new Release(Clean(tag), url);
        }
        return null;
    }

    // 받은 주소를 그대로 실행 파일로 저장한다. 깃허브가 준 주소인지 확인하고, 평문 http 는 받지 않는다.
    public static bool TrustedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        return u.Scheme == Uri.UriSchemeHttps
            && (u.Host == "github.com" || u.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal));
    }

    public static string Clean(string tag) => tag.TrimStart('v', 'V');

    public static bool Newer(string tag, string current) =>
        Version.TryParse(Clean(tag), out var a) && Version.TryParse(current, out var b) && a > b;

    public static async Task<string> DownloadAsync(Release r, CancellationToken ct = default)
    {
        using var http = Client();
        var bytes = await http.GetByteArrayAsync(r.Url, ct);
        if (bytes.Length < 50_000) throw new InvalidOperationException("받은 파일이 너무 작습니다");

        var path = Path.Combine(Path.GetTempPath(), "NvidiaConsole-" + r.Version + ".exe");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    // 실행 중인 파일은 자기 자신을 덮어쓸 수 없다. 이 프로세스가 끝나기를 기다렸다가
    // 바꿔치고 다시 켜는 일을 PowerShell 에 맡긴다.
    // 배치 파일(.cmd)은 한글 경로에서 인코딩이 어긋나므로 쓰지 않는다 — EncodedCommand 는 UTF-16 이라 안전하다.
    public static void InstallAndRestart()
    {
        var target = Environment.ProcessPath ?? Application.ExecutablePath;
        if (Ready is null) return;

        var script = string.Join("; ", new[]
        {
            "$ErrorActionPreference='Stop'",
            "while (Get-Process -Id " + Environment.ProcessId + " -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }",
            "Move-Item -LiteralPath " + Quote(Ready) + " -Destination " + Quote(target) + " -Force",
            "Start-Process -FilePath " + Quote(target),
        });

        Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Application.Exit();
    }

    static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    /// 받아 둔 새 버전 파일. 아직 없으면 null.
    public static string? Ready { get; set; }
}
