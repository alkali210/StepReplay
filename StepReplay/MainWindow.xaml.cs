using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using StepReplay.Models;
using StepReplay.Native;
using StepReplay.Services;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.System;
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
    private readonly GlobalHotkeyService _hotkeyService = new();
    private readonly List<InputEvent> _events = [];
    private readonly AppSettings _settings;
    private readonly Localizer _localizer;
    private CancellationTokenSource? _recordCts;
    private CancellationTokenSource? _replayCts;
    private TextBox? _capturingHotkeyBox;
    private string? _appliedThemeMode;
    private string? _appliedBackdropKind;
    private bool _isNavPaneExpanded;
    private bool _isUpdatingSettingsUi;
    private bool _isTransitioningPage;

    public ObservableCollection<EventListItem> VisibleEvents { get; } = [];

    public MainWindow()
    {
        _settings = AppSettingsStore.Load();
        _localizer = new Localizer(_settings);

        InitializeComponent();
        InitializeCustomTitleBar();
        _recorder.EventRecorded += OnEventRecorded;
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.HotkeyCaptured += OnHotkeyCaptured;
        _hotkeyService.HotkeyCaptureCanceled += OnHotkeyCaptureCanceled;
        _hotkeyService.HotkeyCaptureCleared += OnHotkeyCaptureCleared;
        _hotkeyService.HotkeyCaptureNeedsModifier += OnHotkeyCaptureNeedsModifier;
        Closed += MainWindow_Closed;

        ApplySettingsToControls();
        ApplyAppearance();
        ApplyLocalization();
        RebuildVisibleEvents();
        StatusText.Text = _localizer.T("Status.Ready");

        try
        {
            _hotkeyService.Start(_settings);
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(_localizer.T("Status.HotkeyStartFailed"), ex.Message);
        }
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
        await RequestStartRecordingAsync();
    }

    private async Task RequestStartRecordingAsync()
    {
        if (_recorder.IsRecording || _recordCts is not null || _replayCts is not null || _replayer.IsReplaying)
        {
            return;
        }

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        ReplayButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        LoadButton.IsEnabled = false;
        SetNavigationEnabled(false);
        BusyRing.IsActive = true;

        var cts = new CancellationTokenSource();
        _recordCts = cts;
        try
        {
            await RunCountdownAsync(_settings.RecordDelaySeconds, "Status.RecordDelay", cts.Token);

            ClearEvents();
            _recordCts = null;
            _recorder.Start();
            StopButton.IsEnabled = true;
            StatusText.Text = _localizer.T("Status.RecordStart");
        }
        catch (OperationCanceledException)
        {
            RestoreIdleControls();
            StatusText.Text = _localizer.T("Status.RecordCanceled");
        }
        catch (Exception ex)
        {
            RestoreIdleControls();
            StatusText.Text = string.Format(_localizer.T("Status.RecordFailed"), ex.Message);
        }
        finally
        {
            if (_recordCts == cts)
            {
                _recordCts = null;
            }

            cts.Dispose();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
    }

    private void StopRecording()
    {
        if (_recordCts is not null)
        {
            _recordCts.Cancel();
            return;
        }

        if (!_recorder.IsRecording)
        {
            return;
        }

        _recorder.Stop();
        RestoreIdleControls();
        StatusText.Text = _events.Count == 0
            ? _localizer.T("Status.NoEvents")
            : BuildCountText(_localizer.T("Status.RecordComplete"));
    }

    private async void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_replayCts is not null || _replayer.IsReplaying)
        {
            ForceStopReplay();
            return;
        }

        await RequestStartReplayAsync();
    }

    private async Task RequestStartReplayAsync()
    {
        if (_replayCts is not null || _replayer.IsReplaying || _recordCts is not null || _recorder.IsRecording)
        {
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
        SetNavigationEnabled(false);
        ReplayButton.Content = _localizer.T("Button.CancelReplay");
        BusyRing.IsActive = true;

        _replayCts = new CancellationTokenSource();
        try
        {
            await RunCountdownAsync(_settings.ReplayDelaySeconds, "Status.ReplayDelay", _replayCts.Token);
            var repeatCount = Math.Clamp(_settings.ReplayRepeatCount, 1, 999);
            var replayEvents = _events.ToList();
            for (var repeatIndex = 1; repeatIndex <= repeatCount; repeatIndex++)
            {
                _replayCts.Token.ThrowIfCancellationRequested();
                StatusText.Text = repeatCount == 1
                    ? _localizer.T("Status.Replaying")
                    : string.Format(_localizer.T("Status.ReplayingRepeat"), repeatIndex, repeatCount);
                await _replayer.ReplayAsync(replayEvents, _replayCts.Token);
            }

            StatusText.Text = repeatCount == 1
                ? _localizer.T("Status.ReplayComplete")
                : string.Format(_localizer.T("Status.ReplayCompleteRepeat"), repeatCount);
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
            SetNavigationEnabled(true);
            ReplayButton.Content = _localizer.T("Button.Replay");
            BusyRing.IsActive = false;
        }
    }

    private void ForceStopReplay()
    {
        _replayCts?.Cancel();
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

    private async void HomeNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTransitioningPage || SettingsPage.Visibility != Visibility.Visible)
        {
            return;
        }

        await ShowMainPageAsync();
    }

    private async void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTransitioningPage || SettingsPage.Visibility == Visibility.Visible)
        {
            return;
        }

        await ShowSettingsPageAsync();
    }

    private void NavToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isNavPaneExpanded = !_isNavPaneExpanded;
        AnimateNavigationPane(_isNavPaneExpanded);
    }

    private async Task ShowSettingsPageAsync()
    {
        _isTransitioningPage = true;
        SetNavigationEnabled(false);
        MainPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Visible;

        MainPage.Opacity = 1;
        MainPageTransform.X = 0;
        SettingsPage.Opacity = 0;
        SettingsPageTransform.X = 24;

        await BeginStoryboardAsync(ShowSettingsStoryboard);

        MainPage.Visibility = Visibility.Collapsed;
        SetNavigationEnabled(true);
        UpdateNavigationSelection(isSettingsPageVisible: true);
        _isTransitioningPage = false;
    }

    private async Task ShowMainPageAsync()
    {
        _isTransitioningPage = true;
        SetNavigationEnabled(false);
        MainPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Visible;

        SettingsPage.Opacity = 1;
        SettingsPageTransform.X = 0;
        MainPage.Opacity = 0;
        MainPageTransform.X = -24;

        await BeginStoryboardAsync(ShowMainStoryboard);

        SettingsPage.Visibility = Visibility.Collapsed;
        SetNavigationEnabled(true);
        UpdateNavigationSelection(isSettingsPageVisible: false);
        _isTransitioningPage = false;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSettingsUi || LanguageComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
        {
            return;
        }

        _settings.Language = language;
        SaveSettingsAndRefresh(updateLocalization: true, rebuildVisibleEvents: true);
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSettingsUi || ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string themeMode)
        {
            return;
        }

        _settings.ThemeMode = themeMode;
        SaveSettingsAndRefresh(updateAppearance: true);
    }

    private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSettingsUi || BackdropComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string backdropKind)
        {
            return;
        }

        _settings.BackdropKind = backdropKind;
        SaveSettingsAndRefresh(updateAppearance: true);
    }

    private void RecordDelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdatingSettingsUi || double.IsNaN(sender.Value))
        {
            return;
        }

        _settings.RecordDelaySeconds = (int)Math.Round(sender.Value);
        SaveSettingsAndRefresh();
    }

    private void ReplayDelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdatingSettingsUi || double.IsNaN(sender.Value))
        {
            return;
        }

        _settings.ReplayDelaySeconds = (int)Math.Round(sender.Value);
        SaveSettingsAndRefresh();
    }

    private void ReplayRepeatCountBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdatingSettingsUi || double.IsNaN(sender.Value))
        {
            return;
        }

        _settings.ReplayRepeatCount = (int)Math.Round(sender.Value);
        SaveSettingsAndRefresh();
    }

    private void ShowMouseMovesSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSettingsUi)
        {
            return;
        }

        _settings.ShowMouseMovesInList = ShowMouseMovesSwitch.IsOn;
        SaveSettingsAndRefresh(rebuildVisibleEvents: true);
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_capturingHotkeyBox, sender))
        {
            _capturingHotkeyBox = null;
        }

        _hotkeyService.EndCapture();
        if (sender is TextBox textBox)
        {
            ApplyHotkeyTextBox(textBox);
        }
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _capturingHotkeyBox = textBox;
            _hotkeyService.BeginCapture();
            textBox.SelectAll();
        }

        StatusText.Text = _localizer.T("Settings.HotkeyPressPrompt");
    }

    private void HotkeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == VirtualKey.Escape)
        {
            ApplySettingsToControls();
            StatusText.Text = _localizer.T("Settings.HotkeyCaptureCanceled");
            return;
        }

        var key = (int)e.Key;
        if (HotkeyGesture.IsModifierKey(key))
        {
            StatusText.Text = _localizer.T("Settings.HotkeyPressPrompt");
            return;
        }

        var modifiers = GetCurrentHotkeyModifiers();
        if (modifiers == HotkeyModifiers.None)
        {
            StatusText.Text = _localizer.T("Settings.HotkeyNeedModifier");
            return;
        }

        var gesture = HotkeyGesture.Create(modifiers, key);
        textBox.Text = gesture.ToString();
        ApplyHotkeyTextBox(textBox);
        _hotkeyService.EndCapture();
    }

    private void ApplyHotkeyTextBox(TextBox textBox)
    {
        if (_isUpdatingSettingsUi)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(textBox.Text))
        {
            if (string.IsNullOrEmpty(GetHotkeyValue(textBox)))
            {
                return;
            }

            SetHotkeyValue(textBox, string.Empty);
            SaveSettingsAndRefresh(updateHotkeys: true);
            return;
        }

        if (!HotkeyGesture.TryParse(textBox.Text, out var gesture))
        {
            ApplySettingsToControls();
            StatusText.Text = _localizer.T("Settings.HotkeyInvalid");
            return;
        }

        var value = gesture.ToString();
        if (string.Equals(GetHotkeyValue(textBox), value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (TryGetHotkeyConflict(textBox, value, out var conflictName))
        {
            ApplySettingsToControls();
            StatusText.Text = string.Format(_localizer.T("Settings.HotkeyConflict"), conflictName);
            return;
        }

        SetHotkeyValue(textBox, value);
        SaveSettingsAndRefresh(updateHotkeys: true);
    }

    private string GetHotkeyValue(TextBox textBox)
    {
        if (textBox == StartRecordingHotkeyBox)
        {
            return _settings.StartRecordingHotkey;
        }

        if (textBox == StopRecordingHotkeyBox)
        {
            return _settings.StopRecordingHotkey;
        }

        if (textBox == StartReplayHotkeyBox)
        {
            return _settings.StartReplayHotkey;
        }

        return textBox == StopReplayHotkeyBox
            ? _settings.StopReplayHotkey
            : string.Empty;
    }

    private void SetHotkeyValue(TextBox textBox, string value)
    {
        if (textBox == StartRecordingHotkeyBox)
        {
            _settings.StartRecordingHotkey = value;
        }
        else if (textBox == StopRecordingHotkeyBox)
        {
            _settings.StopRecordingHotkey = value;
        }
        else if (textBox == StartReplayHotkeyBox)
        {
            _settings.StartReplayHotkey = value;
        }
        else if (textBox == StopReplayHotkeyBox)
        {
            _settings.StopReplayHotkey = value;
        }
    }

    private bool TryGetHotkeyConflict(TextBox sourceTextBox, string value, out string conflictName)
    {
        conflictName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hotkeys = new (TextBox TextBox, string Value, string LabelKey)[]
        {
            (StartRecordingHotkeyBox, _settings.StartRecordingHotkey, "Settings.Hotkey.StartRecording"),
            (StopRecordingHotkeyBox, _settings.StopRecordingHotkey, "Settings.Hotkey.StopRecording"),
            (StartReplayHotkeyBox, _settings.StartReplayHotkey, "Settings.Hotkey.StartReplay"),
            (StopReplayHotkeyBox, _settings.StopReplayHotkey, "Settings.Hotkey.StopReplay")
        };

        foreach (var hotkey in hotkeys)
        {
            if (ReferenceEquals(hotkey.TextBox, sourceTextBox))
            {
                continue;
            }

            if (string.Equals(hotkey.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                conflictName = _localizer.T(hotkey.LabelKey);
                return true;
            }
        }

        return false;
    }

    private void SaveSettingsAndRefresh(
        bool updateControls = false,
        bool updateAppearance = false,
        bool updateLocalization = false,
        bool rebuildVisibleEvents = false,
        bool updateHotkeys = false)
    {
        AppSettingsStore.Save(_settings);
        if (updateHotkeys)
        {
            _hotkeyService.UpdateSettings(_settings);
        }

        if (updateControls)
        {
            ApplySettingsToControls();
        }

        if (updateAppearance)
        {
            ApplyAppearance();
        }

        if (updateLocalization)
        {
            ApplyLocalization();
        }

        if (rebuildVisibleEvents)
        {
            RebuildVisibleEvents();
        }

        StatusText.Text = _localizer.T("Settings.Saved");
    }

    private void InitializeCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
    }

    private void ApplyAppearance()
    {
        var themeMode = _settings.ThemeMode;
        var requestedTheme = themeMode switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (_appliedThemeMode != themeMode || RootGrid.RequestedTheme != requestedTheme)
        {
            RootGrid.RequestedTheme = requestedTheme;
            _appliedThemeMode = themeMode;
        }

        var backdropKind = _settings.BackdropKind;
        if (_appliedBackdropKind == backdropKind && SystemBackdrop is not null)
        {
            return;
        }

        SystemBackdrop = new MicaBackdrop
        {
            Kind = backdropKind == "MicaAlt"
                ? Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt
                : Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base
        };
        _appliedBackdropKind = backdropKind;
    }

    private void ApplySettingsToControls()
    {
        _isUpdatingSettingsUi = true;
        try
        {
            LanguageComboBox.SelectedItem = _settings.Language == "en-US"
                ? LanguageEnglishItem
                : LanguageChineseItem;
            ThemeComboBox.SelectedItem = _settings.ThemeMode switch
            {
                "Light" => ThemeLightItem,
                "Dark" => ThemeDarkItem,
                _ => ThemeDefaultItem
            };
            BackdropComboBox.SelectedItem = _settings.BackdropKind == "MicaAlt"
                ? BackdropMicaAltItem
                : BackdropMicaItem;
            RecordDelayBox.Value = _settings.RecordDelaySeconds;
            ReplayDelayBox.Value = _settings.ReplayDelaySeconds;
            ReplayRepeatCountBox.Value = _settings.ReplayRepeatCount;
            ShowMouseMovesSwitch.IsOn = _settings.ShowMouseMovesInList;
            StartRecordingHotkeyBox.Text = _settings.StartRecordingHotkey;
            StopRecordingHotkeyBox.Text = _settings.StopRecordingHotkey;
            StartReplayHotkeyBox.Text = _settings.StartReplayHotkey;
            StopReplayHotkeyBox.Text = _settings.StopReplayHotkey;
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
        HomeNavLabel.Text = _localizer.T("Nav.Home");
        SettingsNavLabel.Text = _localizer.T("Nav.Settings");
        NavToggleLabel.Text = "StepReplay";
        UpdateNavigationSelection(SettingsPage.Visibility == Visibility.Visible);

        TimeHeaderText.Text = _localizer.T("Column.Time");
        TypeHeaderText.Text = _localizer.T("Column.Type");
        DetailHeaderText.Text = _localizer.T("Column.Detail");

        TipInfoBar.Title = _localizer.T("Info.Title");
        TipInfoBar.Message = _localizer.T("Info.Message");

        SettingsTitleText.Text = _localizer.T("Settings.Title");
        LanguageLabelText.Text = _localizer.T("Settings.Language");
        LanguageChineseItem.Content = _localizer.T("Settings.Language.zh-CN");
        LanguageEnglishItem.Content = _localizer.T("Settings.Language.en-US");
        ThemeLabelText.Text = _localizer.T("Settings.Theme");
        ThemeDefaultItem.Content = _localizer.T("Settings.Theme.Default");
        ThemeLightItem.Content = _localizer.T("Settings.Theme.Light");
        ThemeDarkItem.Content = _localizer.T("Settings.Theme.Dark");
        BackdropLabelText.Text = _localizer.T("Settings.Backdrop");
        BackdropMicaItem.Content = _localizer.T("Settings.Backdrop.Mica");
        BackdropMicaAltItem.Content = _localizer.T("Settings.Backdrop.MicaAlt");
        RecordDelayBox.Header = _localizer.T("Settings.RecordDelay");
        ReplayDelayBox.Header = _localizer.T("Settings.ReplayDelay");
        ReplayRepeatCountBox.Header = _localizer.T("Settings.ReplayRepeatCount");
        ShowMouseMovesSwitch.Header = _localizer.T("Settings.ShowMouseMoves");
        HotkeysTitleText.Text = _localizer.T("Settings.Hotkeys");
        StartRecordingHotkeyBox.Header = _localizer.T("Settings.Hotkey.StartRecording");
        StopRecordingHotkeyBox.Header = _localizer.T("Settings.Hotkey.StopRecording");
        StartReplayHotkeyBox.Header = _localizer.T("Settings.Hotkey.StartReplay");
        StopReplayHotkeyBox.Header = _localizer.T("Settings.Hotkey.StopReplay");
        StartRecordingHotkeyBox.PlaceholderText = _localizer.T("Settings.Hotkey.Unset");
        StopRecordingHotkeyBox.PlaceholderText = _localizer.T("Settings.Hotkey.Unset");
        StartReplayHotkeyBox.PlaceholderText = _localizer.T("Settings.Hotkey.Unset");
        StopReplayHotkeyBox.PlaceholderText = _localizer.T("Settings.Hotkey.Unset");

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

    private void UpdateNavigationSelection(bool isSettingsPageVisible)
    {
        HomeNavButton.IsEnabled = true;
        SettingsNavButton.IsEnabled = true;
        HomeNavButton.Opacity = isSettingsPageVisible ? 0.82 : 1;
        SettingsNavButton.Opacity = isSettingsPageVisible ? 1 : 0.82;
        ApplyNavigationButtonVisual(HomeNavButton, !isSettingsPageVisible);
        ApplyNavigationButtonVisual(SettingsNavButton, isSettingsPageVisible);
        AnimateNavSelectionIndicator(isSettingsPageVisible ? 48 : 0);
        AutomationProperties.SetName(HomeNavButton, _localizer.T("Nav.Home"));
        AutomationProperties.SetName(SettingsNavButton, _localizer.T("Nav.Settings"));
        ToolTipService.SetToolTip(HomeNavButton, _localizer.T("Nav.Home"));
        ToolTipService.SetToolTip(SettingsNavButton, _localizer.T("Nav.Settings"));
        ToolTipService.SetToolTip(NavToggleButton, "StepReplay");
    }

    private void SetNavigationEnabled(bool enabled)
    {
        if (!enabled)
        {
            HomeNavButton.IsEnabled = false;
            SettingsNavButton.IsEnabled = false;
            NavToggleButton.IsEnabled = false;
            HomeNavButton.Opacity = 0.6;
            SettingsNavButton.Opacity = 0.6;
            return;
        }

        NavToggleButton.IsEnabled = true;
        UpdateNavigationSelection(SettingsPage.Visibility == Visibility.Visible);
    }

    private static void ApplyNavigationButtonVisual(Button button, bool isSelected)
    {
        button.BorderThickness = new Thickness(0);
        button.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Background = isSelected
            ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Shadow = null;
        button.Translation = Vector3.Zero;
    }

    private void AnimateNavSelectionIndicator(double targetY)
    {
        var animation = new DoubleAnimation
        {
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, NavSelectionIndicatorTransform);
        Storyboard.SetTargetProperty(animation, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void AnimateNavigationPane(bool expand)
    {
        var targetWidth = expand ? 176 : 64;
        var targetOpacity = expand ? 1 : 0;
        var duration = TimeSpan.FromMilliseconds(240);

        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateWidthAnimation(NavPane, targetWidth, duration));
        storyboard.Children.Add(CreateOpacityAnimation(HomeNavLabel, targetOpacity, duration));
        storyboard.Children.Add(CreateOpacityAnimation(SettingsNavLabel, targetOpacity, duration));
        storyboard.Children.Add(CreateOpacityAnimation(NavToggleLabel, targetOpacity, duration));
        storyboard.Begin();
    }

    private static DoubleAnimation CreateWidthAnimation(FrameworkElement target, double width, TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            To = width,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Width");
        return animation;
    }

    private static DoubleAnimation CreateOpacityAnimation(UIElement target, double opacity, TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            To = opacity,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        return animation;
    }

    private static HotkeyModifiers GetCurrentHotkeyModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsKeyDown(0x11) || IsKeyDown(0xA2) || IsKeyDown(0xA3))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (IsKeyDown(0x12) || IsKeyDown(0xA4) || IsKeyDown(0xA5))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsKeyDown(0x10) || IsKeyDown(0xA0) || IsKeyDown(0xA1))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsKeyDown(0x5B) || IsKeyDown(0x5C))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey) =>
        (Win32.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;

    private void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            switch (action)
            {
                case HotkeyAction.StartRecording:
                    await RequestStartRecordingAsync();
                    break;
                case HotkeyAction.StopRecording:
                    StopRecording();
                    break;
                case HotkeyAction.StartReplay:
                    await RequestStartReplayAsync();
                    break;
                case HotkeyAction.StopReplay:
                    ForceStopReplay();
                    break;
            }
        });
    }

    private void OnHotkeyCaptured(object? sender, HotkeyGesture gesture)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_capturingHotkeyBox is null)
            {
                return;
            }

            _capturingHotkeyBox.Text = gesture.ToString();
            ApplyHotkeyTextBox(_capturingHotkeyBox);
            _capturingHotkeyBox = null;
        });
    }

    private void OnHotkeyCaptureCanceled(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplySettingsToControls();
            _capturingHotkeyBox = null;
            StatusText.Text = _localizer.T("Settings.HotkeyCaptureCanceled");
        });
    }

    private void OnHotkeyCaptureCleared(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_capturingHotkeyBox is null)
            {
                return;
            }

            _capturingHotkeyBox.Text = string.Empty;
            ApplyHotkeyTextBox(_capturingHotkeyBox);
            _capturingHotkeyBox = null;
            StatusText.Text = _localizer.T("Settings.HotkeyCleared");
        });
    }

    private void OnHotkeyCaptureNeedsModifier(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = _localizer.T("Settings.HotkeyNeedModifier");
        });
    }

    private void RestoreIdleControls()
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ReplayButton.IsEnabled = _events.Count > 0;
        ClearButton.IsEnabled = _events.Count > 0;
        SaveButton.IsEnabled = _events.Count > 0;
        LoadButton.IsEnabled = true;
        SetNavigationEnabled(true);
        BusyRing.IsActive = false;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _recordCts?.Cancel();
        _replayCts?.Cancel();
        _recorder.Dispose();
        _hotkeyService.Dispose();
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
