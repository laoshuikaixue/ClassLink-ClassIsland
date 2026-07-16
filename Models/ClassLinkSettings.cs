using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassLink.Models;

public sealed class ClassLinkSettings : ObservableRecipient
{
  public const int DefaultPort = 38973;

  private int _port = DefaultPort;
  private string _token = string.Empty;

  public int Port
  {
    get => _port;
    set
    {
      var normalized = Math.Clamp(value, 1024, 65535);
      if (_port == normalized) return;
      _port = normalized;
      OnPropertyChanged();
    }
  }

  public string Token
  {
    get => _token;
    set
    {
      if (_token == value) return;
      _token = value;
      OnPropertyChanged();
    }
  }
}
