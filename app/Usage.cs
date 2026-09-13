// 사용량 — 대화 파일(chats.json)에서 그때그때 센다. 따로 기록하지 않는다.
// 이 기능 전에 쌓인 대화에는 시각·모델·토큰이 없어(0) 날짜 칸과 토큰 합계에는 안 잡힌다.

using System.Drawing.Drawing2D;

namespace NvidiaConsole;

sealed record ModelUse(string Model, int Messages, long Tokens);

sealed record Usage(
    int Sessions, int Messages, long Tokens, int ActiveDays, int Streak, int Longest,
    int? PeakHour, string TopModel, Dictionary<DateOnly, int> PerDay, List<ModelUse> Models);

static class UsageStats
{
    public static Usage Compute(IReadOnlyList<Chat> chats, int? days, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var from = days is int d ? today.AddDays(1 - d) : DateOnly.MinValue;
        bool Inside(Message m) => days is null || (m.At > 0 && Day(m) >= from);

        var all = chats.SelectMany(c => c.Messages).Where(Inside).ToList();
        var stamped = all.Where(m => m.At > 0).ToList();
        var perDay = stamped.GroupBy(Day).ToDictionary(g => g.Key, g => g.Count());
        var (streak, longest) = Streaks(perDay.Keys, today);

        int? peak = stamped.Count == 0 ? null
            : stamped.GroupBy(m => Local(m).Hour).OrderByDescending(g => g.Count()).First().Key;

        var models = all.Where(m => m.Role == "assistant" && !string.IsNullOrEmpty(m.Model))
            .GroupBy(m => m.Model!)
            .Select(g => new ModelUse(g.Key, g.Count(), g.Sum(m => m.Tokens)))
            .OrderByDescending(u => u.Messages)
            .ToList();

        return new Usage(
            chats.Count(c => c.Messages.Any(Inside)), all.Count, all.Sum(m => m.Tokens),
            perDay.Count, streak, longest, peak, models.FirstOrDefault()?.Model ?? "—", perDay, models);
    }

    static DateTime Local(Message m) => DateTimeOffset.FromUnixTimeMilliseconds(m.At).LocalDateTime;
    static DateOnly Day(Message m) => DateOnly.FromDateTime(Local(m));

    // 오늘 아직 안 썼으면 어제까지 이어진 걸 현재 연속으로 본다 — 아침에 켜자마자 0 이 되면 이상하다
    public static (int Current, int Longest) Streaks(IEnumerable<DateOnly> active, DateOnly today)
    {
        var set = active.ToHashSet();
        var longest = 0;
        foreach (var day in set)
        {
            if (set.Contains(day.AddDays(-1))) continue;   // 이어진 줄의 첫날에서만 센다
            var n = 1;
            while (set.Contains(day.AddDays(n))) n++;
            longest = Math.Max(longest, n);
        }

        var start = set.Contains(today) ? today : today.AddDays(-1);
        var current = 0;
        while (set.Contains(start.AddDays(-current))) current++;
        return (current, longest);
    }
}

// 빈 대화 화면. 글자를 컨트롤로 두지 않고 직접 칠하므로 드래그로 선택되지 않는다.
sealed class UsagePanel : Control
{
    public Func<IReadOnlyList<Chat>> Source = () => Array.Empty<Chat>();
    public string? Hint;

    public bool ModelsTab;   // 화면 확인(--shot)에서 모델 탭을 바로 띄울 수 있게
    int? days;
    readonly List<(Rectangle Area, Action Click)> hits = new();

    static readonly Font Value = new("Malgun Gothic", 12F, FontStyle.Bold);
    static readonly Color[] Heat =
    {
        Color.FromArgb(0x8f, 0xb4, 0xf0), Color.FromArgb(0x6a, 0x9b, 0xed),
        Color.FromArgb(0x4a, 0x7f, 0xe6), Color.FromArgb(0x2f, 0x63, 0xd8),
    };

