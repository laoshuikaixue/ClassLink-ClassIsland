using Avalonia;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassLink.Controls;
using ClassLink.Services;

namespace ClassLink.Components;

[ComponentInfo(
    "2E06C298-D5B7-4820-9028-A1BE1D97981E",
    "ClassLink 歌词",
    "\uEBC9",
    "显示播放器推送的逐行、逐字与 TTML 高级歌词。")]
public sealed class ClassLinkLyricsComponent : ComponentBase<ClassLinkLyricsComponentConfig>
{
  private readonly ClassLinkLyricsPresenter _presenter;
  private bool _settingsSubscribed;

  public ClassLinkLyricsComponent(ClassLinkStateService state)
  {
    _presenter = new ClassLinkLyricsPresenter(state);
    Content = _presenter;
    AttachedToVisualTree += OnAttached;
    DetachedFromVisualTree += OnDetached;
  }

  private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
  {
    ApplySettings();
    if (_settingsSubscribed) return;
    Settings.PropertyChanged += SettingsOnPropertyChanged;
    _settingsSubscribed = true;
  }

  private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
  {
    if (!_settingsSubscribed) return;
    Settings.PropertyChanged -= SettingsOnPropertyChanged;
    _settingsSubscribed = false;
  }

  private void SettingsOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
      ApplySettings();

  private void ApplySettings()
  {
    _presenter.ShowTranslation = Settings.ShowTranslation;
    _presenter.ShowRomanization = Settings.ShowRomanization;
    _presenter.WordByWord = Settings.WordByWord;
    _presenter.HideWhenDisconnected = Settings.HideWhenDisconnected;
    _presenter.RefreshSettings();
  }
}
