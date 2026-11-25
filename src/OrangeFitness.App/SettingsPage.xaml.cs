using OrangeFitness.Core;

namespace OrangeFitness.App;

public partial class SettingsPage : ContentPage
{
    readonly AppSettings _settings;
    readonly List<UserEditor> _editors = [];

    sealed record UserEditor(UserProfile Source, Entry First, Entry Last, Entry Email, Entry Phone)
    {
        public Border View { get; set; } = null!;
        public Label Title { get; set; } = null!;
        public Button Remove { get; set; } = null!;
    }

    public SettingsPage()
    {
        InitializeComponent();
        _settings = SettingsStore.Load(MainPage.SettingsPath);

        ClassNameEntry.Text = _settings.ClassName;
        ClassTimeEntry.Text = _settings.ClassTime;
        SearchDaysEntry.Text = _settings.SearchDays.ToString();
        TestModeSwitch.IsToggled = _settings.TestMode;
        FormUrlEntry.Text = _settings.FormUrl;

        foreach (var u in _settings.Users) AddEditor(u);
        RefreshEditors();

        foreach (var entry in new[] { ClassNameEntry, ClassTimeEntry, SearchDaysEntry, FormUrlEntry })
            entry.TextChanged += (_, _) => UpdateDirty();
        TestModeSwitch.Toggled += (_, _) => UpdateDirty();

        _initialSnapshot = Snapshot();
        UpdateDirty();
    }

    string _initialSnapshot = "";

    /// <summary>Everything editable on the page, as one string, so "dirty" = differs from the state on open.</summary>
    string Snapshot() => string.Join("\u001f",
        new[] { ClassNameEntry.Text, ClassTimeEntry.Text, SearchDaysEntry.Text, TestModeSwitch.IsToggled.ToString(), FormUrlEntry.Text }
            .Concat(_editors.SelectMany(ed => new[] { ed.First.Text, ed.Last.Text, ed.Email.Text, ed.Phone.Text }))
            .Select(s => (s ?? "").Trim()))
        + "\u001e" + _editors.Count;

    /// <summary>Not dirty: "Изход" + disabled "Запази". Dirty: "Отказ" + enabled "Запази".</summary>
    void UpdateDirty()
    {
        if (_initialSnapshot == "") return; // still building the page
        var dirty = Snapshot() != _initialSnapshot;
        CancelButton.Text = dirty ? "Отказ" : "Изход";
        SaveButton.IsEnabled = dirty;
        SaveButton.Opacity = dirty ? 1 : 0.4;
    }

    void AddEditor(UserProfile u)
    {
        var ed = new UserEditor(
            u,
            new Entry { Text = u.FirstName },
            new Entry { Text = u.LastName },
            new Entry { Text = u.Email, Keyboard = Keyboard.Email, Placeholder = "email@example.com" },
            new Entry { Text = u.Phone, Keyboard = Keyboard.Telephone });
        foreach (var entry in new[] { ed.First, ed.Last, ed.Email, ed.Phone })
            entry.TextChanged += (_, _) => UpdateDirty();

        ed.Title = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
        ed.Remove = new Button
        {
            Text = "✕", Padding = new Thickness(0), WidthRequest = 36, HeightRequest = 32, FontSize = 14,
            BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.End,
        };
        ed.Remove.SetAppThemeColor(Button.TextColorProperty, Colors.Black, Colors.White);
        ToolTipProperties.SetText(ed.Remove, "Премахни потребителя");
        ed.Remove.Clicked += (_, _) =>
        {
            _editors.Remove(ed);
            UsersPanel.Children.Remove(ed.View);
            RefreshEditors();
        };

        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        header.Add(ed.Title);
        header.Add(ed.Remove, 1);

        ed.View = new Border
        {
            Padding = new Thickness(12, 8, 12, 12),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { header, Row("Име", ed.First), Row("Фамилия", ed.Last), Row("Email", ed.Email), Row("Телефонен номер", ed.Phone) },
            },
        };
        ed.View.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#E0E0E0"), Color.FromArgb("#444444"));

        _editors.Add(ed);
        UsersPanel.Children.Add(ed.View);
    }

    /// <summary>Renumbers the user blocks; the remove button is hidden while only one user is left.</summary>
    void RefreshEditors()
    {
        for (var i = 0; i < _editors.Count; i++)
        {
            _editors[i].Title.Text = $"Потребител {i + 1}";
            _editors[i].Remove.IsVisible = _editors.Count > 1;
        }
        UpdateDirty();
    }

    void OnAddUserClicked(object? sender, EventArgs e)
    {
        AddEditor(new UserProfile { Selected = true });
        RefreshEditors();
        _editors[^1].First.Focus();
    }

    static Grid Row(string label, View input)
    {
        var g = new Grid { ColumnDefinitions = [new ColumnDefinition(140), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 10 };
        g.Add(new Label { Text = label, VerticalOptions = LayoutOptions.Center });
        g.Add(input, 1);
        return g;
    }

    async void OnCancelClicked(object? sender, EventArgs e) => await Navigation.PopAsync();

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        _settings.ClassName = ClassNameEntry.Text?.Trim() ?? "";
        _settings.ClassTime = ClassTimeEntry.Text?.Trim() ?? "";
        _settings.SearchDays = int.TryParse(SearchDaysEntry.Text, out var d) && d > 0 ? d : 14;
        _settings.TestMode = TestModeSwitch.IsToggled;
        if (!string.IsNullOrWhiteSpace(FormUrlEntry.Text)) _settings.FormUrl = FormUrlEntry.Text.Trim();

        _settings.Users = _editors.Select(ed =>
        {
            var u = ed.Source;
            u.FirstName = ed.First.Text?.Trim() ?? "";
            u.LastName = ed.Last.Text?.Trim() ?? "";
            u.Email = ed.Email.Text?.Trim() ?? "";
            u.Phone = ed.Phone.Text?.Trim() ?? "";
            return u;
        }).ToList();
        _settings.NormalizeUsers();
        if (_settings.Users.Count == 1) _settings.Users[0].Selected = true;

        SettingsStore.Save(MainPage.SettingsPath, _settings);
        await Navigation.PopAsync();
    }
}
