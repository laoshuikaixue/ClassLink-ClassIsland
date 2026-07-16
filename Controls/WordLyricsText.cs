using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassLink.Protocol;

namespace ClassLink.Controls;

public sealed class WordLyricsText : Control
{
  private const double SweepFeatherWidth = 4;
  private LyricLineMessage? _line;
  private LyricLineMessage? _backgroundLine;
  private double _positionMs;
  private double _lineFontSize = 15;
  private IBrush _foreground = Brushes.White;
  private TextAlignment _textAlignment = TextAlignment.Center;
  private bool _wordByWord = true;
  private bool _showTranslation = true;
  private bool _showRomanization;
  private FormattedText? _mainText;
  private FormattedText? _mutedMainText;
  private FormattedText? _backgroundPrefixText;
  private FormattedText? _backgroundText;
  private FormattedText? _mutedBackgroundText;
  private FormattedText? _suffixText;
  private double[] _wordStarts = [];
  private double[] _wordWidths = [];
  private double[] _backgroundWordStarts = [];
  private double[] _backgroundWordWidths = [];
  private readonly LinearGradientBrush _mainSweepMask = CreateSweepMask();
  private readonly LinearGradientBrush _backgroundSweepMask = CreateSweepMask();

  public LyricLineMessage? Line
  {
    get => _line;
    set
    {
      if (ReferenceEquals(_line, value)) return;
      _line = value;
      InvalidateMetrics();
    }
  }

  public LyricLineMessage? BackgroundLine
  {
    get => _backgroundLine;
    set
    {
      if (ReferenceEquals(_backgroundLine, value)) return;
      _backgroundLine = value;
      InvalidateMetrics();
    }
  }

  public double PositionMs
  {
    get => _positionMs;
    set
    {
      if (Math.Abs(_positionMs - value) < 0.5) return;
      _positionMs = value;
      InvalidateVisual();
    }
  }

  public double LineFontSize
  {
    get => _lineFontSize;
    set
    {
      if (Math.Abs(_lineFontSize - value) < 0.01) return;
      _lineFontSize = value;
      InvalidateMetrics();
    }
  }

  public IBrush Foreground
  {
    get => _foreground;
    set
    {
      if (ReferenceEquals(_foreground, value)) return;
      _foreground = value;
      InvalidateMetrics();
    }
  }

  public TextAlignment TextAlignment
  {
    get => _textAlignment;
    set
    {
      if (_textAlignment == value) return;
      _textAlignment = value;
      InvalidateVisual();
    }
  }

  public bool WordByWord
  {
    get => _wordByWord;
    set
    {
      if (_wordByWord == value) return;
      _wordByWord = value;
      InvalidateVisual();
    }
  }

  public bool ShowTranslation
  {
    get => _showTranslation;
    set
    {
      if (_showTranslation == value) return;
      _showTranslation = value;
      InvalidateMetrics();
    }
  }

  public bool ShowRomanization
  {
    get => _showRomanization;
    set
    {
      if (_showRomanization == value) return;
      _showRomanization = value;
      InvalidateMetrics();
    }
  }

  protected override Size MeasureOverride(Size availableSize)
  {
    EnsureMetrics();
    var width = (_mainText?.WidthIncludingTrailingWhitespace ?? 0) +
                (_backgroundPrefixText?.WidthIncludingTrailingWhitespace ?? 0) +
                (_backgroundText?.WidthIncludingTrailingWhitespace ?? 0) +
                (_suffixText?.WidthIncludingTrailingWhitespace ?? 0);
    var height = new[]
    {
      _mainText?.Height ?? 0,
      _backgroundText?.Height ?? 0,
      _suffixText?.Height ?? 0
    }.Max();
    return new Size(Math.Min(width, availableSize.Width), height);
  }

