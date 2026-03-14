# Transcriber v1.3

Prosta aplikacja desktopowa do transkrypcji audio na Windows.
Nagrywa dźwięk systemowy (WASAPI Loopback) i transkrybuje go przy użyciu modelu Whisper lokalnie — **bez chmury, bez internetu**.

---

## Spis treści
1. [Wymagania](#1-wymagania)
2. [Jak pobrać projekt](#2-jak-pobrać-projekt)
3. [Instalacja automatyczna](#3-instalacja-automatyczna-polecana)
4. [Instalacja ręczna](#4-instalacja-ręczna-dla-deweloperów)
5. [Pierwsze uruchomienie – pobieranie modelu](#5-pierwsze-uruchomienie--pobieranie-modelu)
6. [Tryb odroczonej transkrypcji](#6-tryb-odroczonej-transkrypcji)
7. [Nagrywanie audio (WAV)](#7-nagrywanie-audio-wav)
8. [Gdzie zapisują się pliki](#8-gdzie-zapisują-się-pliki)
9. [Ustawienia aplikacji](#9-ustawienia-aplikacji)
10. [Troubleshooting](#10-troubleshooting)

---

## 1) Wymagania

| Wymaganie | Minimalne | Zalecane |
|-----------|-----------|----------|
| System | Windows 10/11 | Windows 11 |
| RAM | 4 GB | 8 GB+ |
| .NET SDK | 8.0+ | 8.0+ |
| Dysk | 1 GB wolne | 5 GB+ (na modele + nagrania WAV) |

Pobierz .NET SDK 8.0: https://dotnet.microsoft.com/download/dotnet/8.0

---

## 2) Jak pobrać projekt

Otwórz **PowerShell** i wklej:

```powershell
git clone https://github.com/NikodemJachec99/Transcriber.git
cd Transcriber
```

> Jeśli nie masz git: https://git-scm.com/download/win

---

## 3) Instalacja automatyczna (polecana)

**Jedna komenda — buduje, instaluje, tworzy skróty:**

```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force; .\scripts\install.ps1
```

**Krok po kroku:**
1. Otwórz **PowerShell** w folderze projektu
   *(Shift + prawy klik na folderze → "Otwórz okno PowerShell tutaj")*
2. Wklej komendę powyżej i naciśnij Enter
3. Poczekaj ~2-5 minut na kompilację

**Co robi skrypt:**
- Sprawdza .NET SDK 8.0
- Buduje aplikację (Release mode, win-x64)
- Instaluje do `%LocalAppData%\Programs\Transcriber v1.2`
- Tworzy skrót na pulpicie i w menu Start

Po instalacji uruchom **Transcriber** z pulpitu.

---

## 4) Instalacja ręczna (dla deweloperów)

```powershell
dotnet restore AlwaysOnTopTranscriber.sln
dotnet build AlwaysOnTopTranscriber.sln -c Release
dotnet run --project src/AlwaysOnTopTranscriber.App/AlwaysOnTopTranscriber.App.csproj
```

---

## 5) Pierwsze uruchomienie – pobieranie modelu

**Po uruchomieniu aplikacji musisz pobrać model Whisper.**
Bez modelu transkrypcja nie działa.

### Jak pobrać model:
1. Otwórz aplikację
2. Przejdź do zakładki **Ustawienia**
3. Wybierz model z listy rozwijanej (np. `tiny` lub `base`)
4. Kliknij przycisk **"Pobierz model"**
5. Poczekaj na pobranie (pasek postępu)

### Który model wybrać?

| Model | Rozmiar | Szybkość | Dokładność | Dla kogo |
|-------|---------|----------|------------|----------|
| `tiny` | ~75 MB | Najszybszy (~0.4x) | Podstawowa | Słabe PC (4-6 GB RAM) |
| `base` | ~142 MB | Szybki (~0.8x) | Dobra | Standardowe PC (8 GB RAM) |
| `small` | ~466 MB | Średni (~1.5x) | Lepsza | Mocniejsze PC (16 GB RAM) |
| `medium` | ~1.5 GB | Wolny (~3x) | Bardzo dobra | Mocne PC (16+ GB RAM) |

> Szybkość w nawiasach = stosunek: ile trwa transkrypcja vs. długość audio. 0.4x = 1s transkrypcji na 2.5s audio.

**Rekomendacje:**
- **Słabe PC (6 GB RAM, Ryzen 3):** `tiny`
- **Standardowy PC (8 GB RAM):** `base` lub `small`
- **Mocny PC (16+ GB RAM):** `small` lub `medium`

---

## 6) Tryb odroczonej transkrypcji

Domyślny tryb pracy: **nagraj teraz, transkrybuj później**.

1. **Kliknij Start** — aplikacja nagrywa dźwięk systemowy do pliku WAV
2. **Kliknij Stop** — nagrywanie kończy się, plik WAV jest gotowy
3. **Kliknij "Transkrybuj teraz"** — transkrypcja z pliku WAV
4. Po zakończeniu — transkrypt + audio zapisane automatycznie

**Dlaczego ten tryb?**
- Zero obciążenia CPU podczas nagrywania
- Stałe zużycie RAM niezależnie od długości nagrania (~50-80 MB)
- Nagrania 5h+ bez dropsy audio
- Transkrypcja w tle — można uruchomić kiedy komputer jest wolny

**Szacunki dla 5h nagrania (model `tiny`, Ryzen 3):**

| Metryka | Wartość |
|---------|---------|
| Plik WAV | ~549 MB |
| RAM nagrywanie | ~50-80 MB (stałe) |
| RAM transkrypcja | ~150 MB |
| Czas transkrypcji | ~2h |

---

## 7) Nagrywanie audio (WAV)

W trybie odroczonej transkrypcji aplikacja automatycznie zapisuje **pełne nagranie audio** jako plik `.wav`:

- **Format:** PCM16, 16 kHz, mono
- **Lokalizacja:** `%AppData%\Transcriber\transcripts\`
- **Rozmiar:** ~110 MB na godzinę nagrania
- **Zawartość:** Cały dźwięk systemowy (łącznie z ciszą)

Plik WAV jest zapisywany obok transkryptów (.txt, .md, .json) i jest powiązany z sesją w bazie danych.

---

## 8) Gdzie zapisują się pliki

| Folder | Zawartość |
|--------|-----------|
| `%AppData%\Transcriber\` | Główny folder danych |
| `%AppData%\Transcriber\models\` | Pobrane modele Whisper (.bin) |
| `%AppData%\Transcriber\transcripts\` | Transkrypcje (.txt, .md, .json) + nagrania (.wav) |
| `%AppData%\Transcriber\logs\` | Logi aplikacji |
| `%AppData%\Transcriber\settings.json` | Ustawienia |

---

## 9) Ustawienia aplikacji

| Ustawienie | Domyślnie | Opis |
|-----------|-----------|------|
| `EnableLiveTranscript` | OFF | Live transkrypcja w czasie nagrywania |
| `EnableDeferredTranscription` | ON | Nagraj najpierw, transkrybuj później |
| `ChunkLengthSeconds` | 10s | Jak często dzielić audio na fragmenty |
| `MaxBufferedAudioFrames` | 2048 | Bufor audio (zwiększ jeśli tracisz dźwięk) |

### Rekomendowana konfiguracja (6 GB RAM):
```
Model: tiny
EnableLiveTranscript: false
EnableDeferredTranscription: true
ChunkLengthSeconds: 15
```

---

## 10) Troubleshooting

### Problem: Transkrypcja jest bardzo wolna
- Użyj lżejszego modelu: `tiny` zamiast `base`/`small`
- Zwiększ `ChunkLengthSeconds` do 15-20s
- Włącz tryb odroczonej transkrypcji

### Problem: Aplikacja zużywa za dużo RAM
1. Wyłącz **Live transkrypcję**
2. Wybierz model `tiny`
3. Zwiększ chunk length do `20-30s`

### Problem: Brak modelu / transkrypcja nie zaczyna
1. Idź do **Ustawienia → Pobierz model**
2. Sprawdź `%AppData%\Transcriber\models\` – czy jest plik `.bin`?
3. Sprawdź logi w `%AppData%\Transcriber\logs\`

### Problem: Plik WAV jest za duży
- 1 godzina = ~110 MB — to normalne dla nieskompresowanego audio
- Możesz ręcznie usunąć stare pliki WAV z `%AppData%\Transcriber\transcripts\`

### Problem: Błąd przy instalacji (NU1900)
- Sprawdź połączenie internetowe
- `NU1900` to zwykle tylko warning — instalacja powinna działać

---

## Logi i diagnostyka

Logi: `%AppData%\Transcriber\logs\app-YYYY-MM-DD.log`

Przydatne wpisy:
```
[INF] Nagrywanie audio do: ...\sesja.wav          ← WAV recording started
[INF] Załadowano model Whisper: ...\ggml-tiny.bin  ← model loaded
[INF] Transcribed 10.0s audio in 4200ms (0.42x)   ← chunk transcribed
[INF] Transkrypcja zakończona. WAV: ...            ← done
```
