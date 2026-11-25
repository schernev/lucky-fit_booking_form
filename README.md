# Orange Fitness – записване

One-button app that registers selected users for a group class at Orange Fitness Sofia
(https://www.orangefitness.bg/sofia/grafik).

It calls the same JSON API as the site's LuckyFit booking widget instead of clicking through the page:

1. `GET /lessons_form?uuid=…` – API key, CSRF token, session cookie
2. `GET /lesson_calendars/feed` – finds the next class with the configured name (and optional time)
3. `GET /clients/accessible`, `GET /clients` – phone lookup (as the widget does)
4. `POST /clients` – the Име / Фамилия / Email / Телефон form (when the phone is not found)
5. `POST /lesson_calendar_bookings` – the booking

## Projects

- `src/OrangeFitness.Core` – API client, settings, booking workflow (shared by all platforms)
- `src/OrangeFitness.App` – .NET MAUI app (Windows now, Android later)
- `src/OrangeFitness.Cli` – console runner for checks; always test mode unless `--live`

## Settings

### Default values (local, not in git)

The initial values (form URL, class name, time, …) come from `appsettings.local.json` in the repo root.
That file is git-ignored; `appsettings.example.json` shows its structure. After cloning:

```powershell
Copy-Item appsettings.example.json appsettings.local.json
```

Then fill in your values and rebuild – the file is embedded into `OrangeFitness.Core` at build time,
so it works the same on Windows, Android and in the CLI. Any field you omit keeps the built-in value
(empty text, 14 days, test mode on). Fields the user has already saved in the app win over the defaults.

### User settings

Edited in the app (Настройки). Stored as JSON in the app data folder
(Windows: `%LOCALAPPDATA%\User Name\bg.orangefitness.register\Data\settings.json`).
**Test mode is on by default** – nothing is sent to the site until you switch it off.
Each selected user must have their own phone number.

## Build & run (Windows)

```powershell
dotnet build src/OrangeFitness.App -c Release
dotnet run --project src/OrangeFitness.App
```

## Android (later)

```powershell
dotnet workload install maui-android
dotnet build src/OrangeFitness.App -c Release -f net11.0-android -p:EnableAndroid=true
```
Requires the Android SDK + JDK (Visual Studio Installer → ".NET Multi-platform App UI development" installs them).
