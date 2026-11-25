using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace OrangeFitness.Core;

/// <summary>
/// Everything the user can edit on the Settings screen.
/// Initial values come from <c>appsettings.local.json</c> (repo root, not in git – see
/// <c>appsettings.example.json</c>), which is embedded into the build; anything it omits keeps the
/// neutral value below.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The LuckyFit booking widget embedded
    public string FormUrl { get; set; } = "";

    /// <summary>Name of the group class to book (matched case-insensitively, trimmed).</summary>
    public string ClassName { get; set; } = "";

    /// <summary>Optional start time "HH:mm". Empty = any time.</summary>
    public string ClassTime { get; set; } = "";

    /// <summary>How many days ahead (including today) to look for the next class.</summary>
    public int SearchDays { get; set; } = 14;

    /// <summary>When true, nothing is POSTed to the site; the app only shows what it would send.</summary>
    public bool TestMode { get; set; } = true;

    /// <summary>People that can be booked. Always at least one entry (possibly empty).</summary>
    public List<UserProfile> Users { get; set; } = [new UserProfile { Selected = true }];

    /// <summary>With a single user there is no checkbox – that user is always booked.</summary>
    public bool IsSelected(UserProfile u) => Users.Count == 1 || u.Selected;

    /// <summary>Drops users with no data at all and makes sure at least one remains.</summary>
    public void NormalizeUsers()
    {
        Users.RemoveAll(u => u.IsEmpty);
        if (Users.Count == 0) Users.Add(new UserProfile { Selected = true });
    }
}

public sealed class UserProfile
{
    public bool Selected { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";

    public string DisplayName(int index) =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? $"Потребител {index + 1}"
            : $"{FirstName} {LastName}".Trim();

    public string PhoneDigits => new(Phone.Where(char.IsDigit).ToArray());

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName) &&
        string.IsNullOrWhiteSpace(Email) && string.IsNullOrWhiteSpace(Phone);
}

public static class SettingsStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    const string DefaultsResource = "appsettings.local.json";

    /// <summary>The defaults from the embedded appsettings.local.json (empty object when the file was absent at build time).</summary>
    static JsonObject LoadDefaults()
    {
        try
        {
            using var stream = typeof(SettingsStore).Assembly.GetManifestResourceStream(DefaultsResource);
            if (stream is not null && JsonNode.Parse(stream, documentOptions: JsonDocOptions) is JsonObject o) return o;
        }
        catch (JsonException) { /* broken defaults file – ignore it */ }
        return [];
    }

    static readonly JsonDocumentOptions JsonDocOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Settings as they are before the user has saved anything: the local defaults on top of the built-in values.</summary>
    public static AppSettings CreateDefault() => FromJson(LoadDefaults());

    /// <summary>Loads the user's settings; fields missing from the file are taken from the defaults.</summary>
    public static AppSettings Load(string path)
    {
        var merged = LoadDefaults();
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path), documentOptions: JsonDocOptions) is JsonObject saved)
                foreach (var (key, value) in saved.ToList())
                {
                    saved.Remove(key);
                    foreach (var k in merged.Select(p => p.Key).Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList())
                        merged.Remove(k);
                    merged[key] = value;
                }
        }
        catch (JsonException) { /* fall back to defaults */ }
        return FromJson(merged);
    }

    static AppSettings FromJson(JsonObject json)
    {
        AppSettings? s = null;
        try { s = json.Deserialize<AppSettings>(Options); }
        catch (JsonException) { }
        s ??= new AppSettings();
        s.Users ??= [];
        s.NormalizeUsers();
        return s;
    }

    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
