using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassLink.Components;

public sealed class ClassLinkNowPlayingComponentSettings : ComponentBase<ClassLinkNowPlayingComponentConfig>
{
  private bool _initialized;

  public ClassLinkNowPlayingComponentSettings() => AttachedToVisualTree += OnAttached;

  private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
  {
    if (_initialized) return;
    _initialized = true;
    var cover = CreateToggle("显示封面", Settings.ShowCover, value => Settings.ShowCover = value);
    var hide = CreateToggle("断开连接时隐藏", Settings.HideWhenDisconnected,
        value => Settings.HideWhenDisconnected = value);
    Content = new StackPanel
    {
      Spacing = 8,
      Children = { cover, hide }
    };
  }

  private static ToggleSwitch CreateToggle(string text, bool value, Action<bool> changed)
  {
    var control = new ToggleSwitch
    {
      Content = text,
      IsChecked = value,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    control.IsCheckedChanged += (_, _) => changed(control.IsChecked == true);
    return control;
  }
}
