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
- buduje aplikację z kodu,
- instaluje ją lokalnie,
- tworzy skrót na pulpicie.

### Szybki start:
1. Otwórz **CMD** w folderze projektu
   - Shift + Right Click → Open PowerShell/CMD here
   - Lub: Start → cmd → Enter
2. Wpisz i naciśnij Enter:
```cmd
scripts\install-transcriber.cmd
```

**Co robi skrypt:**
- ✅ Sprawdza .NET SDK 8.0
- ✅ Instaluje MAUI workload (jeśli potrzeba)
- ✅ Buduje aplikację w Release mode
- ✅ Kopie do `%LocalAppData%\Programs\Transcriber v1.2`
- ✅ Tworzy skrót na pulpicie

**Jeśli pojawi się błąd `.NET SDK not found`:**
1. Pobierz i zainstaluj: https://dotnet.microsoft.com/download/dotnet/8.0
2. Zamknij i otwórz CMD ponownie
3. Uruchom skrypt jeszcze raz

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

## 6) Stabilność długich nagrań (5h+ na 8 GB RAM)
- Aplikacja ma limit bufora audio, więc RAM nie rośnie bez końca.
- Przy przeciążeniu CPU aplikacja pomija nadmiar audio zamiast się wywalać.
- **Dla długich sesji:**
  - Wyłącz **Live transkrypcję** w Ustawieniach (oszczędza CPU/RAM)
  - Ustaw model `tiny` lub `base`
  - Zwiększ chunk length do `15-20s`
- Live transkrypcja limituje wyświetlanie do ostatnich 50KB znaków dla wydajności
- Aplikacja zapisywać się będzie systematycznie, niezależnie od live transkrypcji

## 7) Troubleshooting

### Problem: Aplikacja zużywa zbyt dużo RAM / spowalnia się
**Rozwiązanie:**
1. Wyłącz **Live transkrypcję** w Ustawieniach
2. Wybierz lżejszy model (np. `tiny` zamiast `base`)
3. Zwiększ chunk length do `20s` lub `30s`
4. Zamknij inne aplikacje na komputerze

### Problem: `NU1900` podczas `dotnet restore`
- To zwykle brak dostępu do feedu NuGet (internet/proxy)
- To tylko warning, nie blokuje uruchomienia aplikacji

### Problem: "Windows App Runtime not found"
- Uruchom installer - automatycznie pobierze i zainstaluje wymagane zależności
- Lub pobierz ręcznie: https://aka.ms/windowsappruntimeinstall

### Problem: Transkrypcja się nie zaczyna
1. Upewnij się, że model jest pobrany (Ustawienia → Model powinien być "pobrany")
2. Sprawdź logi w: `%AppData%\Transcriber\logs\`
3. Spróbuj inny model (np. przejdź z `base` na `tiny`)

## 8) Ustawiania aplikacji
- `EnableLiveTranscript` - **domyślnie OFF** dla oszczędności zasobów
  - Włącz, jeśli chcesz widzieć transkrypcję na żywo
  - Wyłącz dla lepszej wydajności na słabszych komputerach
- `ChunkLengthSeconds` - jak często wysyłać do transkrypcji (10-30s)
  - Większe wartości = mniej CPU, ale dłuższe opóźnienie
  - Dla 8GB RAM rekomendujemy 15-20s
- `MaxBufferedAudioFrames` - limit bufora audio (256-4096)
  - Domyślnie 2048, zwiększ jeśli traciła dźwięk
- `TranscriptDisplayMode` - jak wyświetlać transkrypcję
  - AppendAndCorrect, AppendBelow, AppendAbove
