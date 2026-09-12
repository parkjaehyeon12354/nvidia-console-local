// NVIDIA NIM(OpenAI 호환) 호출과 코드펜스 나누기.
// 키는 부를 때마다 넘겨받는다 — 이 파일은 키를 들고 있지 않는다.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NvidiaConsole;

sealed class Message
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

readonly record struct Block(bool IsCode, string Lang, string Text);

static class Fences
{
    // ``` 로만 나눈다. 마크다운 전체를 파싱할 이유가 없다.
    public static List<Block> Parse(string text)
    {
        var parts = text.Split("```");
        var blocks = new List<Block>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0)
            {
                var prose = parts[i].Trim('\n');
                if (prose.Trim().Length > 0) blocks.Add(new Block(false, "", prose));
                continue;
            }
            var nl = parts[i].IndexOf('\n');
            var lang = nl < 0 ? "" : parts[i][..nl].Trim().ToLowerInvariant();
            var code = nl < 0 ? parts[i] : parts[i][(nl + 1)..];
            blocks.Add(new Block(true, lang, code));
        }
        return blocks;
    }
}

static class Nvidia
{
    const string Root = "https://integrate.api.nvidia.com/v1";
    const string SystemPrompt = "너는 숙련된 개발자다. 코드는 반드시 언어를 명시한 코드펜스로 감싸고, 설명은 간결하게 한국어로 한다.";

    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };   // 스트리밍 답변은 오래 걸린다

    // 목록에는 채팅이 안 되는 모델이 섞여 있다 — 임베딩·리랭커·리워드·세이프티가드·OCR·CLIP.
    // /v1/models 가 용도를 알려주지 않아 이름으로 거르는 게 최선이다. 새는 게 보이면 패턴을 추가할 것.
    static readonly Regex NotChat =
        new("(embed|retriev|rerank|reward|guard|content-safety|nvclip|deplot|parse|detector)", RegexOptions.IgnoreCase);

    // 알파벳순 첫 항목(01-ai/yi-large)은 기본값으로 나쁘다. 사고 토큰 문제가 없는 평범한 instruct 를 먼저 본다.
    static readonly string[] Prefer =
    {
        "nvidia/llama-3.1-nemotron-70b-instruct",
        "mistralai/mistral-large-2-instruct",
        "mistralai/codestral-22b-instruct-v0.1",
    };

    public static async Task<List<string>> ModelsAsync(string key, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Root + "/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var res = await Http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw await FailureAsync(res, ct);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString() ?? "")
            .Where(id => id.Length > 0 && !NotChat.IsMatch(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    public static string PickDefault(List<string> ids) =>
        Prefer.FirstOrDefault(ids.Contains) ?? ids.FirstOrDefault() ?? "";

    // 답변 조각이 올 때마다 onDelta 가 불린다. 백그라운드 스레드에서 불리니 화면은 호출부가 넘겨야 한다.
    public static async Task ChatAsync(string key, string model, IEnumerable<Message> history, Action<string> onDelta, CancellationToken ct)
    {
        var payload = new
        {
            model,
            messages = new[] { new { role = "system", content = SystemPrompt } }
                .Concat(history.Select(m => new { role = m.Role, content = m.Content })).ToArray(),
            max_tokens = 4096,
            temperature = 0.4,
            stream = true,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, Root + "/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) throw await FailureAsync(res, ct);

        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var data = line[6..].Trim();
            if (data == "[DONE]") continue;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content) && content.GetString() is { Length: > 0 } text)
                {
                    onDelta(text);
                }
            }
            catch (JsonException) { /* 조각난 JSON 은 버린다 */ }
            catch (KeyNotFoundException) { }
            catch (InvalidOperationException) { }
        }
    }

    static async Task<Exception> FailureAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct);
        var hint = (int)res.StatusCode == 401 ? " — 키를 확인하세요" : "";
        return new InvalidOperationException("HTTP " + (int)res.StatusCode + hint + " — " + body[..Math.Min(300, body.Length)]);
    }
}