  public override void Render(DrawingContext context)
  {
    base.Render(context);
    EnsureMetrics();
    if (_mainText == null) return;

    var mainWidth = _mainText.WidthIncludingTrailingWhitespace;
    var prefixWidth = _backgroundPrefixText?.WidthIncludingTrailingWhitespace ?? 0;
    var backgroundWidth = _backgroundText?.WidthIncludingTrailingWhitespace ?? 0;
    var suffixWidth = _suffixText?.WidthIncludingTrailingWhitespace ?? 0;
    var originX = GetOrigin(mainWidth + prefixWidth + backgroundWidth + suffixWidth);
    var mainOrigin = new Point(originX, 0);

    DrawSweptText(
        context,
        _mainText,
        _mutedMainText ?? _mainText,
        mainOrigin,
        WordByWord && HasWordTiming(Line),
        ComputeFilledWidth(Line, _wordStarts, _wordWidths),
        _mainSweepMask);

    if (_backgroundText != null)
    {
      var prefixOrigin = new Point(originX + mainWidth, 0);
      if (_backgroundPrefixText != null) context.DrawText(_backgroundPrefixText, prefixOrigin);
      var backgroundOrigin = new Point(prefixOrigin.X + prefixWidth, 0);
      DrawSweptText(
          context,
          _backgroundText,
          _mutedBackgroundText ?? _backgroundText,
          backgroundOrigin,
          WordByWord && HasWordTiming(BackgroundLine),
          ComputeFilledWidth(BackgroundLine, _backgroundWordStarts, _backgroundWordWidths),
          _backgroundSweepMask);
    }

    if (_suffixText != null)
      context.DrawText(_suffixText, new Point(originX + mainWidth + prefixWidth + backgroundWidth, 0));
  }

  private void EnsureMetrics()
  {
    if (_mainText != null) return;
    var line = Line;
    var text = line?.Text ?? string.Empty;
    _mainText = CreateFormatted(text, Foreground, LineFontSize);
    _mutedMainText = CreateFormatted(text, CreateOpacityBrush(Foreground, 0.52), LineFontSize);
    (_wordStarts, _wordWidths) = BuildWordMetrics(line, _mainText, LineFontSize);

    var background = BackgroundLine;
    if (background != null && !string.IsNullOrWhiteSpace(background.Text))
    {
      var backgroundFontSize = Math.Max(9, LineFontSize * 0.76);
      _backgroundPrefixText = CreateFormatted(
          " / ",
          CreateOpacityBrush(Foreground, 0.42),
          backgroundFontSize);
      _backgroundText = CreateFormatted(
          background.Text,
          CreateOpacityBrush(Foreground, 0.76),
          backgroundFontSize);
      _mutedBackgroundText = CreateFormatted(
          background.Text,
          CreateOpacityBrush(Foreground, 0.38),
          backgroundFontSize);
      (_backgroundWordStarts, _backgroundWordWidths) =
          BuildWordMetrics(background, _backgroundText, backgroundFontSize);
    }

    var suffixParts = new List<string>();
    if (ShowTranslation && !string.IsNullOrWhiteSpace(line?.TranslatedLyric))
      suffixParts.Add(line.TranslatedLyric.Trim());
    if (ShowRomanization && !string.IsNullOrWhiteSpace(line?.RomanLyric))
      suffixParts.Add(line.RomanLyric.Trim());
    _suffixText = suffixParts.Count == 0
        ? null
        : CreateFormatted(
            $" / {string.Join(" / ", suffixParts)}",
            CreateOpacityBrush(Foreground, 0.52),
            LineFontSize);
  }

