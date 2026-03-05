# Transcriber v1.2

Prosta aplikacja desktopowa do transkrypcji audio na Windows.
Nagrywa dźwięk systemowy (WASAPI Loopback) i transkrybuje go przy użyciu modelu Whisper lokalnie — **bez chmury, bez internetu**.

---

## Spis treści
1. [Wymagania](#1-wymagania)
2. [Jak pobrać projekt](#2-jak-pobrać-projekt)
3. [Instalacja automatyczna](#3-instalacja-automatyczna-polecana)
4. [Instalacja ręczna](#4-instalacja-ręczna-dla-deweloperów)
5. [Pierwsze uruchomienie – pobieranie modelu](#5-pierwsze-uruchomienie--pobieranie-modelu)
6. [Ustawienia GPU / CPU](#6-ustawienia-gpu--cpu)
7. [Tryb odroczonej transkrypcji](#7-tryb-odroczonej-transkrypcji-deferred)
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
| GPU (opcjonalnie) | — | NVIDIA RTX/GTX (CUDA) |

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
- ✅ Sprawdza .NET SDK 8.0
- ✅ Buduje aplikację (Release mode, win-x64)
- ✅ Instaluje do `%LocalAppData%\Programs\Transcriber v1.2`
- ✅ Tworzy skrót na pulpicie i w menu Start

Po instalacji uruchom **Transcriber** z pulpitu.

---

## 4) Instalacja ręczna (dla deweloperów)

```powershell
# Zainstaluj MAUI workload (jednorazowo)
dotnet workload install maui-windows --skip-manifest-update

# Przywróć paczki i zbuduj
dotnet restore AlwaysOnTopTranscriber.sln
dotnet build AlwaysOnTopTranscriber.sln -c Release

# Uruchom bezpośrednio
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
6. Model zostanie zapisany w `%AppData%\Transcriber\models\`

### Który model wybrać?

| Model | Rozmiar | Szybkość (CPU) | Dokładność | Dla kogo |
|-------|---------|----------------|------------|----------|
| `tiny` | ~75 MB | Najszybszy | Podstawowa | Słabe PC (4-6 GB RAM), testy |
| `base` | ~142 MB | Szybki | Dobra | Standardowe PC (8 GB RAM) |
| `small` | ~466 MB | Średni | Lepsza | Mocniejsze PC |
| `medium` | ~1.5 GB | Wolny | Bardzo dobra | PC z GPU (RTX) |
| `medium-q5` | ~1.0 GB | Wolny | Bardzo dobra | PC z GPU (RTX), skompresowany |

**Rekomendacje:**
- **AMD Vega 8 / słabe PC (6 GB RAM):** `tiny` → najszybszy, najmniej zasobów
- **Standardowy PC (8 GB RAM, CPU):** `base` lub `small`
- **NVIDIA RTX (z GPU):** `medium` lub `medium-q5` → GPU przyspiesza znacząco

---

## 6) Ustawienia GPU / CPU

### Co obsługuje GPU acceleration?

| GPU | System | Wsparcie |
|-----|--------|----------|
| NVIDIA RTX / GTX | Windows/Linux | ✅ CUDA (szybkie) |
| AMD Vega 8 / RDNA | Windows | ❌ Brak (używa CPU) |
| AMD Vega / RDNA | Linux | ✅ ROCm |
| Intel Arc / UHD | Windows | ❌ Brak (używa CPU) |
| Apple Silicon | macOS | ✅ CoreML |

### Jak ustawić tryb GPU w aplikacji:

1. Przejdź do **Ustawienia → Zaawansowane ustawienia wydajności**
2. Zaznacz/odznacz **"Włącz GPU acceleration"**
3. Wybierz tryb z listy:
   - **Auto-detect (zalecane)** – aplikacja sama wykryje GPU
   - **NVIDIA CUDA** – wymuś CUDA (dla RTX/GTX)
   - **AMD ROCm (Linux)** – dla AMD na Linuksie
   - **CPU (wyłącz GPU)** – zawsze używaj CPU
4. Kliknij **"Zapisz ustawienia"**

### Dla AMD Vega 8 / Intel (Windows):
Jeśli **GPU acceleration jest włączone**, aplikacja automatycznie wykryje brak wsparcia i użyje CPU.
Jeśli transkrypcja się wysypuje lub działa wolno, ustaw: **"CPU (wyłącz GPU)"** → to jest najbezpieczniejsza opcja.

---

## 7) Tryb odroczonej transkrypcji (Deferred)

Dla słabszych komputerów (mało RAM, wolny CPU) dostępny jest tryb "nagraj teraz, transkrybuj później":

1. **Nagrywaj** – aplikacja zapisuje audio bez transkrypcji (zero obciążenia CPU)
2. **Zatrzymaj nagrywanie** – pojawi się przycisk **"Transkrybuj teraz"**
3. **Kliknij "Transkrybuj teraz"** – pełna transkrypcja w tle
4. Po zakończeniu transkrypt zapisuje się automatycznie

**Zalety:**
- Brak dropsy audio nawet przy 5+ godzin nagrania
- Transkrypcja uruchamiana kiedy komputer jest wolny
- Na AMD Vega 8: brak problemu z zajętością CPU

---

## 8) Gdzie zapisują się pliki

| Folder | Zawartość |
|--------|-----------|
| `%AppData%\Transcriber\` | Główny folder danych |
| `%AppData%\Transcriber\models\` | Pobrane modele Whisper (.bin) |
| `%AppData%\Transcriber\transcripts\` | Zapisane transkrypcje (.txt, .md, .json) |
| `%AppData%\Transcriber\logs\` | Logi aplikacji (diagnostyka) |
| `%AppData%\Transcriber\settings.json` | Ustawienia aplikacji |

---

## 9) Ustawienia aplikacji

| Ustawienie | Domyślnie | Opis |
|-----------|-----------|------|
| `EnableLiveTranscript` | OFF | Live transkrypcja w czasie nagrywania. Wyłącz dla oszczędności zasobów. |
| `EnableDeferredTranscription` | ON | Tryb "nagraj najpierw, transkrybuj później" |
| `ChunkLengthSeconds` | 10s | Jak często wysyłać audio do Whisper. Dla słabego PC: 15-20s |
| `MaxBufferedAudioFrames` | 2048 | Bufor audio. Zwiększ jeśli tracisz dźwięk. |
| `TryGpuAcceleration` | ON | Próbuje użyć GPU. Fallback na CPU jeśli GPU niedostępne. |
| `GpuProvider` | auto | auto / cuda / rocm / cpu |

### Rekomendowana konfiguracja dla AMD Vega 8 (6 GB RAM):
```
Model: tiny
EnableLiveTranscript: false
EnableDeferredTranscription: true
ChunkLengthSeconds: 15
MaxBufferedAudioFrames: 2048
TryGpuAcceleration: false  (lub true z "CPU (wyłącz GPU)")
```

### Rekomendowana konfiguracja dla NVIDIA RTX:
```
Model: medium lub medium-q5
EnableLiveTranscript: true (opcjonalnie)
EnableDeferredTranscription: false
ChunkLengthSeconds: 10
TryGpuAcceleration: true
GpuProvider: cuda
```

---

## 10) Troubleshooting

### Problem: Transkrypcja jest bardzo wolna
- Sprawdź **Ustawienia → GPU acceleration** – czy jest włączone?
- Sprawdź logi: `%AppData%\Transcriber\logs\` – szukaj linii `[GPU DETECTED ✓]`
- Użyj lżejszego modelu: `tiny` zamiast `medium`
- Włącz tryb odroczonej transkrypcji

### Problem: Na Vega 8 GPU acceleration nie działa
- To normalne! AMD Vega 8 / Intel na Windows nie mają wsparcia w tej wersji.
- Idź do **Ustawienia → CPU (wyłącz GPU)** → model `tiny` → będzie działać.
- Transkrypcja `tiny` na CPU to ~2-5 min dla 1h nagrania.

### Problem: Aplikacja zużywa za dużo RAM
1. Wyłącz **Live transkrypcję**
2. Wybierz model `tiny`
3. Zwiększ chunk length do `20-30s`
4. Włącz tryb odroczonej transkrypcji

### Problem: Audio dropsuje po 15 minutach
- Włącz **tryb odroczonej transkrypcji** (Deferred mode)
- To rozdziela nagrywanie od transkrypcji → zero dropsy

### Problem: Brak modelu / transkrypcja nie zaczyna
1. Idź do **Ustawienia → Pobierz model**
2. Sprawdź `%AppData%\Transcriber\models\` – czy jest plik `.bin`?
3. Sprawdź logi w `%AppData%\Transcriber\logs\`

### Problem: Błąd przy instalacji (NU1900 / restore failed)
- Sprawdź połączenie internetowe
- Uruchom jako administrator
- `NU1900` to zwykle tylko warning (nie błąd) – instalacja powinna działać

### Problem: "Windows App Runtime not found"
Pobierz: https://aka.ms/windowsappruntimeinstall

---

## Logi i diagnostyka

Logi aplikacji: `%AppData%\Transcriber\logs\app-YYYY-MM-DD.log`

Przydatne wpisy w logach:
```
[INF] Detectowano GPU: NVIDIA - będzie używany CUDA      ← GPU wykryty
[INF] Załadowano model Whisper z GPU acceleration        ← model załadowany z GPU
[INF] Transcribed 10.0s audio in 3200ms [GPU DETECTED ✓] ← GPU faktycznie używany
[INF] Deferred transcription enabled                     ← tryb odroczonej transkrypcji
```
