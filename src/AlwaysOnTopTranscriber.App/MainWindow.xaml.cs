using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Sessions;
using AlwaysOnTopTranscriber.Core.Storage;
using AlwaysOnTopTranscriber.Core.Utilities;
using Microsoft.Win32;

namespace AlwaysOnTopTranscriber.App;

public partial class MainWindow : Window
{
    private const int MaxDisplayedChars = 50_000; // Limit display to prevent UI slowdown on long recordings

    private readonly AppPaths _appPaths;
    private readonly ISettingsService _settingsService;
    private readonly IModelManager _modelManager;
    private readonly ISessionRepository _sessionRepository;
    private readonly ITranscriptionSessionService _sessionService;
    private readonly ObservableCollection<SessionRow> _sessionRows = [];

    private AppSettings _settings;
    private bool _isBusy;
    private string _fullTranscriptText = string.Empty;
    private MiniWidget _miniWidget;

    public MainWindow(
        AppPaths appPaths,
        ISettingsService settingsService,
        IModelManager modelManager,
        ISessionRepository sessionRepository,
        ITranscriptionSessionService sessionService)
    {
        _appPaths = appPaths;
        _settingsService = settingsService;
        _modelManager = modelManager;
        _sessionRepository = sessionRepository;
        _sessionService = sessionService;
        _settings = settingsService.Load();

        InitializeComponent();

        DataPathText.Text = $"Dane: {_appPaths.RootDirectory}";
        WarningTextBlock.Visibility = Visibility.Collapsed;
        SessionsDataGrid.ItemsSource = _sessionRows;
        SessionNameTextBox.Text = BuildDefaultSessionName();
        SelectLanguage(_settings.Language);
        EnableLiveTranscriptCheckBox.IsChecked = _settings.EnableLiveTranscript;

        // Initialize GPU settings
        EnableGpuAccelerationCheckBox.IsChecked = _settings.TryGpuAcceleration;
        GpuProviderPanel.IsEnabled = _settings.TryGpuAcceleration;

        // Set GPU provider combobox to current setting
        var gpuProviderIndex = _settings.GpuProvider switch
        {
            "cuda" => 1,
            "directml" => 2,
            "rocm" => 3,
            "cpu" => 4,
            _ => 0 // default "auto"
        };
        GpuProviderComboBox.SelectedIndex = gpuProviderIndex;

        // Initialize advanced settings
        ChunkLengthSlider.Value = _settings.ChunkLengthSeconds;
        ChunkLengthValueTextBlock.Text = $"{_settings.ChunkLengthSeconds}s";
        AudioBufferSlider.Value = _settings.MaxBufferedAudioFrames;
        AudioBufferValueTextBlock.Text = _settings.MaxBufferedAudioFrames.ToString();

        // Subscribe to slider changes for real-time value display
        ChunkLengthSlider.ValueChanged += (s, e) =>
        {
            ChunkLengthValueTextBlock.Text = $"{(int)ChunkLengthSlider.Value}s";
            PreviewChunkLengthTextBlock.Text = $"{(int)ChunkLengthSlider.Value}s";
        };
        AudioBufferSlider.ValueChanged += (s, e) =>
        {
            AudioBufferValueTextBlock.Text = ((int)AudioBufferSlider.Value).ToString();
            PreviewAudioBufferTextBlock.Text = $"{(int)AudioBufferSlider.Value} ramek";
        };

        // Initialize quick toggles
        LiveTranscriptQuickToggle.IsChecked = _settings.EnableLiveTranscript;
        AutoPunctuationQuickToggle.IsChecked = _settings.AutoPunctuation;

        _sessionService.RecordingStateChanged += OnRecordingStateChanged;
        _sessionService.LiveTranscriptUpdated += OnLiveTranscriptUpdated;
        _sessionService.WarningRaised += OnWarningRaised;
        _sessionService.SessionSaved += OnSessionSaved;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshModelListAsync();
        await RefreshSessionsAsync();
        UpdateModelStatus();
        UpdateSettingsPreview();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _sessionService.RecordingStateChanged -= OnRecordingStateChanged;
        _sessionService.LiveTranscriptUpdated -= OnLiveTranscriptUpdated;
        _sessionService.WarningRaised -= OnWarningRaised;
        _sessionService.SessionSaved -= OnSessionSaved;
    }

