using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Sessions;

namespace AlwaysOnTopTranscriber.App;

public partial class MiniWidget : Window
{
    private readonly ITranscriptionSessionService _sessionService;
    private readonly AppSettings _settings;
    private readonly Action<string> _setSessionName;
    private readonly Action<string> _setLanguage;

    public MiniWidget(
        ITranscriptionSessionService sessionService,
        AppSettings settings,
        string sessionName,
        string languageCode,
        Action<string> setSessionName,
        Action<string> setLanguage)
    {
        _sessionService = sessionService;
        _settings = settings;
        _setSessionName = setSessionName;
        _setLanguage = setLanguage;

        InitializeComponent();

        Left = _settings.MiniWidgetBounds.Left;
        Top = _settings.MiniWidgetBounds.Top;
        Width = Math.Clamp(_settings.MiniWidgetBounds.Width, 320, 420);
        Height = Math.Clamp(_settings.MiniWidgetBounds.Height, 150, 220);
        Opacity = Math.Clamp(_settings.WidgetOpacityPercent / 100.0, 0.75, 1.0);

        SessionNameTextBox.Text = string.IsNullOrWhiteSpace(sessionName)
            ? $"Sesja_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
            : sessionName;
        SelectLanguage(languageCode);

        _sessionService.RecordingStateChanged += OnRecordingStateChanged;
        _sessionService.LiveTranscriptUpdated += OnLiveTranscriptUpdated;
        _sessionService.AudioLevelChanged += OnAudioLevelChanged;

        UpdateStatusIndicator();
    }

    private void WidgetSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // Płynniejsze przeciąganie niż ręczne liczenie delta w OnMouseMove.
        DragMove();
        SaveBounds();
    }

    private void OnRecordingStateChanged(object? sender, bool isRecording)
    {
        Dispatcher.Invoke(() =>
        {
            StartStopButton.Content = isRecording ? "Stop" : "Start";
            StartStopButton.Background = isRecording
                ? Brushes.IndianRed
                : Brushes.MediumSeaGreen;
            UpdateStatusIndicator();
        });
    }

    private void OnLiveTranscriptUpdated(object? sender, LiveTranscriptUpdate update)
    {
        Dispatcher.Invoke(() =>
        {
            ElapsedTimeTextBlock.Text = update.Elapsed.ToString(@"hh\:mm\:ss");
        });
    }

    private void OnAudioLevelChanged(object? sender, float level)
    {
        Dispatcher.Invoke(() =>
        {
            AudioLevelBar.Value = Math.Clamp(level * 100, 0, 100);
        });
    }

    private void UpdateStatusIndicator()
    {
        var isRecording = _sessionService.IsRecording;
        StatusIndicatorTextBlock.Text = isRecording ? "Recording" : "Ready";
        StatusIndicatorTextBlock.Foreground = isRecording ? Brushes.OrangeRed : Brushes.LightGray;
    }

    private async void StartStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var sessionName = string.IsNullOrWhiteSpace(SessionNameTextBox.Text)
                ? $"Sesja_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
                : SessionNameTextBox.Text.Trim();

            _setSessionName(sessionName);
            _setLanguage(GetSelectedLanguageCode());

            if (_sessionService.IsRecording)
            {
                await _sessionService.StopAsync();
                return;
            }

            await _sessionService.StartAsync(sessionName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Błąd: {ex.Message}", "Transcriber Widget", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LanguageComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _setLanguage(GetSelectedLanguageCode());
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _setSessionName(SessionNameTextBox.Text.Trim());
        _setLanguage(GetSelectedLanguageCode());
        SaveBounds();
        Hide();
    }

    private string GetSelectedLanguageCode()
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is not null)
        {
            return item.Tag.ToString() ?? "auto";
        }

        return "auto";
    }

    private void SelectLanguage(string? languageCode)
    {
        var normalized = string.IsNullOrWhiteSpace(languageCode) ? "auto" : languageCode.Trim().ToLowerInvariant();
        foreach (var item in LanguageComboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(comboItem.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedItem = comboItem;
                return;
            }
        }

        LanguageComboBox.SelectedIndex = 0;
    }

    private void SaveBounds()
    {
        _settings.MiniWidgetBounds = new WidgetBounds
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        SaveBounds();
        _sessionService.RecordingStateChanged -= OnRecordingStateChanged;
        _sessionService.LiveTranscriptUpdated -= OnLiveTranscriptUpdated;
        _sessionService.AudioLevelChanged -= OnAudioLevelChanged;
    }
}