    public UsagePanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Bg;
        ForeColor = Ui.Fg;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = hits.Any(h => h.Area.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        foreach (var h in hits.ToList())
        {
            if (!h.Area.Contains(e.Location)) continue;
            h.Click();
            Invalidate();
            break;
        }
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        hits.Clear();

        var width = Math.Min(Width - 40, 660);
        var x = (Width - width) / 2;
        var y = Math.Max(24, Height / 2 - 190);

        if (Hint is not null)
        {
            TextRenderer.DrawText(g, Hint, Ui.Body, new Rectangle(x, y, width, 24), Ui.Fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            y += 44;
        }

        var u = UsageStats.Compute(Source(), days, DateTime.Now);

        var tx = Pill(g, "개요", x, y, !ModelsTab, () => ModelsTab = false, fromLeft: true);
        Pill(g, "모델", tx + 4, y, ModelsTab, () => ModelsTab = true, fromLeft: true);
        var rx = x + width;
        foreach (var (label, value) in new (string, int?)[] { ("7d", 7), ("30일", 30), ("전체", null) })
            rx = Pill(g, label, rx, y, days == value, () => days = value, fromLeft: false) - 4;
        y += 38;

        if (ModelsTab) PaintModels(g, u, x, y, width);
        else PaintOverview(g, u, x, y, width);
    }

    // 탭 한 칸. 누를 자리를 기록해 두고, 다음 칸이 붙을 가장자리를 돌려준다.
    int Pill(Graphics g, string text, int edge, int y, bool on, Action click, bool fromLeft)
    {
        var w = TextRenderer.MeasureText(text, Ui.Strong).Width + 16;
        var r = new Rectangle(fromLeft ? edge : edge - w, y, w, 28);
        if (on) Ui.Paint(g, r, 8, Ui.CardHi, Color.Transparent);
        TextRenderer.DrawText(g, text, on ? Ui.Strong : Ui.Body, r, on ? Ui.Fg : Ui.UserFg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        hits.Add((r, click));
        return fromLeft ? r.Right : r.Left;
    }

    void PaintOverview(Graphics g, Usage u, int x, int y, int width)
    {
        var cards = new (string Label, string Value)[]
        {
            ("세션", u.Sessions.ToString("N0")),       ("메시지", u.Messages.ToString("N0")),
            ("총 토큰", Compact(u.Tokens)),            ("활성 일수", u.ActiveDays.ToString("N0")),
            ("현재 연속 일수", u.Streak + "일"),        ("최장 연속 일수", u.Longest + "일"),
            ("최다 사용 시간", Hour(u.PeakHour)),       ("즐겨 사용한 모델", Short(u.TopModel)),
        };

        const int gap = 8, h = 58;
        var cw = (width - gap * 3) / 4;
        for (var i = 0; i < cards.Length; i++)
        {
            var r = new Rectangle(x + i % 4 * (cw + gap), y + i / 4 * (h + gap), cw, h);
            Ui.Paint(g, r, 8, Ui.Card, Color.Transparent);
            TextRenderer.DrawText(g, cards[i].Label, Ui.Meta, new Rectangle(r.X + 12, r.Y + 8, r.Width - 20, 18),
                Ui.UserFg, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, cards[i].Value, Value, new Rectangle(r.X + 12, r.Y + 26, r.Width - 20, 24),
                Ui.Fg, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
        y += 2 * h + gap + 14;

        // 잔디 — 세로 한 줄이 한 주(위가 일요일), 오른쪽 끝 줄에 오늘이 있다
        const int weeks = 27, cg = 3;
        var cell = (width - (weeks - 1) * cg) / weeks;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var first = today.AddDays(-(int)today.DayOfWeek - (weeks - 1) * 7);
        var max = u.PerDay.Count == 0 ? 1 : u.PerDay.Values.Max();

        for (var w = 0; w < weeks; w++)
        {
            for (var dow = 0; dow < 7; dow++)
            {
                var day = first.AddDays(w * 7 + dow);
                if (day > today) continue;
                var n = u.PerDay.GetValueOrDefault(day);
                var color = n == 0 ? Ui.Card : Heat[Math.Min(3, (n * 4 - 1) / max)];
                Ui.Paint(g, new Rectangle(x + w * (width - cell) / (weeks - 1), y + dow * (cell + cg), cell, cell), 3, color, Color.Transparent);
            }
        }
    }

    void PaintModels(Graphics g, Usage u, int x, int y, int width)
    {
        if (u.Models.Count == 0)
        {
            TextRenderer.DrawText(g, "아직 기록이 없습니다", Ui.Body, new Rectangle(x, y + 20, width, 24),
                Ui.UserFg, TextFormatFlags.HorizontalCenter);
            return;
        }

        var top = u.Models[0].Messages;
        foreach (var m in u.Models.Take(8))
        {
            var r = new Rectangle(x, y, width, 52);
            Ui.Paint(g, r, 8, Ui.Card, Color.Transparent);
            TextRenderer.DrawText(g, m.Model, Ui.Body, new Rectangle(r.X + 12, r.Y + 6, r.Width - 230, 22),
                Ui.Fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "메시지 " + m.Messages.ToString("N0") + " · 토큰 " + Compact(m.Tokens), Ui.Meta,
                new Rectangle(r.Right - 222, r.Y + 6, 210, 22), Ui.UserFg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            var track = new Rectangle(r.X + 12, r.Y + 34, r.Width - 24, 6);
            Ui.Paint(g, track, 3, Ui.CardHi, Color.Transparent);
            Ui.Paint(g, track with { Width = Math.Max(6, track.Width * m.Messages / top) }, 3, Heat[3], Color.Transparent);
            y += 60;
        }
    }

    static string Compact(long v) =>
        v >= 1_000_000 ? (v / 1_000_000d).ToString(v >= 10_000_000 ? "0" : "0.#") + "M"
        : v >= 100_000 ? (v / 1_000d).ToString("0") + "K"
        : v.ToString("N0");

    static string Hour(int? h) =>
        h is not int v ? "—" : v < 12 ? "오전 " + (v == 0 ? 12 : v) + "시" : "오후 " + (v == 12 ? 12 : v - 12) + "시";

    static string Short(string model) => model.Contains('/') ? model[(model.LastIndexOf('/') + 1)..] : model;
}
