using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassLink.Protocol;
using ClassLink.Services;

namespace ClassLink.Controls;

public sealed class ClassLinkLyricsPresenter : UserControl
{
  private const double TransitionDurationMs = 380;
  private static readonly TimeSpan WordInterval = TimeSpan.FromMilliseconds(33);
  private static readonly TimeSpan TransitionFrameInterval = TimeSpan.FromMilliseconds(16);
  private readonly ClassLinkStateService _state;
  private readonly Grid _root = new();
  private StackPanel _front = CreateLayer();
  private StackPanel _back = CreateLayer();
  private readonly DispatcherTimer _timer = new();
  private readonly DispatcherTimer _transitionTimer = new();
  private readonly List<WordLyricsText> _activeWordControls = [];
  private string _frameSignature = string.Empty;
  private bool _isAttached;
  private TransitionState? _transition;

  public bool ShowTranslation { get; set; } = true;
  public bool ShowRomanization { get; set; }
  public bool WordByWord { get; set; } = true;
  public bool HideWhenDisconnected { get; set; }
  public double BaseFontSize { get; set; } = 15;

  public ClassLinkLyricsPresenter(ClassLinkStateService state)
  {
    _state = state;
    _root.ClipToBounds = true;
    _root.Children.Add(_back);
    _root.Children.Add(_front);
    Content = _root;
    _timer.Tick += TimerOnTick;
    _transitionTimer.Interval = TransitionFrameInterval;
    _transitionTimer.Tick += TransitionTimerOnTick;
    AttachedToVisualTree += (_, _) => Attach();
    DetachedFromVisualTree += (_, _) => Detach();
  }

  public void RefreshSettings()
  {
    _frameSignature = string.Empty;
    Refresh();
  }

  private void Attach()
  {
    if (_isAttached) return;
    _isAttached = true;
    _state.Changed += StateOnChanged;
    Refresh();
  }

  private void Detach()
  {
    if (!_isAttached) return;
    _isAttached = false;
    _state.Changed -= StateOnChanged;
    _timer.Stop();
    CompleteTransition();
  }

  private void StateOnChanged(object? sender, EventArgs e) => Refresh();

  private void TimerOnTick(object? sender, EventArgs e)
  {
    _timer.Stop();
    Refresh();
  }

