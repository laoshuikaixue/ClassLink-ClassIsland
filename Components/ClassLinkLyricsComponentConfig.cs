using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassLink.Components;

public class ClassLinkLyricsComponentConfig : ObservableRecipient
{
  private bool _showTranslation = true;
  private bool _showRomanization;
  private bool _wordByWord = true;
  private bool _hideWhenDisconnected;

  public bool ShowTranslation
  {
    get => _showTranslation;
    set => SetProperty(ref _showTranslation, value);
  }

  public bool ShowRomanization
  {
    get => _showRomanization;
    set => SetProperty(ref _showRomanization, value);
  }

  public bool WordByWord
  {
    get => _wordByWord;
    set => SetProperty(ref _wordByWord, value);
  }

  public bool HideWhenDisconnected
  {
    get => _hideWhenDisconnected;
    set => SetProperty(ref _hideWhenDisconnected, value);
  }
}
