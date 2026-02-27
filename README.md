# Transcriber v1.2

Aktualna wersja aplikacji to `AlwaysOnTopTranscriber.Hybrid` (MAUI Blazor Hybrid, Windows-first).  
Legacy WPF zostało usunięte z solution/repo.

## Co jest w solution
- `src/AlwaysOnTopTranscriber.Hybrid` - aplikacja desktopowa (UI + system integration).
- `src/AlwaysOnTopTranscriber.Core` - logika transkrypcji, zapis, modele, settings.
- `src/AlwaysOnTopTranscriber.Tests` - testy jednostkowe.

## Wymagania
1. Windows 10/11.
2. .NET SDK 8.0.
3. Workload MAUI dla Windows:

```powershell
dotnet workload install maui-windows --skip-manifest-update
```

## Uruchomienie lokalne (krok po kroku)
1. Wejdź do katalogu projektu.
2. Przywróć paczki:

```powershell
dotnet restore AlwaysOnTopTranscriber.sln
```

3. Zbuduj:

```powershell
dotnet build AlwaysOnTopTranscriber.sln
```

4. Uruchom aplikację:

```powershell
dotnet run --project src/AlwaysOnTopTranscriber.Hybrid/AlwaysOnTopTranscriber.Hybrid.csproj
```

Uwaga: jeśli nie masz dostępu do internetu, może pojawić się warning `NU1900` (audit feed NuGet). Nie blokuje uruchomienia.

## Dane aplikacji
Domyślnie:
- `%AppData%\Transcriber\`

W środku:
- `app.db`
- `settings.json`
- `logs\`
- `models\`
- `transcripts\` (`.md`, `.json`, `.txt`)

Nadpisanie ścieżki:
- zmienna środowiskowa `TRANSCRIBER_DATA_DIR`

## Modele Whisper
- Pobieranie modeli jest dostępne z poziomu ekranu `Settings`.
- Możesz też wskazać własny plik modelu (`.bin`) w ustawieniach.
- Domyślny katalog modeli: `%AppData%\Transcriber\models`.

## Automatyczna instalacja (aplikacja + skrót na pulpicie)
Installer publikuje aplikację, kopiuje ją do lokalnego katalogu użytkownika i tworzy skróty:
- pulpit użytkownika,
- menu Start.

Uruchom:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-transcriber.ps1
```

Wariant przez `.cmd`:

```powershell
.\scripts\install-transcriber.cmd
```

Domyślna ścieżka instalacji:
- `%LocalAppData%\Programs\Transcriber v1.2`

## Testy
```powershell
dotnet test src/AlwaysOnTopTranscriber.Tests/AlwaysOnTopTranscriber.Tests.csproj
```

## Szybki start Git (nowe repo)
Jeśli to nowy folder bez `.git`:

```powershell
git init
git branch -M main
git add .
git commit -m "release: transcriber v1.2 hybrid"
git remote add origin <TWOJ_URL_REPO>
git push -u origin main
```
