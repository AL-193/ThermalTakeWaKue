using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using TtLcd.Ipc;
using TtLcd.Protocol;
using TtLcd.Sensors;

namespace TtLcd.Settings;

public partial class MainWindow : Window
{
    private static readonly int[] IntervalChoices = [500, 1000, 2000];

    private readonly ConfigClient _client = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private AppConfig? _config;
    private IReadOnlyList<SensorReading> _sensors = [];
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();

        Title = Strings.Title;
        PreviewLabel.Text = Strings.PreviewLabel;
        DisplayLabel.Text = Strings.Display;
        FanLabel.Text = Strings.Fan;
        UnitLabel.Text = Strings.Unit;
        IntervalLabel.Text = Strings.Interval;
        GpuNote.Text = Strings.GpuNote;
        ReloadButton.Content = Strings.Reload;
        ApplyButton.Content = Strings.Apply;

        SourceGpu.IsCheckedChanged += (_, _) => GpuNote.IsVisible = SourceGpu.IsChecked == true;
        ReloadButton.Click += async (_, _) => await LoadAsync();
        ApplyButton.Click += async (_, _) => await ApplyAsync();
        _previewTimer.Tick += async (_, _) => await RefreshPreviewAsync();
        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => _previewTimer.Stop();
    }

    private async Task LoadAsync()
    {
        SetEnabled(false, Strings.Connecting);
        try
        {
            var state = await _client.GetStateAsync(allSensors: true);
            if (!state.Ok || state.Config is null || state.Sensors is null)
            {
                SetEnabled(false, Strings.Error(state.Error));
                return;
            }

            _config = state.Config;
            _sensors = state.Sensors;
            Populate(state.Selection ?? new Dictionary<DisplayRole, string>());
            ShowPreview(state);
            SetEnabled(true, Strings.Connected);
            _previewTimer.Start();
        }
        catch (Exception e) when (IsConnectionError(e))
        {
            _previewTimer.Stop();
            SetEnabled(false, Strings.NotReachable);
            ReloadButton.IsEnabled = true;
        }
    }

    private void Populate(IReadOnlyDictionary<DisplayRole, string> selection)
    {
        var source = ConfigRules.GetDisplaySource(selection, _sensors);
        SourceCpu.IsChecked = source == DisplaySource.Cpu;
        SourceGpu.IsChecked = source == DisplaySource.Gpu;
        SourceGpu.IsEnabled = SensorSelector.AutoSelect(DisplayRole.GpuTemperature, _sensors) is not null;

        var fans = _sensors
            .Where(s => s.Kind == SensorKind.Fan)
            .Select(s => new Choice<string>(s.Id, $"{s.Hardware} – {s.Name}  ({Format(s.Value)} RPM)"))
            .Prepend(new Choice<string>(SensorSelector.None, Strings.Off))
            .ToList();
        var fanId = selection.GetValueOrDefault(DisplayRole.FanSpeed) ?? SensorSelector.None;
        FanBox.ItemsSource = fans;
        FanBox.SelectedItem = fans.FirstOrDefault(f => f.Value == fanId) ?? fans[0];

        UnitCelsius.IsChecked = _config!.TemperatureUnit == TemperatureUnit.Celsius;
        UnitFahrenheit.IsChecked = _config.TemperatureUnit == TemperatureUnit.Fahrenheit;

        var intervals = IntervalChoices.Append(_config.SensorIntervalMs).Distinct().Order()
            .Select(ms => new Choice<int>(ms, Strings.Seconds(ms)))
            .ToList();
        IntervalBox.ItemsSource = intervals;
        IntervalBox.SelectedItem = intervals.First(i => i.Value == _config.SensorIntervalMs);
    }

    private async Task ApplyAsync()
    {
        if (_config is null)
            return;

        var config = _config.Clone();
        ConfigRules.SetDisplaySource(config, SourceGpu.IsChecked == true ? DisplaySource.Gpu : DisplaySource.Cpu, _sensors);
        if (FanBox.SelectedItem is Choice<string> fan)
            config.Sensors[DisplayRole.FanSpeed] = fan.Value;
        config.TemperatureUnit = UnitFahrenheit.IsChecked == true ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius;
        if (IntervalBox.SelectedItem is Choice<int> interval)
            config.SensorIntervalMs = interval.Value;

        SetEnabled(false, Strings.Saving);
        try
        {
            var response = await _client.SetConfigAsync(config);
            if (response.Ok)
                _config = response.Config ?? config;
            SetEnabled(true, response.Ok ? Strings.Saved : Strings.Error(response.Error));
        }
        catch (Exception e) when (IsConnectionError(e))
        {
            SetEnabled(false, Strings.NotReachable);
            ReloadButton.IsEnabled = true;
        }
    }

    private async Task RefreshPreviewAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            ShowPreview(await _client.GetStateAsync(allSensors: false));
        }
        catch (Exception e) when (IsConnectionError(e))
        {
            _previewTimer.Stop();
            SetEnabled(false, Strings.NotReachable);
            ReloadButton.IsEnabled = true;
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>The three values the LA series LCD shows.</summary>
    private void ShowPreview(IpcResponse state)
    {
        if (state.Sensors is null || state.Selection is null || state.Config is null)
            return;

        double? Value(DisplayRole role) =>
            state.Selection.TryGetValue(role, out var id) ? state.Sensors.FirstOrDefault(s => s.Id == id)?.Value : null;

        var fahrenheit = state.Config.TemperatureUnit == TemperatureUnit.Fahrenheit;
        var temperature = Value(DisplayRole.CpuTemperature) is { } c ? (fahrenheit ? c * 9 / 5 + 32 : c) : (double?)null;
        PreviewText.Text =
            $"{Format(temperature)} {(fahrenheit ? "°F" : "°C")}    {Format(Value(DisplayRole.CpuUsage))} %    {Format(Value(DisplayRole.FanSpeed))} RPM";
    }

    private void SetEnabled(bool enabled, string status)
    {
        Form.IsEnabled = enabled;
        ApplyButton.IsEnabled = enabled;
        ReloadButton.IsEnabled = enabled;
        StatusText.Text = status;
    }

    private static string Format(double? value) =>
        value is { } v && double.IsFinite(v) ? Math.Floor(v).ToString("0", CultureInfo.CurrentCulture) : "–";

    private static bool IsConnectionError(Exception e) =>
        e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException
            or InvalidDataException or JsonException;

    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }
}
