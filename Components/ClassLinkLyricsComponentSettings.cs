using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassLink.Components;

public sealed class ClassLinkLyricsComponentSettings : ComponentBase<ClassLinkLyricsComponentConfig>
{
  private bool _initialized;

  public ClassLinkLyricsComponentSettings() => AttachedToVisualTree += OnAttached;

  private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
  {
    if (_initialized) return;
    _initialized = true;
    var translation = CreateToggle("显示翻译并使用 / 连接", Settings.ShowTranslation,
        value => Settings.ShowTranslation = value);
    var romanization = CreateToggle("显示音译", Settings.ShowRomanization,
        value => Settings.ShowRomanization = value);
    var wordByWord = CreateToggle("启用逐字高亮", Settings.WordByWord,
        value => Settings.WordByWord = value);
    var hide = CreateToggle("断开连接时隐藏", Settings.HideWhenDisconnected,
        value => Settings.HideWhenDisconnected = value);
    Content = new StackPanel
    {
      Spacing = 8,
      Children = { translation, romanization, wordByWord, hide }
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
