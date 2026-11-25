using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrangeFitness.Core;

public sealed record Lesson(
    long Id, DateTime Start, DateTime End, string Name, int FreeCount,
    string StaffName, string LocationName, string Price)
{
    public override string ToString() =>
        $"{Name.Trim()} — {Start:yyyy-MM-dd HH:mm}-{End:HH:mm}, {StaffName}, {LocationName}, свободни места: {FreeCount}";
}

public sealed class LuckyFitException(string message) : Exception(message);

/// <summary>
/// Talks to the LuckyFit JSON API the same way the site's booking widget
/// (lessons_form.js) does:
///   1. GET  /lesson_calendars/feed?start=..&amp;end=..         – list classes for a day
///   2. GET  /clients/accessible?params={"phone#phone1":".."} – is this phone a known client?
///      GET  /clients?params={"phone#phone2":".."}            – (fallback lookup)
///   3. POST /clients                                          – new client form (Име, Фамилия, Email, Телефон)
///   4. POST /lesson_calendar_bookings                         – the booking itself
///   5. POST /self_service_forms/send_notification             – notification, as the widget does
/// </summary>
public sealed partial class LuckyFitClient : IDisposable
{
    readonly HttpClient _http;
    readonly Uri _formUri;

    public string ApiKey { get; private set; } = "";
    public string TemplateUuid { get; private set; } = "";
    public string CityId { get; private set; } = "960";
    public string Sex { get; private set; } = "0";
    string _csrf = "";

