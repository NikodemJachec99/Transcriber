using System;
using System.Windows;
using System.Windows.Input;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Sessions;

namespace AlwaysOnTopTranscriber.App;

public partial class MiniWidget : Window
{
    private readonly ITranscriptionSessionService _sessionService;
    private readonly AppSettings _settings;
    private Point _dragStartPoint;
    private bool _isDragging;

    public MiniWidget(ITranscriptionSessionService sessionService, AppSettings settings)
    {
        _sessionService = sessionService;
        _settings = settings;

        InitializeComponent();

        // Apply saved bounds and opacity
        Left = _settings.MiniWidgetBounds.Left;
        Top = _settings.MiniWidgetBounds.Top;
        Width = _settings.MiniWidgetBounds.Width;
        Height = _settings.MiniWidgetBounds.Height;
        var opacity = _settings.WidgetOpacityPercent / 100.0;
        Opacity = opacity;

        // Subscribe to session events
        _sessionService.RecordingStateChanged += OnRecordingStateChanged;
        _sessionService.LiveTranscriptUpdated += OnLiveTranscriptUpdated;
        _sessionService.AudioLevelChanged += OnAudioLevelChanged;

        // Update initial state
        UpdateStatusIndicator();
    }

    private void OnRecordingStateChanged(bool isRecording)
    {
        Dispatcher.Invoke(() =>
        {
            StartStopButton.Content = isRecording ? "Stop" : "Start";
            StartStopButton.Background = isRecording
                ? System.Windows.Media.Brushes.Red
                : System.Windows.Media.Brushes.Green;
            UpdateStatusIndicator();
        });
    }

    private void OnLiveTranscriptUpdated(LiveTranscriptUpdate update)
    {
        Dispatcher.Invoke(() =>
        {
            ElapsedTimeTextBlock.Text = update.ElapsedTime.ToString(@"hh\:mm\:ss");
        });
    }

    private void OnAudioLevelChanged(float level)
    {
        Dispatcher.Invoke(() =>
        {
            AudioLevelBar.Value = Math.Clamp(level * 100, 0, 100);
        });
    }

    private void UpdateStatusIndicator()
    {
        var state = _sessionService.IsRecording ? "● Recording" : "● Ready";
        var color = _sessionService.IsRecording ? "#EF4444" : "#9CA3AF";
        StatusIndicatorTextBlock.Text = state;
        StatusIndicatorTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private async void StartStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_sessionService.IsRecording)
            {
                await _sessionService.StopAsync();
            }
            else
            {
                var sessionName = SessionNameTextBlock.Text;
                await _sessionService.StartAsync(sessionName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Błąd: {ex.Message}", "Transcriber Widget", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SaveBounds();
        Hide();
    }

    private void DragHandle_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(null);
            ((UIElement)sender).CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isDragging)
        {
            Point currentPoint = e.GetPosition(null);
            Left += currentPoint.X - _dragStartPoint.X;
            Top += currentPoint.Y - _dragStartPoint.Y;
            _dragStartPoint = currentPoint;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            SaveBounds();
        }
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
