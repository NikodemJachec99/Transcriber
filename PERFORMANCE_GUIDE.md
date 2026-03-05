# Wytyczne wydajności - Nagrywanie sesji 5+ godzin

## Tl;dr - Quick Setup dla 8GB RAM
1. ✅ Model: `tiny` lub `base`
2. ✅ Chunk length: `15-20s` (Settings)
3. ✅ **Live transkrypcja: WYŁĄCZONA** (Settings - nie zaznaczaj checkbox)
4. ✅ Zamknij inne aplikacje
5. ✅ Nagraj, transkrypcja zapisze się automatycznie

## Detaily

### Model Whisper - Porównanie wydajności
| Model | RAM | CPU | Czas/1h | Polecamy dla 8GB |
|-------|-----|-----|---------|------------------|
| **tiny** | ~400MB | ⭐ | ~15 min | ✅ NAJLEPSZY |
| **base** | ~800MB | ⭐⭐ | ~45 min | ✅ OK |
| **small** | ~1.5GB | ⭐⭐⭐ | ~3h | ⚠️ Riskowy |
| **medium** | ~2.5GB | ⭐⭐⭐⭐ | ~6h | ❌ Za dużo |

### Ustawienia dla 5h+ sesji

#### ChunkLengthSeconds (Domyślnie: 10s)
```
10s  → Szybka transkrypcja, więcej CPU
15s  → Zbalansowane  ← REKOMENDUJEMY
20s  → Mniej CPU, więcej RAM
30s+ → Dla słabych komputerów
```
**Jak zmienić:** Ustawienia → (nie ma UI, edytuj `%AppData%\Transcriber\settings.json`)

#### MaxBufferedAudioFrames (Domyślnie: 2048)
Nie zmieniam, chyba że:
- Traciną dźwięk → zwiększ do 3000-4000
- Zużywasz zbyt dużo RAM → zmniejsz do 1024-1500

#### SilenceRmsThreshold (Domyślnie: 0.003)
Pomija cichych chunków:
- `0.001` → Czule, mogą być przerwy w transkrypcji
- `0.003` → Normalne
- `0.01` → Pomija dużo cisz

### Live Transkrypcja - WYŁĄCZENIE OSZCZĘDZA RAM/CPU
```
❌ WYŁĄCZONA (domyślnie):
   - Najmniejsze zużycie RAM
   - CPU na transkrypcji
   - Transkrypcja zapisuje się na końcu

✅ WŁĄCZONA:
   - +5-10% zużycia CPU
   - Widzisz tekst na żywo
   - Limitowane do ostatnich 50KB znaków
```

## Monitoring podczas nagrywania

### Szukaj tych ostrzeżeń w aplikacji:
```
⚠️ "Bufor audio osiągnął limit..."
   → Zwiększ chunk length lub wyłącz live transkrypcję

⚠️ "Kolejka transkrypcji rośnie..."
   → CPU za słaby na wybrany model
   → Zmień na lighter model (tiny)
```

### Windows Task Manager
Sprawdzaj (Ctrl+Shift+Esc) podczas nagrywania:
```
RAM:  < 4GB zajęte (dla 8GB) ✅
CPU:  < 60% average ✅
Disk: < 50 MB/s ✅
```

## Troubleshooting

### Problem: "Pominięto X ramek audio"
**Przyczyna:** Audio buffer overflow
**Rozwiązanie:**
1. Zwiększ `ChunkLengthSeconds` do 20-30s
2. Zamknij Chrome, VS Code, itd.
3. Zmień model na `tiny`

### Problem: Transkrypcja trwa 10h dla 5h nagrania
**Przyczyna:** Model `small` / `medium` na słabym CPU
**Rozwiązanie:**
1. Przerwij (Stop)
2. Zmień model na `tiny` / `base`
3. Nagraj nową sesję

### Problem: Crash/Wyłączenie aplikacji
**Co robić:**
1. Sprawdź `%AppData%\Transcriber\logs\` - szukaj ERROR
2. Zgłoś bug z logiem
3. Tymczasowe rozwiązanie: smaller model + no live transcript

## Wskazówki

1. **Testuj przed długą sesją** - nagrywaj 5-10 minut z ustawieniami
2. **Nie uruchamiaj nic na boku** - zamknij Discord, Chrome, VS Code
3. **SSD > HDD** - transkrypcja zapisuje się na dysk
4. **Odłącz webcam/USB** - mogą generować szumy
5. **Nie konwertuj formatu** - nagrywaj MP3/WAV 16-bit 44.1kHz

## Zapisane transkrypcje

Każda sesja jest zapisywana w:
```
%AppData%\Transcriber\transcripts\
├── Sesja_2024-01-15_14-30-45.txt   (czysty tekst)
├── Sesja_2024-01-15_14-30-45.md    (markdown)
└── Sesja_2024-01-15_14-30-45.json  (segmenty + timing)
```

**Pełny tekst jest zawsze zapisany**, niezależnie od live transkrypcji!

## Uaktualnij aplikację

Jeśli masz starszą wersję:
```powershell
# PowerShell w folderze projektu
powershell -ExecutionPolicy Bypass -File .\scripts\install-transcriber.ps1
```

To zbuduje i zainstaluje najnowszą wersję.
