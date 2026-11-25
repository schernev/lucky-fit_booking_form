namespace OrangeFitness.Core;

public sealed record UserResult(string User, bool Ok, string Message);

/// <summary>The one-button workflow: find the next matching class, then register every selected user for it.</summary>
public sealed class BookingService(AppSettings settings, Action<string> log)
{
    static readonly TimeZoneInfo Sofia = FindSofia();

    public static DateTime NowInSofia() => TimeZoneInfo.ConvertTime(DateTime.UtcNow, Sofia);

    /// <summary>Validates the settings for the selected users. Returns a list of problems (empty = OK).</summary>
    public static List<string> Validate(AppSettings s)
    {
        var problems = new List<string>();
        if (!Uri.TryCreate(s.FormUrl?.Trim(), UriKind.Absolute, out _)) problems.Add("Не е зададен (валиден) адрес на формата за записване.");
        if (string.IsNullOrWhiteSpace(s.ClassName)) problems.Add("Не е зададено име на занимание.");
        if (s.ClassTime.Trim() is { Length: > 0 } t && !TimeOnly.TryParse(t, out _))
            problems.Add($"Невалиден час \"{t}\" (очаква се ЧЧ:ММ).");

        var selected = s.Users.Select((u, i) => (u, i)).Where(x => s.IsSelected(x.u)).ToList();
        if (selected.Count == 0) problems.Add("Не е избран нито един потребител.");
        foreach (var (u, i) in selected)
        {
            var who = u.DisplayName(i);
            if (string.IsNullOrWhiteSpace(u.FirstName)) problems.Add($"{who}: липсва Име.");
            if (string.IsNullOrWhiteSpace(u.LastName)) problems.Add($"{who}: липсва Фамилия.");
            if (u.PhoneDigits.Length < 6) problems.Add($"{who}: липсва/невалиден телефонен номер.");
            if (!string.IsNullOrWhiteSpace(u.Email) && !u.Email.Contains('@')) problems.Add($"{who}: невалиден Email.");
        }
        // The site identifies people by phone: a second person with the same phone would be booked
        // as the first person's client record, so two selected users must have different phones.
        foreach (var g in selected.GroupBy(x => x.u.PhoneDigits).Where(g => g.Key != "" && g.Count() > 1))
            problems.Add($"Избраните потребители са с еднакъв телефон ({g.First().u.Phone}). Сайтът разпознава клиентите по телефон, затова всеки трябва да е с различен номер.");
        return problems;
    }

    public async Task<Lesson?> FindNextLessonAsync(LuckyFitClient client, CancellationToken ct = default)
    {
        var now = NowInSofia();
        TimeOnly? time = TimeOnly.TryParse(settings.ClassTime.Trim(), out var t) ? t : null;
        var name = settings.ClassName.Trim();

        for (var d = 0; d < Math.Max(1, settings.SearchDays); d++)
        {
            var day = DateOnly.FromDateTime(now).AddDays(d);
            var lessons = await client.GetLessonsAsync(day, ct);
            var match = lessons
                .Where(l => string.Equals(l.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase))
                .Where(l => time is null || TimeOnly.FromDateTime(l.Start) == time)
                .Where(l => l.Start > now)
                .OrderBy(l => l.Start)
                .FirstOrDefault();
            if (match is not null) return match;
        }
        return null;
    }

    public async Task<List<UserResult>> RunAsync(CancellationToken ct = default)
    {
        var results = new List<UserResult>();
        var problems = Validate(settings);
        if (problems.Count > 0)
        {
            foreach (var p in problems) log("⚠ " + p);
            return results;
        }

        using var client = new LuckyFitClient(settings.FormUrl);
        log("Свързване със сайта…");
        await client.InitAsync(ct);

        log($"Търсене на \"{settings.ClassName}\"{(settings.ClassTime.Trim() != "" ? " в " + settings.ClassTime.Trim() : "")} в следващите {settings.SearchDays} дни…");
        var lesson = await FindNextLessonAsync(client, ct);
        if (lesson is null)
        {
            log("✖ Не е намерено предстоящо занимание с това име.");
            return results;
        }
        log("Намерено: " + lesson);
        if (lesson.FreeCount <= 0)
        {
            log("✖ Няма свободни места.");
            return results;
        }
        if (settings.TestMode)
            log("ТЕСТОВ РЕЖИМ – нищо няма да бъде изпратено към сайта.");

        for (var i = 0; i < settings.Users.Count; i++)
        {
            var u = settings.Users[i];
            if (!settings.IsSelected(u)) continue;
            var who = u.DisplayName(i);
            try
            {
                results.Add(await RegisterAsync(client, u, who, lesson, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log($"✖ {who}: {ex.Message}");
                results.Add(new UserResult(who, false, ex.Message));
            }
        }
        return results;
    }

    async Task<UserResult> RegisterAsync(LuckyFitClient client, UserProfile u, string who, Lesson lesson, CancellationToken ct)
    {
        var clientId = await client.FindClientIdAsync(u.PhoneDigits, ct);
        if (clientId is null)
        {
            log($"{who}: телефонът не е в базата – ще се попълни формата Име/Фамилия/Email/Телефон.");
            if (settings.TestMode)
            {
                log($"  [тест] POST /clients  name={u.FirstName}, surname={u.LastName}, email={u.Email}, phone={u.Phone}, city_id={client.CityId}");
                log($"  [тест] POST /lesson_calendar_bookings  lesson={lesson.Id}, template={client.TemplateUuid}");
                return new UserResult(who, true, "тест – нищо не е изпратено");
            }
            clientId = await client.CreateClientAsync(u, ct);
            log($"  Клиентът е създаден (id {clientId}).");
        }
        else
        {
            log($"{who}: телефонът е намерен в базата (клиент id {clientId}) – записва се директно.");
            if (settings.TestMode)
            {
                log($"  [тест] POST /lesson_calendar_bookings  client={clientId}, lesson={lesson.Id}");
                return new UserResult(who, true, "тест – нищо не е изпратено");
            }
        }

        await client.BookAsync(clientId.Value, lesson.Id, ct);
        log($"✔ {who} е записан(а) за {lesson.Name.Trim()} на {lesson.Start:dd.MM HH:mm}.");
        return new UserResult(who, true, "записан(а)");
    }

    static TimeZoneInfo FindSofia()
    {
        foreach (var id in new[] { "Europe/Sofia", "FLE Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception) { }
        }
        return TimeZoneInfo.Local;
    }
}