  private (double[] Starts, double[] Widths) BuildWordMetrics(
      LyricLineMessage? line,
      FormattedText text,
      double fontSize)
  {
    var words = line?.Words ?? [];
    var starts = new double[words.Count];
    var widths = new double[words.Count];
    var cursor = 0d;
    for (var index = 0; index < words.Count; index++)
    {
      var layout = CreateFormatted(words[index].Word, Foreground, fontSize);
      starts[index] = cursor;
      widths[index] = layout.WidthIncludingTrailingWhitespace;
      cursor += widths[index];
    }
    if (cursor > 0 && Math.Abs(cursor - text.WidthIncludingTrailingWhitespace) > 1)
    {
      var scale = text.WidthIncludingTrailingWhitespace / cursor;
      cursor = 0;
      for (var index = 0; index < widths.Length; index++)
      {
        starts[index] = cursor;
        widths[index] *= scale;
        cursor += widths[index];
      }
    }
    return (starts, widths);
  }

  private void DrawSweptText(
      DrawingContext context,
      FormattedText text,
      FormattedText mutedText,
      Point origin,
      bool animate,
      double filledWidth,
      LinearGradientBrush mask)
  {
    context.DrawText(mutedText, origin);
    var width = text.WidthIncludingTrailingWhitespace;
    if (!animate || filledWidth >= width)
    {
      context.DrawText(text, origin);
      return;
    }
    if (filledWidth <= 0 || width <= 0) return;

    var feather = Math.Min(SweepFeatherWidth, width / 2);
    mask.GradientStops[0].Offset = Math.Clamp((filledWidth - feather) / width, 0, 1);
    mask.GradientStops[1].Offset = Math.Clamp((filledWidth + feather) / width, 0, 1);
    using (context.PushOpacityMask(mask, new Rect(origin, new Size(width, text.Height))))
      context.DrawText(text, origin);
  }

  private double ComputeFilledWidth(
      LyricLineMessage? line,
      double[] wordStarts,
      double[] wordWidths)
  {
    var words = line?.Words;
    if (words == null || words.Count == 0 || wordStarts.Length != words.Count) return 0;
    var filled = 0d;
    for (var index = 0; index < words.Count; index++)
    {
      var progress = GetProgress(words[index]);
      if (progress <= 0) break;
      filled = wordStarts[index] + wordWidths[index] * progress;
    }
    return filled;
  }

  private double GetProgress(LyricWordMessage word)
  {
    if (PositionMs <= word.StartTime) return 0;
    if (word.EndTime <= word.StartTime || PositionMs >= word.EndTime) return 1;
    return Math.Clamp((PositionMs - word.StartTime) / (word.EndTime - word.StartTime), 0, 1);
  }

  private double GetOrigin(double width) => TextAlignment switch
  {
    TextAlignment.Left or TextAlignment.Start => 0,
    TextAlignment.Right or TextAlignment.End => Math.Max(0, Bounds.Width - width),
    _ => Math.Max(0, (Bounds.Width - width) / 2)
  };

  private static bool HasWordTiming(LyricLineMessage? line) =>
      line?.Words.Any(word => word.EndTime > word.StartTime) == true;

  private static FormattedText CreateFormatted(string text, IBrush brush, double fontSize) => new(
      text,
      System.Globalization.CultureInfo.CurrentUICulture,
      FlowDirection.LeftToRight,
      new Typeface(FontFamily.Default),
      fontSize,
      brush);

  private static IBrush CreateOpacityBrush(IBrush foreground, double opacity) =>
      foreground is ISolidColorBrush solid
          ? new SolidColorBrush(solid.Color, opacity)
          : foreground;

  private static LinearGradientBrush CreateSweepMask() => new()
  {
    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
    GradientStops =
    {
      new GradientStop(Colors.White, 0),
      new GradientStop(Colors.Transparent, 1)
    }
  };

  private void InvalidateMetrics()
  {
    _mainText = null;
    _mutedMainText = null;
    _backgroundPrefixText = null;
    _backgroundText = null;
    _mutedBackgroundText = null;
    _suffixText = null;
    _wordStarts = [];
    _wordWidths = [];
    _backgroundWordStarts = [];
    _backgroundWordWidths = [];
    InvalidateMeasure();
    InvalidateVisual();
  }
}
