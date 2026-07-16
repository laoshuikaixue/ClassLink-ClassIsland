using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassLink.Components;

public sealed class ClassLinkNowPlayingComponentConfig : ObservableRecipient
{
  private bool _showCover = true;
  private bool _hideWhenDisconnected;

  public bool ShowCover
  {
    get => _showCover;
    set => SetProperty(ref _showCover, value);
  }

  public bool HideWhenDisconnected
  {
    get => _hideWhenDisconnected;
    set => SetProperty(ref _hideWhenDisconnected, value);
  }
}
