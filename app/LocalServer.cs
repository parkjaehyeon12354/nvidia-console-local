// EXE 안에서 도는 작은 HTTP 서버.
//
//   GET /            → web/index.html
//   GET /<파일>      → web 폴더 안의 파일
//   /v1/*            → NVIDIA(integrate.api.nvidia.com) 로 중계. 답변은 오는 대로 흘려보낸다.
//
// 127.0.0.1 에만 바인딩해서 이 PC 밖에서는 접근할 수 없다.
// 키는 서버가 들고 있지 않다 — 페이지가 보낸 Authorization 헤더를 그대로 넘길 뿐이다.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace NvidiaConsole;

sealed class LocalServer : IDisposable
{
    const string Upstream = "https://integrate.api.nvidia.com";

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
    };

    readonly HttpListener listener = new();
    readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };   // 스트리밍 답변은 오래 걸린다
    readonly string root;

    public string Origin { get; }

    public LocalServer(string webRoot)
    {
        root = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar);

        // 확인용으로 포트를 고정할 수 있게 한다. 평소에는 비어 있어 임의의 빈 포트를 쓴다.
        var pinned = Environment.GetEnvironmentVariable("NVIDIA_CONSOLE_PORT");
        if (int.TryParse(pinned, out var forced) && forced > 1023 && forced < 65536)
        {
            listener.Prefixes.Add("http://127.0.0.1:" + forced + "/");
            listener.Start();
            Origin = "http://127.0.0.1:" + forced + "/";
            _ = Task.Run(LoopAsync);
            return;
        }

        // 빈 포트를 OS 에게 받아 그 번호로 연다. 그사이 다른 프로그램이 채가면 몇 번 더 시도한다.
        Exception? last = null;
        for (var i = 0; i < 5; i++)
        {
            var candidate = "http://127.0.0.1:" + FreePort() + "/";
            try
            {
                listener.Prefixes.Clear();
                listener.Prefixes.Add(candidate);
                listener.Start();
                Origin = candidate;
                _ = Task.Run(LoopAsync);
                return;
            }
            catch (HttpListenerException ex) { last = ex; }
        }
        throw last ?? new InvalidOperationException("포트를 열지 못했습니다");
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    async Task LoopAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (Exception) { return; }      // 창을 닫으면 여기서 끝난다
            _ = HandleAsync(ctx);
        }
    }

    async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path.StartsWith("/v1/", StringComparison.Ordinal)) await ProxyAsync(ctx);
            else await FileAsync(ctx, path);
        }
        catch (Exception)
        {
            // 페이지를 새로고침하면 받던 응답이 끊긴다. 흔한 일이라 창을 죽이지 않는다.
        }
        finally
        {
            try { ctx.Response.Close(); } catch (Exception) { }
        }
    }

    async Task FileAsync(HttpListenerContext ctx, string path)
    {
        if (path == "/") path = "/index.html";
        var full = Path.GetFullPath(Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

        // web 폴더 밖으로 나가는 경로(../)는 거절한다
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        ctx.Response.ContentType = Types.TryGetValue(Path.GetExtension(full), out var type) ? type : "application/octet-stream";
        ctx.Response.Headers["Cache-Control"] = "no-store";   // web 폴더를 고치고 다시 켜면 바로 반영되게
        using var file = File.OpenRead(full);
        ctx.Response.ContentLength64 = file.Length;
        await file.CopyToAsync(ctx.Response.OutputStream);
    }

    async Task ProxyAsync(HttpListenerContext ctx)
    {
        var auth = ctx.Request.Headers["Authorization"];
        if (string.IsNullOrEmpty(auth))
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.ContentType = "application/json";
            var msg = System.Text.Encoding.UTF8.GetBytes("{\"error\":\"NVIDIA 키가 필요합니다\"}");
            await ctx.Response.OutputStream.WriteAsync(msg);
            return;
        }

        using var up = new HttpRequestMessage(new HttpMethod(ctx.Request.HttpMethod), Upstream + ctx.Request.Url!.PathAndQuery);
        up.Headers.TryAddWithoutValidation("Authorization", auth);
        if (ctx.Request.HttpMethod == "POST")
        {
            using var buffer = new MemoryStream();
            await ctx.Request.InputStream.CopyToAsync(buffer);
            up.Content = new ByteArrayContent(buffer.ToArray());
            up.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var res = await http.SendAsync(up, HttpCompletionOption.ResponseHeadersRead);
        ctx.Response.StatusCode = (int)res.StatusCode;
        ctx.Response.ContentType = res.Content.Headers.ContentType?.ToString() ?? "application/json";
        ctx.Response.SendChunked = true;      // 길이를 모른 채 오는 대로 내보낸다

        await using var stream = await res.Content.ReadAsStreamAsync();
        var chunk = new byte[8192];
        int n;
        // 답변 한 조각씩 바로 흘려보내야 화면에 글자가 흐르듯 나온다
        while ((n = await stream.ReadAsync(chunk)) > 0)
        {
            await ctx.Response.OutputStream.WriteAsync(chunk.AsMemory(0, n));
            await ctx.Response.OutputStream.FlushAsync();
        }
    }

    public void Dispose()
    {
        try { listener.Stop(); listener.Close(); } catch (Exception) { }
        http.Dispose();
    }
}
