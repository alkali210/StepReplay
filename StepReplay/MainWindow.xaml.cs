using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using StepReplay.Models;
using StepReplay.Services;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;

namespace StepReplay;

public sealed partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InputRecorder _recorder = new();
    private readonly InputReplayer _replayer = new();
    private readonly List<InputEvent> _events = [];
    private readonly AppSettings _settings;
    private readonly Localizer _localizer;
    private CancellationTokenSource? _replayCts;
    private bool _isUpdatingSettingsUi;
    private bool _isTransitioningPage;

    public ObservableCollection<EventListItem> VisibleEvents { get; } = [];

    public MainWindow()
    {
        _settings = AppSettingsStore.Load();
        _localizer = new Localizer(_settings);

        InitializeComponent();
        _recorder.EventRecorded += OnEventRecorded;

        ApplySettingsToControls();
        ApplyLocalization();
        RebuildVisibleEvents();
        StatusText.Text = _localizer.T("Status.Ready");
    }

    private void OnEventRecorded(object? sender, InputEvent inputEvent)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AddRecordedEvent(inputEvent);
            StatusText.Text = BuildCountText(_localizer.T("Status.Recording"));
            ReplayButton.IsEnabled = false;
            ClearButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
        });
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        ReplayButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        LoadButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        BusyRing.IsActive = true;

        try
        {
            await RunCountdownAsync(_settings.RecordDelaySeconds, "Status.RecordDelay", CancellationToken.None);

            ClearEvents();
            _recorder.Start();
            StopButton.IsEnabled = true;
            StatusText.Text = _localizer.T("Status.RecordStart");
        }
        catch (Exception ex)
        {
            BusyRing.IsActive = false;
            StartButton.IsEnabled = true;
            LoadButton.IsEnabled = true;
            SettingsButton.IsEnabled = true;
            StatusText.Text = string.Format(_localizer.T("Status.RecordFailed"), ex.Message);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _recorder.Stop();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ReplayButton.IsEnabled = _events.Count > 0;
        ClearButton.IsEnabled = _events.Count > 0;
        SaveButton.IsEnabled = _events.Count > 0;
        LoadButton.IsEnabled = true;
        SettingsButton.IsEnabled = true;
        BusyRing.IsActive = false;
        StatusText.Text = _events.Count == 0
            ? _localizer.T("Status.NoEvents")
            : BuildCountText(_localizer.T("Status.RecordComplete"));
    }

    private async void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_replayer.IsReplaying)
        {
            _replayCts?.Cancel();
            return;
        }

        if (_events.Count == 0)
        {
            return;
        }

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        LoadButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        ReplayButton.Content = _localizer.T("Button.CancelReplay");
        BusyRing.IsActive = true;

        _replayCts = new CancellationTokenSource();
        try
        {
            await RunCountdownAsync(_settings.ReplayDelaySeconds, "Status.ReplayDelay", _replayCts.Token);
            StatusText.Text = _localizer.T("Status.Replaying");
            await _replayer.ReplayAsync(_events.ToList(), _replayCts.Token);
            StatusText.Text = _localizer.T("Status.ReplayComplete");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = _localizer.T("Status.ReplayCanceled");
        }
        finally
        {
            _replayCts.Dispose();
            _replayCts = null;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            ClearButton.IsEnabled = _events.Count > 0;
            ReplayButton.IsEnabled = _events.Count > 0;
            SaveButton.IsEnabled = _events.Count > 0;
            LoadButton.IsEnabled = true;
            SettingsButton.IsEnabled = true;
            ReplayButton.Content = _localizer.T("Button.Replay");
            BusyRing.IsActive = false;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearEvents();
        ReplayButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        StatusText.Text = _localizer.T("Status.Cleared");
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_events.Count == 0)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"stepreplay-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add(_localizer.T("Picker.Json"), [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            CachedFileManager.DeferUpdates(file);
            var json = JsonSerializer.Serialize(_events, JsonOptions);
            await FileIO.WriteTextAsync(file, json);
            var status = await CachedFileManager.CompleteUpdatesAsync(file);
            StatusText.Text = status == FileUpdateStatus.Complete
                ? string.Format(_localizer.T("Status.SaveComplete"), file.Name)
                : string.Format(_localizer.T("Status.SaveSync"), status);
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(_localizer.T("Status.SaveFailed"), ex.Message);
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var json = await FileIO.ReadTextAsync(file);
            var loadedEvents = JsonSerializer.Deserialize<List<InputEvent>>(json, JsonOptions) ?? [];

            ClearEvents();
            foreach (var inputEvent in loadedEvents.OrderBy(e => e.OffsetMs))
            {
                AddRecordedEvent(inputEvent);
            }

            ReplayButton.IsEnabled = _events.Count > 0;
            ClearButton.IsEnabled = _events.Count > 0;
            SaveButton.IsEnabled = _events.Count > 0;
            StatusText.Text = _events.Count == 0
                ? string.Format(_localizer.T("Status.LoadEmpty"), file.Name)
                : string.Format(_localizer.T("Status.LoadComplete"), BuildCountText(_localizer.T("Status.Loaded")), file.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(_localizer.T("Status.LoadFailed"), ex.Message);
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTransitioningPage || SettingsPage.Visibility == Visibility.Visible)
        {
            return;
        }

        _isTransitioningPage = true;
        SettingsButton.IsEnabled = false;
        MainPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Visible;

        MainPage.Opacity = 1;
        MainPageTransform.X = 0;
        SettingsPage.Opacity = 0;
        SettingsPageTransform.X = 24;

        await BeginStoryboardAsync(ShowSettingsStoryboard);

        MainPage.Visibility = Visibility.Collapsed;
        _isTransitioningPage = false;
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTransitioningPage || SettingsPage.Visibility != Visibility.Visible)
        {
            return;
        }

        _isTransitioningPage = true;
        SettingsButton.IsEnabled = false;
        MainPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Visible;

        SettingsPage.Opacity = 1;
        SettingsPageTransform.X = 0;
        MainPage.Opacity = 0;
        MainPageTransform.X = -24;

        await BeginStoryboardAsync(ShowMainStoryboard);

        SettingsPage.Visibility = Visibility.Collapsed;
        SettingsButton.IsEnabled = true;
        _isTransitioningPage = false;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSettingsUi || LanguageComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
        {
            return;
        }

        _settings.Language = language;
        SaveSettingsAndRefresh();
    }

    private void RecordDelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdatingSettingsUi || double.IsNaN(sender.Value))
        {
            return;
        }

        _settings.RecordDelaySeconds = (int)Math.Round(sender.Value);
        SaveSettingsAndRefresh(updateControls: true);
    }

    private void ReplayDelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdatingSettingsUi || double.IsNaN(sender.Value))
        {
            return;
        }

        _settings.ReplayDelaySeconds = (int)Math.Round(sender.Value);
        SaveSettingsAndRefresh(updateControls: true);
    }

    private void ShowMouseMovesSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSettingsUi)
        {
            return;
        }

        _settings.ShowMouseMovesInList = ShowMouseMovesSwitch.IsOn;
        SaveSettingsAndRefresh();
    }

    private void SaveSettingsAndRefresh(bool updateControls = false)
    {
        AppSettingsStore.Save(_settings);
        if (updateControls)
        {
            ApplySettingsToControls();
        }

        ApplyLocalization();
        RebuildVisibleEvents();
        StatusText.Text = _localizer.T("Settings.Saved");
    }

    private void ApplySettingsToControls()
    {
        _isUpdatingSettingsUi = true;
        try
        {
            LanguageComboBox.SelectedItem = _settings.Language == "en-US"
                ? LanguageEnglishItem
                : LanguageChineseItem;
            RecordDelayBox.Value = _settings.RecordDelaySeconds;
            ReplayDelayBox.Value = _settings.ReplayDelaySeconds;
            ShowMouseMovesSwitch.IsOn = _settings.ShowMouseMovesInList;
        }
        finally
        {
            _isUpdatingSettingsUi = false;
        }
    }

    private void ApplyLocalization()
    {
        SubtitleText.Text = _localizer.T("App.Subtitle");
        StartButton.Content = _localizer.T("Button.Start");
        StopButton.Content = _localizer.T("Button.Stop");
        ReplayButton.Content = _replayer.IsReplaying ? _localizer.T("Button.CancelReplay") : _localizer.T("Button.Replay");
        ClearButton.Content = _localizer.T("Button.Clear");
        SaveButton.Content = _localizer.T("Button.Save");
        LoadButton.Content = _localizer.T("Button.Load");
        AutomationProperties.SetName(SettingsButton, _localizer.T("Button.Settings"));
        ToolTipService.SetToolTip(SettingsButton, _localizer.T("Button.Settings"));
        BackButton.Content = _localizer.T("Button.Back");

        TimeHeaderText.Text = _localizer.T("Column.Time");
        TypeHeaderText.Text = _localizer.T("Column.Type");
        DetailHeaderText.Text = _localizer.T("Column.Detail");

        TipInfoBar.Title = _localizer.T("Info.Title");
        TipInfoBar.Message = _localizer.T("Info.Message");

        SettingsTitleText.Text = _localizer.T("Settings.Title");
        LanguageLabelText.Text = _localizer.T("Settings.Language");
        LanguageChineseItem.Content = _localizer.T("Settings.Language.zh-CN");
        LanguageEnglishItem.Content = _localizer.T("Settings.Language.en-US");
        RecordDelayBox.Header = _localizer.T("Settings.RecordDelay");
        ReplayDelayBox.Header = _localizer.T("Settings.ReplayDelay");
        ShowMouseMovesSwitch.Header = _localizer.T("Settings.ShowMouseMoves");

        UpdateEventsSummary();
    }

    private async Task RunCountdownAsync(int delaySeconds, string statusKey, CancellationToken cancellationToken)
    {
        for (var seconds = delaySeconds; seconds > 0; seconds--)
        {
            StatusText.Text = string.Format(_localizer.T(statusKey), seconds);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static Task BeginStoryboardAsync(Storyboard storyboard)
    {
        var tcs = new TaskCompletionSource();

        void OnCompleted(object? sender, object e)
        {
            storyboard.Completed -= OnCompleted;
            tcs.TrySetResult();
        }

        storyboard.Completed += OnCompleted;
        storyboard.Begin();
        return tcs.Task;
    }

    private void AddRecordedEvent(InputEvent inputEvent)
    {
        _events.Add(inputEvent);
        if (ShouldShowEvent(inputEvent))
        {
            VisibleEvents.Add(CreateListItem(inputEvent));
        }

        UpdateEventsSummary();
    }

    private void ClearEvents()
    {
        _events.Clear();
        VisibleEvents.Clear();
        UpdateEventsSummary();
    }

    private void RebuildVisibleEvents()
    {
        VisibleEvents.Clear();
        foreach (var inputEvent in _events.Where(ShouldShowEvent))
        {
            VisibleEvents.Add(CreateListItem(inputEvent));
        }

        UpdateEventsSummary();
    }

    private EventListItem CreateListItem(InputEvent inputEvent) => new()
    {
        Source = inputEvent,
        OffsetText = $"{inputEvent.OffsetMs:N0} ms",
        KindText = inputEvent.Kind == InputEventKind.Mouse
            ? _localizer.T("Kind.Mouse")
            : _localizer.T("Kind.Keyboard"),
        DetailText = _localizer.FormatEventDetail(inputEvent)
    };

    private void UpdateEventsSummary()
    {
        var hiddenMoves = CountHiddenMoves();
        EventsSummaryText.Text = hiddenMoves == 0
            ? _localizer.T("Summary.NoHidden")
            : string.Format(_localizer.T("Summary.Hidden"), hiddenMoves);
    }

    private string BuildCountText(string prefix)
    {
        var hiddenMoves = CountHiddenMoves();
        return _localizer.FormatCount(prefix, _events.Count, VisibleEvents.Count, hiddenMoves);
    }

    private int CountHiddenMoves() => _settings.ShowMouseMovesInList ? 0 : _events.Count(IsMouseMove);

    private bool ShouldShowEvent(InputEvent inputEvent) =>
        _settings.ShowMouseMovesInList || !IsMouseMove(inputEvent);

    private static bool IsMouseMove(InputEvent inputEvent) =>
        inputEvent.Kind == InputEventKind.Mouse && inputEvent.MouseAction == MouseAction.Move;
}
