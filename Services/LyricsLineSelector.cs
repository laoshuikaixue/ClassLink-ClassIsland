using ClassLink.Protocol;

namespace ClassLink.Services;

public sealed record LyricsLineSelection(
    int LineIndex,
    LyricLineMessage Line,
    bool IsDuetSide,
    LyricLineMessage? BackgroundLine = null);

public static class LyricsLineSelector
{
  private const int MaximumVisibleRows = 2;

  public static IReadOnlyList<LyricsLineSelection> SelectActive(LyricsMessage lyrics, double positionMs)
  {
    var active = lyrics.Lines
        .Select((line, index) => new IndexedLine(index, line))
        .Where(item => IsActive(item.Line, positionMs))
        .ToArray();
    if (active.Length == 0)
    {
      var fallback = FindFallback(lyrics.Lines, positionMs);
      return fallback == null
          ? []
          : [new LyricsLineSelection(fallback.Index, fallback.Line, fallback.Line.IsDuet)];
    }

    if (!string.Equals(lyrics.Format, "ttml", StringComparison.OrdinalIgnoreCase))
    {
      return active
          .Take(MaximumVisibleRows)
          .Select(item => new LyricsLineSelection(item.Index, item.Line, item.Line.IsDuet))
          .ToArray();
    }

    var foreground = active.Where(item => !item.Line.IsBG).ToArray();
    if (foreground.Length == 0)
    {
      return active
          .Take(MaximumVisibleRows)
          .Select(item => new LyricsLineSelection(item.Index, item.Line, item.Line.IsDuet))
          .ToArray();
    }

    var backgrounds = active.Where(item => item.Line.IsBG).ToArray();
    var usedBackgrounds = new HashSet<int>();
    var result = new List<LyricsLineSelection>();
    foreach (var main in foreground.Take(MaximumVisibleRows))
    {
      var background = backgrounds
          .Where(item => !usedBackgrounds.Contains(item.Index) && Overlaps(main.Line, item.Line))
          .OrderByDescending(item => Overlap(main.Line, item.Line))
          .ThenBy(item => item.Index)
          .FirstOrDefault();
      if (background != null) usedBackgrounds.Add(background.Index);
      result.Add(new LyricsLineSelection(
          main.Index,
          main.Line,
          main.Line.IsDuet,
          background?.Line));
    }
    return result.OrderBy(item => item.LineIndex).ToArray();
  }

  private static IndexedLine? FindFallback(IReadOnlyList<LyricLineMessage> lines, double positionMs)
  {
    IndexedLine? latest = null;
    IndexedLine? foreground = null;
    for (var index = 0; index < lines.Count; index++)
    {
      if (lines[index].StartTime > positionMs) break;
      latest = new IndexedLine(index, lines[index]);
      if (!lines[index].IsBG) foreground = latest;
    }
    return foreground ?? latest;
  }

  private static bool IsActive(LyricLineMessage line, double positionMs) =>
      line.StartTime <= positionMs && line.EndTime > line.StartTime && positionMs < line.EndTime;

  private static bool Overlaps(LyricLineMessage first, LyricLineMessage second) =>
      first.EndTime > second.StartTime && second.EndTime > first.StartTime;

  private static double Overlap(LyricLineMessage first, LyricLineMessage second) =>
      Math.Max(0, Math.Min(first.EndTime, second.EndTime) - Math.Max(first.StartTime, second.StartTime));

  private sealed record IndexedLine(int Index, LyricLineMessage Line);
}
