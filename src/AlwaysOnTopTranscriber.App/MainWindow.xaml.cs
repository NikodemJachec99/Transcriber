using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        var selected = models.FirstOrDefault(x => x.Name == _settings.ModelName);
        ModelComboBox.SelectedItem = selected ?? models.FirstOrDefault();

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

        await _settingsService.SaveAsync(_settings, CancellationToken.None);
        FooterStatusTextBlock.Text = "Ustawienia zapisane.";
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