    private async Task RefreshModelListAsync()
    {
        var models = await _modelManager.GetAvailableAsync(CancellationToken.None);
        ModelComboBox.ItemsSource = models;
        LiveModelComboBox.ItemsSource = models;

        var selected = models.FirstOrDefault(x => x.Name == _settings.ModelName);
        ModelComboBox.SelectedItem = selected ?? models.FirstOrDefault();
        LiveModelComboBox.SelectedItem = selected ?? models.FirstOrDefault();

        if (selected is null && models.Count > 0)
        {
            _settings.ModelName = models[0].Name;
        }

        CustomModelPathTextBox.Text = _settings.ModelPath ?? string.Empty;
    }

    private void UpdateModelStatus()
    {
        var selectedModel = ModelComboBox.SelectedItem as ModelDescriptor;
        if (selectedModel is null)
        {
            ModelStatusTextBlock.Text = "Brak wybranego modelu.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(CustomModelPathTextBox.Text))
        {
            var customPath = CustomModelPathTextBox.Text.Trim();
            ModelStatusTextBlock.Text = File.Exists(customPath)
                ? $"Używany będzie własny plik: {customPath}"
                : $"Wybrano własny plik, ale nie istnieje: {customPath}";
            return;
        }

        ModelStatusTextBlock.Text = selectedModel.IsDownloaded
            ? $"Model {selectedModel.Name} jest pobrany."
            : $"Model {selectedModel.Name} nie jest pobrany.";
    }

    private async Task SaveSettingsAsync()
    {
        var selectedLanguage = GetSelectedLanguage();
        var selectedModel = ModelComboBox.SelectedItem as ModelDescriptor;
        if (selectedModel is not null)
        {
            _settings.ModelName = selectedModel.Name;
        }

        _settings.Language = selectedLanguage;
        _settings.ModelPath = string.IsNullOrWhiteSpace(CustomModelPathTextBox.Text)
            ? null
            : CustomModelPathTextBox.Text.Trim();
        _settings.EnableLiveTranscript = EnableLiveTranscriptCheckBox.IsChecked ?? true;
        _settings.ChunkLengthSeconds = (int)ChunkLengthSlider.Value;
        _settings.MaxBufferedAudioFrames = (int)AudioBufferSlider.Value;
        _settings.TryGpuAcceleration = EnableGpuAccelerationCheckBox.IsChecked ?? true;

        // Save GPU provider from combobox
        var selectedGpuItem = GpuProviderComboBox.SelectedItem as ComboBoxItem;
        if (selectedGpuItem?.Tag is string gpuProvider)
        {
            _settings.GpuProvider = gpuProvider;
        }

        await _settingsService.SaveAsync(_settings, CancellationToken.None);
        FooterStatusTextBlock.Text = "Ustawienia zapisane.";
        UpdateSettingsPreview();
    }

    private async void StartStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        StartStopButton.IsEnabled = false;

        try
        {
            await SaveSettingsAsync();

            if (_sessionService.IsRecording)
            {
                await _sessionService.StopAsync();
                FooterStatusTextBlock.Text = "Nagrywanie zatrzymane.";
            }
            else
            {
                var sessionName = string.IsNullOrWhiteSpace(SessionNameTextBox.Text)
                    ? BuildDefaultSessionName()
                    : SessionNameTextBox.Text.Trim();
                await _sessionService.StartAsync(sessionName);
                FooterStatusTextBlock.Text = "Nagrywanie uruchomione.";
            }
        }
        catch (Exception ex)
        {
            FooterStatusTextBlock.Text = ex.Message;
            WarningTextBlock.Text = ex.Message;
            WarningTextBlock.Visibility = Visibility.Visible;
        }
        finally
        {
            StartStopButton.IsEnabled = true;
            _isBusy = false;
        }
    }