  private void Refresh()
  {
    if (!_isAttached) return;
    var runtime = _state.Current;
    if (!_state.IsConnected || runtime == null)
    {
      IsVisible = !HideWhenDisconnected;
      var appName = string.IsNullOrWhiteSpace(runtime?.State.AppName)
          ? "播放器"
          : runtime.State.AppName;
      var status = appName == "播放器" ? "播放器未连接" : $"{appName} 未连接";
      ApplyStatus(status, $"disconnected:{appName}");
      _timer.Stop();
      return;
    }

    IsVisible = true;
    if (runtime.State.Track == null)
    {
      var appName = string.IsNullOrWhiteSpace(runtime.State.AppName)
          ? "播放器"
          : runtime.State.AppName;
      var status = appName == "播放器" ? "等待播放器播放" : $"等待 {appName} 播放";
      ApplyStatus(status, $"empty:{appName}");
      _timer.Stop();
      return;
    }

    var lyrics = runtime.State.Lyrics;
    if (string.Equals(lyrics.Status, "loading", StringComparison.OrdinalIgnoreCase))
    {
      ApplyStatus("歌词加载中…", $"loading:{runtime.State.TrackKey}");
      UpdateCadence(runtime, []);
      return;
    }
    if (!string.Equals(lyrics.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
        lyrics.Lines.Count == 0)
    {
      ApplyStatus("暂无歌词", $"none:{runtime.State.TrackKey}");
      UpdateCadence(runtime, []);
      return;
    }

    var position = _state.GetCurrentPositionMs() + runtime.Playback.LyricOffsetMs;
    var activeLines = LyricsLineSelector.SelectActive(lyrics, position);
    if (activeLines.Count == 0)
    {
      ApplyStatus("♪", $"prelude:{runtime.State.TrackKey}");
      UpdateCadence(runtime, activeLines);
      return;
    }

    var signature = BuildSignature(activeLines);
    if (!string.Equals(signature, _frameSignature, StringComparison.Ordinal))
    {
      _frameSignature = signature;
      RebuildFrame(activeLines, position);
    }
    else
    {
      UpdateActiveControls(activeLines, position);
    }
    UpdateCadence(runtime, activeLines);
  }

  private void ApplyStatus(string text, string signature)
  {
    if (string.Equals(_frameSignature, signature, StringComparison.Ordinal)) return;
    _frameSignature = signature;
    CompleteTransition();
    _activeWordControls.Clear();
    _back.Children.Clear();
    _back.Children.Add(new TextBlock
    {
      Text = text,
      FontSize = BaseFontSize,
      Opacity = 0.68,
      TextAlignment = TextAlignment.Center,
      HorizontalAlignment = HorizontalAlignment.Stretch,
      VerticalAlignment = VerticalAlignment.Center
    });
    StartTransition();
  }

  private void RebuildFrame(IReadOnlyList<LyricsLineSelection> activeLines, double position)
  {
    CompleteTransition();
    _activeWordControls.Clear();
    _back.Children.Clear();
    var hasDuet = activeLines.Any(item => item.IsDuetSide);
    foreach (var selection in activeLines)
    {
      var line = selection.Line;
      var control = new WordLyricsText
      {
        Line = line,
        BackgroundLine = selection.BackgroundLine,
        PositionMs = position,
        LineFontSize = line.IsBG ? Math.Max(9, BaseFontSize * 0.76) : BaseFontSize,
        Foreground = Foreground ?? Brushes.White,
        TextAlignment = selection.IsDuetSide
              ? TextAlignment.Right
              : hasDuet
                  ? TextAlignment.Left
                  : TextAlignment.Center,
        WordByWord = WordByWord &&
                     (HasWordTiming(line) ||
                      selection.BackgroundLine != null && HasWordTiming(selection.BackgroundLine)),
        ShowTranslation = ShowTranslation,
        ShowRomanization = ShowRomanization,
        Opacity = line.IsBG ? 0.72 : 1,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MaxWidth = 620
      };
      _back.Children.Add(control);
      _activeWordControls.Add(control);
    }
    StartTransition();
  }

  private void UpdateActiveControls(
      IReadOnlyList<LyricsLineSelection> activeLines,
      double position)
  {
    if (_activeWordControls.Count != activeLines.Count)
    {
      RebuildFrame(activeLines, position);
      return;
    }
    for (var index = 0; index < activeLines.Count; index++)
    {
      var control = _activeWordControls[index];
      var line = activeLines[index].Line;
      control.Line = line;
      control.BackgroundLine = activeLines[index].BackgroundLine;
      control.PositionMs = position;
      var background = activeLines[index].BackgroundLine;
      control.WordByWord = WordByWord &&
                           (HasWordTiming(line) || background != null && HasWordTiming(background));
    }
  }

  private void StartTransition()
  {
    var oldLayer = _front;
    var newLayer = _back;
    var oldTransform = (TranslateTransform)oldLayer.RenderTransform!;
    var newTransform = (TranslateTransform)newLayer.RenderTransform!;
    var offset = Math.Max(20, BaseFontSize * 1.5);
    oldLayer.Opacity = oldLayer.Children.Count == 0 ? 0 : 1;
    oldTransform.Y = 0;
    newLayer.Opacity = 0;
    newTransform.Y = offset;
    newLayer.IsVisible = true;
    oldLayer.IsVisible = true;

    _front = newLayer;
    _back = oldLayer;
    _transition = new TransitionState(
        oldLayer,
        oldTransform,
        newLayer,
        newTransform,
        Environment.TickCount64,
        offset);
    _transitionTimer.Start();
  }

  private void TransitionTimerOnTick(object? sender, EventArgs e)
  {
    var transition = _transition;
    if (transition == null) return;
    var progress = Math.Clamp(
        (Environment.TickCount64 - transition.StartedAtTick) / TransitionDurationMs,
        0,
        1);
    var eased = 1 - Math.Pow(1 - progress, 3);
    transition.OldLayer.Opacity = 1 - eased;
    transition.OldTransform.Y = -transition.Offset * eased;
    transition.NewLayer.Opacity = eased;
    transition.NewTransform.Y = transition.Offset * (1 - eased);
    if (progress >= 1) CompleteTransition();
  }

  private void UpdateCadence(
      ClassLinkRuntimeSnapshot runtime,
      IReadOnlyList<LyricsLineSelection> activeLines)
  {
    _timer.Stop();
    if (!_isAttached ||
        !string.Equals(runtime.Playback.State, "playing", StringComparison.OrdinalIgnoreCase)) return;

    if (WordByWord && activeLines.Any(item =>
            HasWordTiming(item.Line) ||
            item.BackgroundLine != null && HasWordTiming(item.BackgroundLine)))
    {
      _timer.Interval = WordInterval;
      _timer.Start();
      return;
    }

    var position = _state.GetCurrentPositionMs() + runtime.Playback.LyricOffsetMs;
    var nextBoundary = runtime.State.Lyrics.Lines
        .SelectMany(line => new[] { line.StartTime, line.EndTime })
        .Where(time => time > position + 1)
        .DefaultIfEmpty(position + 1000)
        .Min();
    var delay = (nextBoundary - position) / Math.Max(0.1, runtime.Playback.Speed);
    _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(delay, 30, 1000));
    _timer.Start();
  }

  private string BuildSignature(IReadOnlyList<LyricsLineSelection> activeLines) => string.Join(
      "\u001f",
      activeLines.Select(item => string.Join(
          "\u001e",
          item.LineIndex,
          item.Line.StartTime,
          item.Line.EndTime,
          item.Line.Text,
          ShowTranslation ? item.Line.TranslatedLyric : string.Empty,
          ShowRomanization ? item.Line.RomanLyric : string.Empty,
          item.Line.IsBG,
          item.IsDuetSide,
          item.BackgroundLine?.StartTime,
          item.BackgroundLine?.EndTime,
          item.BackgroundLine?.Text)));

  private static bool HasWordTiming(LyricLineMessage line) =>
      line.Words.Any(word => word.EndTime > word.StartTime);

  private static StackPanel CreateLayer() => new()
  {
    Spacing = 0,
    HorizontalAlignment = HorizontalAlignment.Stretch,
    VerticalAlignment = VerticalAlignment.Center,
    RenderTransform = new TranslateTransform(),
    IsVisible = false,
    Opacity = 0
  };

  private void CompleteTransition()
  {
    _transitionTimer.Stop();
    _transition = null;
    _front.IsVisible = true;
    _front.Opacity = 1;
    ((TranslateTransform)_front.RenderTransform!).Y = 0;
    _back.Children.Clear();
    _back.IsVisible = false;
    _back.Opacity = 0;
    ((TranslateTransform)_back.RenderTransform!).Y = 0;
  }

  private sealed record TransitionState(
      StackPanel OldLayer,
      TranslateTransform OldTransform,
      StackPanel NewLayer,
      TranslateTransform NewTransform,
      long StartedAtTick,
      double Offset);
}