    public LuckyFitClient(string formUrl)
    {
        _formUri = new Uri(formUrl);
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(_formUri.GetLeftPart(UriPartial.Authority)),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) OrangeFitnessRegister/1.0");
        _http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
    }

    /// <summary>Loads the widget page to obtain the API key, CSRF token, session cookie and hidden form values.</summary>
    public async Task InitAsync(CancellationToken ct = default)
    {
        var html = await _http.GetStringAsync(_formUri, ct);

        ApiKey = Match(SetApiKeyRx(), html) ?? Match(DataApiKeyRx(), html)
            ?? throw new LuckyFitException("Не открих API ключа във формата на сайта (setApiKey). Сайтът може да е променен.");
        _csrf = Match(CsrfRx(), html) ?? "";
        TemplateUuid = Match(TemplateRx(), html)
            ?? System.Web.HttpUtility.ParseQueryString(_formUri.Query)["uuid"] ?? "";
        CityId = Match(CityRx(), html) ?? CityId;
        Sex = Match(SexRx(), html) ?? Sex;

        _http.DefaultRequestHeaders.Remove("Api-Key");
        _http.DefaultRequestHeaders.Add("Api-Key", ApiKey);
        if (_csrf != "")
        {
            _http.DefaultRequestHeaders.Remove("X-CSRF-Token");
            _http.DefaultRequestHeaders.Add("X-CSRF-Token", _csrf);
        }
        _http.DefaultRequestHeaders.Referrer = _formUri;
    }

    public async Task<IReadOnlyList<Lesson>> GetLessonsAsync(DateOnly day, CancellationToken ct = default)
    {
        var url = $"/lesson_calendars/feed?start={day:yyyy-MM-dd}&end={day.AddDays(1):yyyy-MM-dd}";
        var json = await _http.GetStringAsync(url, ct);
        var items = JsonSerializer.Deserialize<List<FeedItem>>(json) ?? [];
        return items.Select(i => new Lesson(
            i.Id,
            DateTime.Parse(i.Start, CultureInfo.InvariantCulture),
            DateTime.Parse(i.End, CultureInfo.InvariantCulture),
            i.LessonName ?? "", i.FreeCount, i.StaffName ?? "", i.LocationName ?? "",
            i.Price?.ToString() ?? "")).ToList();
    }

    /// <summary>Returns the client id registered with this phone, or null (same two lookups as the widget).</summary>
    public async Task<long?> FindClientIdAsync(string phoneDigits, CancellationToken ct = default)
    {
        foreach (var (path, key) in new[] { ("/clients/accessible", "phone#phone1"), ("/clients", "phone#phone2") })
        {
            var p = Uri.EscapeDataString(JsonSerializer.Serialize(new Dictionary<string, string> { [key] = phoneDigits }));
            var json = await _http.GetStringAsync($"{path}?params={p}", ct);
            var resp = JsonSerializer.Deserialize<ApiResponse<List<IdOnly>>>(json);
            if (resp is { Success: false })
                throw new LuckyFitException($"Грешка при търсене на клиент: {resp.Error}");
            if (resp?.Data is { Count: > 0 } list) return list[0].Id;
        }
        return null;
    }

    public async Task<long> CreateClientAsync(UserProfile u, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client[name]"] = u.FirstName.Trim(),
            ["client[surname]"] = u.LastName.Trim(),
            ["client[email1]"] = u.Email.Trim(),
            ["client[phone1]"] = u.Phone.Trim(),
            ["client[city_id]"] = CityId,
            ["client[sex]"] = Sex,
        };
        var json = await PostAsync("/clients", form, ct);
        var resp = JsonSerializer.Deserialize<ApiResponse<IdOnly>>(json);
        if (resp is not { Success: true, Data: not null })
            throw new LuckyFitException($"Сайтът отказа регистрацията на клиент: {resp?.Error ?? json}");
        return resp.Data.Id;
    }

    public async Task BookAsync(long clientId, long lessonId, CancellationToken ct = default)
    {
        var form = BookingForm(clientId, lessonId);
        var json = await PostAsync("/lesson_calendar_bookings", form, ct);
        EnsureNotError(json, "Сайтът отказа записването");

        // The widget fires this after a successful booking; failure here doesn't undo the booking.
        form["lesson_kind"] = "group";
        try { await PostAsync($"/self_service_forms/send_notification?uuid={ApiKey}", form, ct); }
        catch (Exception) { /* best effort, same as the widget */ }
    }

    public Dictionary<string, string> BookingForm(long clientId, long lessonId) => new()
    {
        ["lesson_calendar_booking[client_id]"] = clientId.ToString(CultureInfo.InvariantCulture),
        ["lesson_calendar_booking[lesson_calendar_id]"] = lessonId.ToString(CultureInfo.InvariantCulture),
        ["lesson_calendar_booking[template_uuid]"] = TemplateUuid,
    };

    async Task<string> PostAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new LuckyFitException($"HTTP {(int)resp.StatusCode} от {url}: {Truncate(body)}");
        return body;
    }

    static void EnsureNotError(string json, string what)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
            {
                var err = doc.RootElement.TryGetProperty("error", out var e) ? e.ToString() : json;
                throw new LuckyFitException($"{what}: {err}");
            }
        }
        catch (JsonException) { /* non-JSON 200 response is treated as success, like the widget */ }
    }

    static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
    static string? Match(Regex rx, string html) { var m = rx.Match(html); return m.Success ? m.Groups[1].Value : null; }

    [GeneratedRegex(@"setApiKey\('([0-9a-fA-F-]+)'\)")] private static partial Regex SetApiKeyRx();
    [GeneratedRegex(@"data-apikey=""([0-9a-fA-F-]+)""")] private static partial Regex DataApiKeyRx();
    [GeneratedRegex(@"<meta name=""csrf-token"" content=""([^""]+)""")] private static partial Regex CsrfRx();
    [GeneratedRegex(@"value=""([0-9a-fA-F-]+)""[^>]*id=""group_template_uuid""")] private static partial Regex TemplateRx();
    [GeneratedRegex(@"name=""client\[city_id\]"" value=""(\d+)""")] private static partial Regex CityRx();
    [GeneratedRegex(@"name=""client\[sex\]"" value=""(\d+)""")] private static partial Regex SexRx();

    public void Dispose() => _http.Dispose();

    sealed class FeedItem
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("start")] public string Start { get; set; } = "";
        [JsonPropertyName("end")] public string End { get; set; } = "";
        [JsonPropertyName("lesson_name")] public string? LessonName { get; set; }
        [JsonPropertyName("free_count")] public int FreeCount { get; set; }
        [JsonPropertyName("staff_name")] public string? StaffName { get; set; }
        [JsonPropertyName("location_name")] public string? LocationName { get; set; }
        [JsonPropertyName("price")] public JsonElement? Price { get; set; }
    }

    sealed class ApiResponse<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("data")] public T? Data { get; set; }
        [JsonPropertyName("error")] public JsonElement? ErrorRaw { get; set; }
        public string Error => ErrorRaw?.ToString() ?? "";
    }

    sealed class IdOnly
    {
        [JsonPropertyName("id")] public long Id { get; set; }
    }
}
