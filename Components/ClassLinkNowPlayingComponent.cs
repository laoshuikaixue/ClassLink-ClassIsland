using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassLink.Services;

namespace ClassLink.Components;

[ComponentInfo(
    "BFE1867E-1E9E-4932-90A8-6209CE60BA66",
    "ClassLink 正在播放",
    "\uE8D6",
    "显示播放器的封面、歌曲名与歌手。")]
public sealed class ClassLinkNowPlayingComponent : ComponentBase<ClassLinkNowPlayingComponentConfig>
{
  private static readonly TimeSpan CoverTransitionDuration = TimeSpan.FromMilliseconds(180);
  private readonly ClassLinkStateService _state;
  private readonly Grid _root;
  private readonly Border _coverContainer;
  private Image _frontCover = CreateCoverImage();
  private Image _backCover = CreateCoverImage();
  private readonly TextBlock _titleText;
  private readonly TextBlock _artistText;
  private Bitmap? _displayedCover;
  private CancellationTokenSource? _coverTransitionCancellation;
  private bool _subscribed;

  public ClassLinkNowPlayingComponent(ClassLinkStateService state)
  {
    _state = state;
    var coverLayers = new Grid
    {
      Children = { _backCover, _frontCover }
    };
    _coverContainer = new Border
    {
      Width = 34,
      Height = 34,
      Margin = new Thickness(0, 0, 7, 0),
      CornerRadius = new CornerRadius(6),
      ClipToBounds = true,
      Child = coverLayers
    };
    _titleText = new TextBlock
    {
      FontSize = 13,
      FontWeight = FontWeight.SemiBold,
      TextTrimming = TextTrimming.CharacterEllipsis
    };
    _artistText = new TextBlock
    {
      FontSize = 10,
      Opacity = 0.68,
      TextTrimming = TextTrimming.CharacterEllipsis
    };
    var info = new StackPanel
    {
      VerticalAlignment = VerticalAlignment.Center,
      Children = { _titleText, _artistText }
    };
    _root = new Grid
    {
      ColumnDefinitions = new ColumnDefinitions("Auto,*"),
      VerticalAlignment = VerticalAlignment.Center
    };
    _root.Children.Add(_coverContainer);
    Grid.SetColumn(info, 1);
    _root.Children.Add(info);
    Content = _root;
    AttachedToVisualTree += OnAttached;
    DetachedFromVisualTree += OnDetached;
  }

  private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
  {
    ApplySettings();
    UpdateState();
    if (_subscribed) return;
    _state.Changed += StateOnChanged;
    Settings.PropertyChanged += SettingsOnPropertyChanged;
    _subscribed = true;
  }

  private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
  {
    if (!_subscribed) return;
    _state.Changed -= StateOnChanged;
    Settings.PropertyChanged -= SettingsOnPropertyChanged;
    _subscribed = false;
    StopCoverTransition(true);
  }

  private void StateOnChanged(object? sender, EventArgs e) => UpdateState();

  private void SettingsOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
  {
    ApplySettings();
    UpdateState();
  }

  private void ApplySettings()
  {
    _coverContainer.IsVisible = Settings.ShowCover;
  }

  private void UpdateState()
  {
    var runtime = _state.Current;
    if (!_state.IsConnected || runtime?.State.Track == null)
    {
      _root.IsVisible = !Settings.HideWhenDisconnected;
      _titleText.Text = $"{runtime?.State.AppName ?? "播放器"}未连接";
      _artistText.Text = string.Empty;
      SetCover(null);
      return;
    }

    _root.IsVisible = true;
    var track = runtime.State.Track;
    _titleText.Text = track.Title;
    _artistText.Text = string.Join(" / ", track.Artists.Select(artist => artist.Name));
    SetCover(Settings.ShowCover ? _state.Cover : null);
  }

  private void SetCover(Bitmap? cover)
  {
    if (ReferenceEquals(_displayedCover, cover)) return;
    _displayedCover = cover;
    StopCoverTransition();
    var oldImage = _frontCover;
    var newImage = _backCover;
    newImage.Source = cover;
    newImage.Opacity = cover == null ? 0 : 1;
    oldImage.Opacity = oldImage.Source == null ? 0 : 1;
    _frontCover = newImage;
    _backCover = oldImage;
    var cancellation = new CancellationTokenSource();
    _coverTransitionCancellation = cancellation;
    _ = RunCoverTransitionAsync(oldImage, newImage, cancellation);
  }

  private async Task RunCoverTransitionAsync(
      Image oldImage,
      Image newImage,
      CancellationTokenSource cancellation)
  {
    try
    {
      await Task.WhenAll(
          CreateOpacityAnimation(oldImage.Opacity, 0).RunAsync(oldImage, cancellation.Token),
          CreateOpacityAnimation(0, newImage.Source == null ? 0 : 1).RunAsync(newImage, cancellation.Token));
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
      if (ReferenceEquals(_coverTransitionCancellation, cancellation))
      {
        _coverTransitionCancellation = null;
        oldImage.Source = null;
        oldImage.Opacity = 0;
        newImage.Opacity = newImage.Source == null ? 0 : 1;
      }
      cancellation.Dispose();
    }
  }

  private static Animation CreateOpacityAnimation(double from, double to) => new()
  {
    Duration = CoverTransitionDuration,
    Easing = new CubicEaseOut(),
    FillMode = FillMode.None,
    Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, to) } }
        }
  };

  private static Image CreateCoverImage() => new()
  {
    Stretch = Stretch.UniformToFill,
    Opacity = 0
  };

  private void StopCoverTransition(bool settle = false)
  {
    var cancellation = _coverTransitionCancellation;
    _coverTransitionCancellation = null;
    cancellation?.Cancel();
    if (!settle) return;
    _frontCover.Source = _displayedCover;
    _frontCover.Opacity = _displayedCover == null ? 0 : 1;
    _backCover.Source = null;
    _backCover.Opacity = 0;
  }
}
