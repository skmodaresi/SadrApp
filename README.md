# سامانه صدر — SadrApp

Accounting & project-management desktop application for Windows (Persian / RTL UI).

WPF on .NET 10, SQL Server (LocalDB or full) via Entity Framework Core, QuestPDF for invoice printing.

## Download (for testers)

Grab the ready-to-run offline installer from the
[**v1.1 release page**](https://github.com/skmodaresi/SadrApp/releases/download/v1.1/SadrApp-Setup-1.1.zip)
or browse all releases on the [Releases](https://github.com/skmodaresi/SadrApp/releases) page.

The zip contains the full offline setup — .NET 10 runtime and SQL Server 2022 LocalDB installers
included, no internet or preinstalled SQL Server needed:

1. Unzip anywhere.
2. Run `SadrSetup.exe` (accept the administrator prompt).
3. Pick the install folder and click شروع نصب — prerequisites, database, and shortcut are set up automatically.
4. On first launch, choose the main admin username/password, then log in.

Prerequisite: Microsoft Visual C++ Redistributable (required by LocalDB) — included in recent
Windows 11, otherwise install it from Microsoft.

## Features

- Projects and tasks with same-project prerequisites, task reports, and progress tracking
- Products with categories, brands, units, attributes, and **price history** (latest price feeds the invoice editor as default price + default discount)
- Invoices with live row totals, discounts, three print layouts, and PDF output
- Accounting trees (group / general / subsidiary / detail accounts), banks, customers & providers
- Users and roles with PBKDF2-hashed passwords; login lockout after 5 failed attempts
- Thousands-separated numeric input everywhere, Jalali (Persian) dates

## Offline installer for testers

`build-installer.ps1` publishes the app and the setup wizard into `dist/SadrApp-Setup/`:

```
SadrSetup.exe                            run as administrator
redist/windowsdesktop-runtime-10.0-*.exe .NET 10 Desktop Runtime
redist/SqlLocalDB-2022.msi               SQL Server 2022 LocalDB
app/                                     application payload
```

The wizard installs missing prerequisites silently, creates/starts the LocalDB instance,
creates the database, writes `Connection.dat` beside the app, and adds a Start-Menu shortcut.
On first launch the app creates its schema and asks for the main admin username/password.

To publish a new tester release, zip `dist/SadrApp-Setup/` and run:

```
powershell -ExecutionPolicy Bypass -File scripts/make-release.ps1 -Zip dist/SadrApp-Setup-<ver>.zip -Tag v<ver> -Title "SadrApp <ver>"
```

## Building from source

Requirements: .NET 10 SDK, Windows.

```
dotnet build SadrApp/SadrApp.csproj
dotnet run --project SadrApp
```

The database connection is read from `Connection.dat` next to the executable; without it the
app falls back to the default LocalDB instance (`(localdb)\MSSQLLocalDB`).

## Repository layout

| Path | Purpose |
|------|---------|
| `SadrApp/` | WPF application (Views, Data/EF Core entities, Infrastructure services) |
| `installer/SadrSetup/` | Setup wizard (WinForms on .NET Framework 4.7.2, runs before any runtime install) |
| `build-installer.ps1` | Produces the offline `dist/` package |
| `ui-*.ps1` | UIAutomation smoke tests |
