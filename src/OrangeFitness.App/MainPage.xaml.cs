using OrangeFitness.Core;

namespace OrangeFitness.App;

public partial class MainPage : ContentPage
{
    public static string SettingsPath => Path.Combine(FileSystem.AppDataDirectory, "settings.json");

    AppSettings _settings = new();

    public MainPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _settings = SettingsStore.Load(SettingsPath);
        Render();
    }

    void Render()
    {
        var time = string.IsNullOrWhiteSpace(_settings.ClassTime) ? "" : $" ({_settings.ClassTime.Trim()})";
        ClassLabel.Text = $"{_settings.ClassName}{time} – следващото занимание";
        TestModeLabel.IsVisible = _settings.TestMode;

        UsersPanel.Children.Clear();
        if (_settings.Users.Count == 1)
        {
            // Only one person – nothing to choose, so no checkbox.
            UsersPanel.Children.Add(new Label { Text = _settings.Users[0].DisplayName(0), FontSize = 18, HorizontalTextAlignment = TextAlignment.Center });
            return;
        }
        for (var i = 0; i < _settings.Users.Count; i++)
        {
            var user = _settings.Users[i];
            var box = new CheckBox { IsChecked = user.Selected, VerticalOptions = LayoutOptions.Center };
            box.CheckedChanged += (_, e) =>
            {
                user.Selected = e.Value;
                SettingsStore.Save(SettingsPath, _settings);
            };
            var label = new Label { Text = user.DisplayName(i), FontSize = 18, VerticalOptions = LayoutOptions.Center };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => box.IsChecked = !box.IsChecked;
            label.GestureRecognizers.Add(tap);
            UsersPanel.Children.Add(new HorizontalStackLayout { Spacing = 6, Children = { box, label } });
        }
    }

    async void OnSettingsClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new SettingsPage());

    async void OnRegisterClicked(object? sender, EventArgs e)
    {
        var problems = BookingService.Validate(_settings);
        if (problems.Count > 0)
        {
            await DisplayAlertAsync("Попълнете настройките", string.Join("\n", problems), "OK");
            return;
        }

        var names = string.Join(", ", _settings.Users.Select((u, i) => (u, i)).Where(x => _settings.IsSelected(x.u)).Select(x => x.u.DisplayName(x.i)));
        if (!_settings.TestMode &&
            !await DisplayAlertAsync("Потвърждение", $"Да запиша ли {names} за {_settings.ClassName}?", "Запиши", "Отказ"))
            return;

        RegisterButton.IsEnabled = false;
        LogLabel.Text = "";
        LogBorder.IsVisible = true;
        try
        {
            var service = new BookingService(_settings, Log);
            var results = await Task.Run(() => service.RunAsync());
            if (results.Count > 0)
            {
                var summary = string.Join("\n", results.Select(r => $"{(r.Ok ? "✔" : "✖")} {r.User}: {r.Message}"));
                await DisplayAlertAsync(results.All(r => r.Ok) ? "Готово" : "Има проблем", summary, "OK");
            }
        }
        catch (Exception ex)
        {
            Log("✖ Грешка: " + ex.Message);
            await DisplayAlertAsync("Грешка", ex.Message, "OK");
        }
        finally
        {
            RegisterButton.IsEnabled = true;
        }
    }

    void Log(string line) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        LogLabel.Text += (LogLabel.Text?.Length > 0 ? "\n" : "") + line;
        await LogScroll.ScrollToAsync(LogLabel, ScrollToPosition.End, false);
    });
}
