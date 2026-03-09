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
    private readonly Action _restoreMainPanel;
    private bool _isBusy;

    public MiniWidget(
        ITranscriptionSessionService sessionService,
        AppSettings settings,
        string sessionName,
        string languageCode,
        Action<string> setSessionName,
        Action<string> setLanguage,
        Action restoreMainPanel)
    {
        _sessionService = sessionService;
        _settings = settings;
        _setSessionName = setSessionName;
        _setLanguage = setLanguage;
        _restoreMainPanel = restoreMainPanel;

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
        _sessionService.SessionStateChanged += OnSessionStateChanged;
        _sessionService.LiveTranscriptUpdated += OnLiveTranscriptUpdated;
        _sessionService.AudioLevelChanged += OnAudioLevelChanged;

        RefreshTransportControls();
    }

    private void WidgetSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // Nie przechwytuj drag na kontrolkach interaktywnych.
        if (e.OriginalSource is DependencyObject source &&
            FindInteractiveParent(source) is not null)
        {
            return;
        }

        // Płynniejsze przeciąganie niż ręczne liczenie delta w OnMouseMove.
        DragMove();
        SaveBounds();
    }

    private void OnRecordingStateChanged(object? sender, bool isRecording)
    {
        Dispatcher.Invoke(RefreshTransportControls);
    }

    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        Dispatcher.Invoke(RefreshTransportControls);
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
        var state = _sessionService.CurrentState;
        StatusIndicatorTextBlock.Text = state switch
        {
            SessionState.Recording => "Recording",
            SessionState.Paused => "Paused",
            SessionState.Recorded => "Recorded",
            SessionState.Transcribing => "Transcribing",
            SessionState.Completed => "Ready",
            _ => "Ready"
        };
        StatusIndicatorTextBlock.Foreground = state switch
        {
            SessionState.Recording => Brushes.OrangeRed,
            SessionState.Paused => Brushes.Gold,
            SessionState.Recorded => Brushes.DeepSkyBlue,
            SessionState.Transcribing => Brushes.DeepSkyBlue,
            _ => Brushes.LightGray
        };
    }

    private async void StartStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        RefreshTransportControls();

        try
        {
            var sessionName = string.IsNullOrWhiteSpace(SessionNameTextBox.Text)
                ? $"Sesja_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
                : SessionNameTextBox.Text.Trim();

            _setSessionName(sessionName);
            _setLanguage(GetSelectedLanguageCode());

            if (_sessionService.CurrentState is SessionState.Recording or SessionState.Paused)
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
        finally
        {
            _isBusy = false;
            RefreshTransportControls();
        }
    }

    private async void PauseResumeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        RefreshTransportControls();

        try
        {
            if (_sessionService.CurrentState == SessionState.Recording)
            {
                await _sessionService.PauseAsync();
            }
            else if (_sessionService.CurrentState == SessionState.Paused)
            {
                await _sessionService.ResumeAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Błąd: {ex.Message}", "Transcriber Widget", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            RefreshTransportControls();
        }
    }

    private void LanguageComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _setLanguage(GetSelectedLanguageCode());
    }

    private void ReturnToPanelButton_OnClick(object sender, RoutedEventArgs e)
    {
        _setSessionName(SessionNameTextBox.Text.Trim());
        _setLanguage(GetSelectedLanguageCode());
        SaveBounds();
        _restoreMainPanel();
        Hide();
    }

    private static DependencyObject? FindInteractiveParent(DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Button || current is TextBox || current is ComboBox || current is ComboBoxItem)
            {
                return current;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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
        _sessionService.SessionStateChanged -= OnSessionStateChanged;
        _sessionService.LiveTranscriptUpdated -= OnLiveTranscriptUpdated;
        _sessionService.AudioLevelChanged -= OnAudioLevelChanged;
    }

    private void RefreshTransportControls()
    {
        var state = _sessionService.CurrentState;

        StartStopButton.Content = "Start";
        StartStopButton.Background = Brushes.MediumSeaGreen;
        StartStopButton.IsEnabled = !_isBusy;

        PauseResumeButton.Content = "Pauza";
        PauseResumeButton.Background = Brushes.DarkOrange;
        PauseResumeButton.IsEnabled = false;

        switch (state)
        {
            case SessionState.Recording:
                StartStopButton.Content = "Stop";
                StartStopButton.Background = Brushes.IndianRed;
                PauseResumeButton.IsEnabled = !_isBusy;
                break;
            case SessionState.Paused:
                StartStopButton.Content = "Stop";
                StartStopButton.Background = Brushes.IndianRed;
                PauseResumeButton.Content = "Wznów";
                PauseResumeButton.Background = Brushes.SteelBlue;
                PauseResumeButton.IsEnabled = !_isBusy;
                break;
            case SessionState.Recorded:
            case SessionState.Transcribing:
                StartStopButton.IsEnabled = false;
                break;
        }

        UpdateStatusIndicator();
    }
}
