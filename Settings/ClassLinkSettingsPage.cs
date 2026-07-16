using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassLink.Models;
using ClassLink.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace ClassLink.Settings;

[SettingsPageInfo(
    "classlink.general",
    "ClassLink",
    "\uE8D6",
    "\uEBC9",
    SettingsPageCategory.External)]
public sealed partial class ClassLinkSettingsPage : SettingsPageBase
{
  private readonly Plugin _plugin;
  private readonly ClassLinkStateService _state;
  private readonly TextBox _portBox;
  private readonly TextBox _tokenBox;
  private readonly TextBlock _portValidationText;
  private readonly TextBlock _endpointText;
  private readonly TextBlock _connectionStatusText;
  private readonly TextBlock _activePlayerText;
  private readonly TextBlock _lastSeenText;
  private readonly InfoBar _listenerInfoBar;
  private bool _subscribed;

  public ClassLinkSettings Settings => _plugin.Settings;

  public ClassLinkSettingsPage()
  {
    _plugin = null!;
    _state = null!;
    InitializeComponent();

    _portBox = this.FindControl<TextBox>("PortBox")!;
    _tokenBox = this.FindControl<TextBox>("TokenBox")!;
    _portValidationText = this.FindControl<TextBlock>("PortValidationText")!;
    _endpointText = this.FindControl<TextBlock>("EndpointText")!;
    _connectionStatusText = this.FindControl<TextBlock>("ConnectionStatusText")!;
    _activePlayerText = this.FindControl<TextBlock>("ActivePlayerText")!;
    _lastSeenText = this.FindControl<TextBlock>("LastSeenText")!;
    _listenerInfoBar = this.FindControl<InfoBar>("ListenerInfoBar")!;
  }

  [ActivatorUtilitiesConstructor]
  public ClassLinkSettingsPage(Plugin plugin, ClassLinkStateService state) : this()
  {
    _plugin = plugin;
    _state = state;

    _portBox.Text = Settings.Port.ToString();
    _tokenBox.Text = Settings.Token;
    _portBox.LostFocus += (_, _) => SavePort();
    _portBox.KeyDown += PortBoxOnKeyDown;
    this.FindControl<Button>("CopyTokenButton")!.Click += CopyTokenButtonOnClick;
    this.FindControl<Button>("RegenerateTokenButton")!.Click += RegenerateTokenButtonOnClick;
    AttachedToVisualTree += (_, _) => Attach();
    DetachedFromVisualTree += (_, _) => Detach();
    UpdateStatus();
  }

  private void Attach()
  {
    if (_subscribed) return;
    _state.Changed += StateOnChanged;
    _subscribed = true;
    UpdateStatus();
  }

  private void Detach()
  {
    if (!_subscribed) return;
    _state.Changed -= StateOnChanged;
    _subscribed = false;
  }

  private void StateOnChanged(object? sender, EventArgs e) => UpdateStatus();

  private void PortBoxOnKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.Key != Key.Enter) return;
    SavePort();
    e.Handled = true;
  }

  private void SavePort()
  {
    if (int.TryParse(_portBox.Text, out var port) && port is >= 1024 and <= 65535)
    {
      _portValidationText.IsVisible = false;
      Settings.Port = port;
      _portBox.Text = Settings.Port.ToString();
      UpdateStatus();
      return;
    }

    _portValidationText.IsVisible = true;
    _portBox.Text = Settings.Port.ToString();
  }

  private async void CopyTokenButtonOnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
    if (clipboard != null) await clipboard.SetTextAsync(Settings.Token);
  }

  private void RegenerateTokenButtonOnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    _plugin.RegenerateToken();
    _tokenBox.Text = Settings.Token;
  }

  private void UpdateStatus()
  {
    var listenerStatus = _state.ListenerStatus;
    _listenerInfoBar.Title = listenerStatus.StartsWith("正在监听", StringComparison.Ordinal)
        ? "监听服务运行正常"
        : listenerStatus.StartsWith("监听失败", StringComparison.Ordinal)
            ? "监听服务启动失败"
            : "监听服务";
    _listenerInfoBar.Message = listenerStatus;
    _listenerInfoBar.Severity = listenerStatus.StartsWith("正在监听", StringComparison.Ordinal)
        ? InfoBarSeverity.Success
        : listenerStatus.StartsWith("监听失败", StringComparison.Ordinal)
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Informational;

    _endpointText.Text = $"http://127.0.0.1:{Settings.Port}";
    _connectionStatusText.Text = _state.IsConnected ? "已连接" : "等待播放器连接";
    var appName = _state.Current?.State.AppName;
    _activePlayerText.Text = string.IsNullOrWhiteSpace(appName) ? "暂无" : appName;
    _lastSeenText.Text = _state.LastSeenUtc == DateTime.MinValue
        ? "尚未收到数据"
        : _state.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
  }
}
