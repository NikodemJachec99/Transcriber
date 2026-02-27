# Transcriber v1.2

Prosta aplikacja desktopowa do transkrypcji audio na Windows.

## Szybki wybór
1. Chcesz po prostu zainstalować i używać? Skorzystaj z `Instalacja automatyczna (polecana)`.
2. Chcesz uruchamiać z kodu i samodzielnie budować aplikację? Skorzystaj z `Instalacja ręczna`.

## 1) Jak pobrać projekt
1. Otwórz PowerShell.
2. Wejdź do folderu, gdzie chcesz mieć projekt.
3. Wklej:

```powershell
git clone https://github.com/NikodemJachec99/Transcriber.git
cd Transcriber
```

## 2) Instalacja automatyczna (polecana)
Ta opcja:
- buduje aplikację,
- instaluje ją lokalnie,
- instaluje wymagany `Windows App Runtime` (jeśli go brakuje),
- tworzy skrót na pulpicie i w menu Start.

Kroki:
1. Otwórz PowerShell w folderze projektu.
2. Uruchom:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-transcriber.ps1
```

Możesz też użyć:

```powershell
.\scripts\install-transcriber.cmd
```

Domyślna ścieżka instalacji:
- `%LocalAppData%\Programs\Transcriber v1.2`

## 3) Instalacja ręczna (dla dewelopera)
Wymagania:
1. Windows 10/11
2. .NET SDK 8.0
3. MAUI workload

Instalacja zależności:

```powershell
dotnet workload install maui-windows --skip-manifest-update
```

Budowanie i uruchomienie:

```powershell
dotnet restore AlwaysOnTopTranscriber.sln
dotnet build AlwaysOnTopTranscriber.sln
dotnet run --project src/AlwaysOnTopTranscriber.Hybrid/AlwaysOnTopTranscriber.Hybrid.csproj
```

## 4) Jak pobrać model do transkrypcji
1. Otwórz aplikację.
2. Przejdź do `Settings`.
3. Wybierz model z listy i kliknij pobieranie.
4. Po pobraniu ustaw model jako aktywny.

Możesz też wskazać własny plik `.bin`.

## 5) Gdzie zapisują się pliki
Domyślny katalog danych:
- `%AppData%\Transcriber\`

Najważniejsze foldery:
- `models\` - modele Whisper
- `transcripts\` - zapisane transkrypcje (`.txt`, `.md`, `.json`)
- `logs\` - logi aplikacji

## 6) Stabilność długich nagrań (np. 4h na 8 GB RAM)
- Aplikacja ma limit bufora audio, więc RAM nie rośnie bez końca.
- Przy przeciążeniu CPU aplikacja pomija nadmiar audio zamiast się wywalać.
- Dla długich sesji ustaw model `tiny` albo `base` i chunk `10-15s`.

## 7) Najczęstszy problem
`NU1900` podczas `dotnet restore`/`dotnet build`:
- to zwykle brak dostępu do feedu NuGet (internet/proxy),
- warning nie blokuje uruchomienia aplikacji.