    private async void SaveSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveSettingsAsync();
            UpdateModelStatus();
        }
        catch (Exception ex)
        {
            FooterStatusTextBlock.Text = ex.Message;
        }
    }

    private void ResetAdvancedSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Reset to defaults from AppSettings class
        ChunkLengthSlider.Value = 10; // Default from AppSettings
        AudioBufferSlider.Value = 2048; // Default from AppSettings
        ChunkLengthValueTextBlock.Text = "10s";
        AudioBufferValueTextBlock.Text = "2048";
        EnableGpuAccelerationCheckBox.IsChecked = true;
        GpuProviderComboBox.SelectedIndex = 0; // Auto-detect
        FooterStatusTextBlock.Text = "Ustawienia zaawansowane przywrócone do wartości domyślnych.";
    }

    private void EnableGpuAccelerationCheckBox_OnToggled(object sender, RoutedEventArgs e)
    {
        var isEnabled = EnableGpuAccelerationCheckBox.IsChecked == true;
        GpuProviderPanel.IsEnabled = isEnabled;
        _settings.TryGpuAcceleration = isEnabled;
        FooterStatusTextBlock.Text = isEnabled
            ? "GPU acceleration: włączony"
            : "GPU acceleration: wyłączony";
    }

    private void GpuProviderComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItem = GpuProviderComboBox.SelectedItem as ComboBoxItem;
        if (selectedItem?.Tag is string provider)
        {
            _settings.GpuProvider = provider;
            FooterStatusTextBlock.Text = $"GPU Provider wybrany: {selectedItem.Content}";
        }
    }

    private void LiveTranscriptQuickToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        _settings.EnableLiveTranscript = LiveTranscriptQuickToggle.IsChecked ?? false;
        EnableLiveTranscriptCheckBox.IsChecked = _settings.EnableLiveTranscript;
    }

    private void AutoPunctuationQuickToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        _settings.AutoPunctuation = AutoPunctuationQuickToggle.IsChecked ?? true;
    }

    private void LiveModelComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LiveModelComboBox.SelectedItem is ModelDescriptor selected)
        {
            _settings.ModelName = selected.Name;
            ModelComboBox.SelectedItem = selected;
            UpdateSettingsPreview();
        }
    }

    private void UpdateSettingsPreview()
    {
        PreviewChunkLengthTextBlock.Text = $"{_settings.ChunkLengthSeconds}s";
        PreviewAudioBufferTextBlock.Text = $"{_settings.MaxBufferedAudioFrames} ramek";
        var hasCustomModel = !string.IsNullOrWhiteSpace(_settings.ModelPath);
        PreviewCustomModelTextBlock.Text = hasCustomModel ? "Tak" : "Nie";
    }

    private void GoToSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var tabControl = FindVisualParent<TabControl>(this);
        if (tabControl != null && tabControl.Items.Count > 1)
        {
            tabControl.SelectedIndex = 1; // Switch to Settings tab (index 1)
        }
    }

    private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T parentAsT)
            {
                return parentAsT;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private async void DownloadModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedModel = ModelComboBox.SelectedItem as ModelDescriptor;
        if (selectedModel is null)
        {
            FooterStatusTextBlock.Text = "Wybierz model do pobrania.";
            return;
        }

        DownloadModelButton.IsEnabled = false;
        DownloadProgressBar.Value = 0;
        FooterStatusTextBlock.Text = $"Pobieranie modelu {selectedModel.Name}...";

        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            DownloadProgressBar.Value = Math.Round(Math.Clamp(p.FractionCompleted * 100, 0, 100), 2);
            FooterStatusTextBlock.Text = $"Pobieranie {p.ModelName}: {DownloadProgressBar.Value:0.##}%";
        });

        try
        {
            var downloadedPath = await _modelManager.DownloadAsync(selectedModel.Name, progress, CancellationToken.None);
            _settings.ModelName = selectedModel.Name;
            _settings.ModelPath = null;
            await _settingsService.SaveAsync(_settings, CancellationToken.None);

            FooterStatusTextBlock.Text = $"Model pobrany: {downloadedPath}";
            await RefreshModelListAsync();
            UpdateModelStatus();
        }
        catch (Exception ex)
        {
            FooterStatusTextBlock.Text = $"Błąd pobierania: {ex.Message}";
        }
        finally
        {
            DownloadModelButton.IsEnabled = true;
        }
    }

    private async void RefreshModelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshModelListAsync();
        UpdateModelStatus();
    }

    private void BrowseModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Wybierz plik modelu Whisper",
            Filter = "Whisper model (*.bin)|*.bin|Wszystkie pliki (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            CustomModelPathTextBox.Text = dialog.FileName;
            UpdateModelStatus();
        }
    }

    private async Task RefreshSessionsAsync()
    {
        var sessions = await _sessionRepository.GetSessionsAsync(CancellationToken.None);
        _sessionRows.Clear();
        foreach (var session in sessions.OrderByDescending(x => x.StartTimeUtc))
        {
            _sessionRows.Add(new SessionRow(session));
        }

        SessionsInfoTextBlock.Text = $"Sesje: {_sessionRows.Count}";
    }

    private async void RefreshSessionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshSessionsAsync();
    }

    private void ShowMiniWidgetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_miniWidget == null || !_miniWidget.IsVisible)
        {
            _miniWidget = new MiniWidget(
                _sessionService,
                _settings,
                SessionNameTextBox.Text,
                GetSelectedLanguage(),
                ApplyWidgetSessionName,
                ApplyWidgetLanguage,
                RestoreMainPanelFromWidget);
            _miniWidget.Show();
            WindowState = WindowState.Minimized;
        }
        else
        {
            _miniWidget.Activate();
        }
    }

    private void ApplyWidgetSessionName(string sessionName)
    {
        Dispatcher.Invoke(() =>
        {
            SessionNameTextBox.Text = sessionName;
        });
    }

    private void ApplyWidgetLanguage(string languageCode)
    {
        Dispatcher.Invoke(() =>
        {
            SelectLanguage(languageCode);
            _settings.Language = languageCode;
        });
    }

    private void RestoreMainPanelFromWidget()
    {
        Dispatcher.Invoke(() =>
        {
            WindowState = WindowState.Normal;
            Show();
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });
    }

    private void OpenDataFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPath(_appPaths.RootDirectory);
    }

    private void OpenTranscriptsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPath(_appPaths.TranscriptsDirectory);
    }

    private void OpenSelectedTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SessionsDataGrid.SelectedItem is not SessionRow row)
        {
            FooterStatusTextBlock.Text = "Wybierz sesję z listy.";
            return;
        }

        var txtPath = string.IsNullOrWhiteSpace(row.TextPath) ? row.JsonPath : row.TextPath;
        if (!File.Exists(txtPath))
        {
            FooterStatusTextBlock.Text = $"Plik nie istnieje: {txtPath}";
            return;
        }

        OpenPath(txtPath);
    }

    private static void OpenPath(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        };
        Process.Start(psi);
    }

    private void OnRecordingStateChanged(object? sender, bool isRecording)
    {
        Dispatcher.Invoke(() =>
        {
            StartStopButton.Content = isRecording ? "Stop" : "Start";
            StartStopButton.Background = isRecording
                ? System.Windows.Media.Brushes.IndianRed
                : System.Windows.Media.Brushes.MediumSeaGreen;

            StatusTextBlock.Text = isRecording ? "Nagrywanie..." : "Ready";
            if (!isRecording)
            {
                SessionNameTextBox.Text = BuildDefaultSessionName();
            }
        });
    }

    private void OnLiveTranscriptUpdated(object? sender, LiveTranscriptUpdate update)
    {
        Dispatcher.Invoke(() =>
        {
            ElapsedTextBlock.Text = update.Elapsed.ToString(@"hh\:mm\:ss");
            AudioLevelBar.Value = Math.Round(Math.Clamp(update.SmoothedAudioLevel * 100d, 0d, 100d), 1);

            // Deferred transcription progress
            if (update.IsTranscriptionInProgress)
            {
                DeferredTranscriptionBorder.Visibility = Visibility.Visible;
                TranscribeNowButton.Visibility = Visibility.Collapsed;
                TranscriptionProgressBar.Visibility = Visibility.Visible;
                TranscriptionProgressBar.Value = update.TranscriptionProgressPercent;
                TranscriptionProgressTextBlock.Text =
                    $"Transkrypcja w toku: {update.TranscribedChunks}/{update.TotalChunksToTranscribe} " +
                    $"({update.TranscriptionProgressPercent}%)";
            }
            else if (update.TotalChunksToTranscribe > 0 && update.TranscribedChunks == 0)
            {
                // Nagranie gotowe, czekamy na kliknięcie "Transkrybuj teraz"
                DeferredTranscriptionBorder.Visibility = Visibility.Visible;
                TranscribeNowButton.Visibility = Visibility.Visible;
                TranscriptionProgressBar.Visibility = Visibility.Collapsed;
                TranscriptionProgressTextBlock.Text = $"Chunks do przetworzenia: {update.TotalChunksToTranscribe}";
            }

            if (_settings.EnableLiveTranscript)
            {
                _fullTranscriptText = update.FullText;
                // Display only last N characters to prevent UI slowdown on long recordings
                var displayText = _fullTranscriptText.Length > MaxDisplayedChars
                    ? _fullTranscriptText.Substring(_fullTranscriptText.Length - MaxDisplayedChars)
                    : _fullTranscriptText;

                TranscriptTextBox.Text = displayText;
                if (TranscriptTextBox.ActualHeight > 0)
                {
                    TranscriptTextBox.ScrollToEnd();
                }
            }
        });
    }

    private void OnWarningRaised(object? sender, string warning)
    {
        Dispatcher.Invoke(() =>
        {
            WarningTextBlock.Text = warning;
            WarningTextBlock.Visibility = Visibility.Visible;
            FooterStatusTextBlock.Text = warning;
        });
    }

    private void OnSessionSaved(object? sender, SessionEntity session)
    {
        Dispatcher.Invoke(async () =>
        {
            FooterStatusTextBlock.Text = $"Zapisano sesję: {session.Name}";
            await RefreshSessionsAsync();
        });
    }

    private void SelectLanguage(string languageCode)
    {
        var normalized = string.IsNullOrWhiteSpace(languageCode) ? "auto" : languageCode.Trim().ToLowerInvariant();
        foreach (var item in LanguageComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }

        LanguageComboBox.SelectedIndex = 0;
    }

    private string GetSelectedLanguage()
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem selected && selected.Tag is not null)
        {
            return selected.Tag.ToString() ?? "auto";
        }

        return "auto";
    }

    private static string BuildDefaultSessionName() => $"Sesja_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

    private async void TranscribeNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            TranscribeNowButton.IsEnabled = false;
            TranscriptionProgressBar.Visibility = Visibility.Visible;
            TranscriptionProgressTextBlock.Text = "Inicjowanie transkrypcji...";

            // Startuj transkrypcję asynchronicznie
            await _sessionService.StartTranscriptionAsync().ConfigureAwait(true);

            FooterStatusTextBlock.Text = "Transkrypcja zakończona pomyślnie";
        }
        catch (Exception ex)
        {
            FooterStatusTextBlock.Text = $"Błąd transkrypcji: {ex.Message}";
        }
        finally
        {
            TranscribeNowButton.IsEnabled = true;
        }
    }

    private sealed class SessionRow
    {
        public SessionRow(SessionEntity source)
        {
            Id = source.Id;
            Name = source.Name;
            StartLocal = source.StartTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            DurationText = source.Duration.ToString(@"hh\:mm\:ss");
            ModelName = source.ModelName;
            TextPath = source.TextPath;
            JsonPath = source.JsonPath;
        }

        public long Id { get; }

        public string Name { get; }

        public string StartLocal { get; }

        public string DurationText { get; }

        public string ModelName { get; }

        public string TextPath { get; }

        public string JsonPath { get; }
    }
}
