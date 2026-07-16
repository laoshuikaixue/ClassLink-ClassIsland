using System.Text.Json.Serialization;

namespace ClassLink.Protocol;

public sealed class StateMessage
{
  public int ProtocolVersion { get; init; }
  public string InstanceId { get; init; } = string.Empty;
  public long StartedAtUnixMs { get; init; }
  public string AppId { get; init; } = string.Empty;
  public string AppName { get; init; } = "播放器";
  public string ConnectorName { get; init; } = string.Empty;
  public string ConnectorVersion { get; init; } = string.Empty;
  public string AppVersion { get; init; } = string.Empty;
  public long StateRevision { get; init; }
  public long TrackRevision { get; init; }
  public string? TrackKey { get; init; }
  public TrackMessage? Track { get; init; }
  public LyricsMessage Lyrics { get; init; } = new();
  public PlaybackMessage Playback { get; init; } = new();
  public CoverMetadataMessage? Cover { get; init; }
}

public sealed class AnchorMessage
{
  public int ProtocolVersion { get; init; }
  public string InstanceId { get; init; } = string.Empty;
  public long StartedAtUnixMs { get; init; }
  public long Sequence { get; init; }
  public string? TrackKey { get; init; }
  public PlaybackMessage Playback { get; init; } = new();
}

public sealed class HeartbeatMessage
{
  public int ProtocolVersion { get; init; }
  public string InstanceId { get; init; } = string.Empty;
  public long StartedAtUnixMs { get; init; }
  public string? TrackKey { get; init; }
}

public sealed class TrackMessage
{
  public string Id { get; init; } = string.Empty;
  public string Source { get; init; } = string.Empty;
  public string Title { get; init; } = string.Empty;
  public IReadOnlyList<ArtistMessage> Artists { get; init; } = [];
  public AlbumMessage? Album { get; init; }
  public long Duration { get; init; }
}

public sealed class ArtistMessage
{
  public string Name { get; init; } = string.Empty;
}

public sealed class AlbumMessage
{
  public string Name { get; init; } = string.Empty;
}

public sealed class LyricsMessage
{
  public string Status { get; init; } = "none";
  public long Revision { get; init; }
  public string? Source { get; init; }
  public string? Format { get; init; }
  public string? Platform { get; init; }
  public IReadOnlyList<LyricLineMessage> Lines { get; init; } = [];
}

public sealed class LyricLineMessage
{
  public IReadOnlyList<LyricWordMessage> Words { get; init; } = [];
  public string TranslatedLyric { get; init; } = string.Empty;
  public string RomanLyric { get; init; } = string.Empty;
  public double StartTime { get; init; }
  public double EndTime { get; init; }
  public bool IsBG { get; init; }
  public bool IsDuet { get; init; }

  [JsonIgnore]
  public string Text => string.Concat(Words.Select(word => word.Word));
}

public sealed class LyricWordMessage
{
  public double StartTime { get; init; }
  public double EndTime { get; init; }
  public string Word { get; init; } = string.Empty;
  public string? RomanWord { get; init; }
  public IReadOnlyList<LyricRubyMessage>? Ruby { get; init; }
}

public sealed class LyricRubyMessage
{
  public double StartTime { get; init; }
  public double EndTime { get; init; }
  public string Word { get; init; } = string.Empty;
}

public sealed class PlaybackMessage
{
  public double PositionMs { get; init; }
  public string State { get; init; } = "paused";
  public double Speed { get; init; } = 1;
  public double LyricOffsetMs { get; init; }
  public long SentAtUnixMs { get; init; }
}

public sealed class CoverMetadataMessage
{
  public long Revision { get; init; }
  public string Hash { get; init; } = string.Empty;
  public string MimeType { get; init; } = "image/jpeg";
}

public sealed class ClassLinkResponseMessage
{
  public string Message { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? CoverRequired { get; init; }
}
