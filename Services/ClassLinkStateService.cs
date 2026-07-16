using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClassLink.Protocol;

namespace ClassLink.Services;

public sealed record ClassLinkRuntimeSnapshot(
    StateMessage State,
    PlaybackMessage Playback,
    long PlaybackReceivedAtTick);

public sealed class ClassLinkStateService
{
  private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(22);
  private readonly object _gate = new();
  private string? _activeInstanceId;
  private long _activeStartedAt;
  private long _lastStateRevision = -1;
  private long _lastAnchorSequence = -1;
  private long _lastCoverRevision = -1;
  private string? _coverHash;
  private string? _coverMimeType;
  private DateTime _lastSeenUtc = DateTime.MinValue;
  private bool _reportedConnected;
  private ClassLinkRuntimeSnapshot? _current;
  private Bitmap? _cover;
  private string _listenerStatus = "尚未启动";

  public event EventHandler? Changed;

  public ClassLinkRuntimeSnapshot? Current
  {
    get
    {
      lock (_gate) return _current;
    }
  }

  public Bitmap? Cover
  {
    get
    {
      lock (_gate) return _cover;
    }
  }

  public string ListenerStatus
  {
    get
    {
      lock (_gate) return _listenerStatus;
    }
  }

  public bool IsConnected
  {
    get
    {
      lock (_gate) return IsConnectedCore(DateTime.UtcNow);
    }
  }

  public DateTime LastSeenUtc
  {
    get
    {
      lock (_gate) return _lastSeenUtc;
    }
  }

  public bool ApplyState(StateMessage message)
  {
    if (message.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(message.InstanceId)) return false;
    Bitmap? coverToDispose = null;
    lock (_gate)
    {
      if (!TryActivateInstance(message.InstanceId, message.StartedAtUnixMs, out var replaced))
        return false;
      if (message.StateRevision < _lastStateRevision) return false;
      if (message.StateRevision == _lastStateRevision)
      {
        if (_current == null) return false;
        _lastSeenUtc = DateTime.UtcNow;
        _reportedConnected = true;
      }
      else
      {
        var previousTrackKey = _current?.State.TrackKey;
        if (replaced)
        {
          coverToDispose = ClearCoverCore();
        }
        _lastStateRevision = message.StateRevision;
        _lastSeenUtc = DateTime.UtcNow;
        _reportedConnected = true;
        _current = new ClassLinkRuntimeSnapshot(message, message.Playback, Environment.TickCount64);
        if (!string.Equals(previousTrackKey, message.TrackKey, StringComparison.Ordinal) ||
            message.Cover == null)
        {
          coverToDispose ??= ClearCoverCore();
        }
      }
    }

    RaiseChanged(coverToDispose);
    return true;
  }

  public bool NeedsCurrentCover()
  {
    lock (_gate)
    {
      var metadata = _current?.State.Cover;
      return metadata != null &&
             (_cover == null ||
              metadata.Revision != _lastCoverRevision ||
              !string.Equals(metadata.Hash, _coverHash, StringComparison.OrdinalIgnoreCase) ||
              !string.Equals(metadata.MimeType, _coverMimeType, StringComparison.OrdinalIgnoreCase));
    }
  }

  public bool ApplyAnchor(AnchorMessage message)
  {
    lock (_gate)
    {
      if (!IsActiveInstance(message.InstanceId, message.StartedAtUnixMs)) return false;
      if (_current == null) return false;
      if (!string.Equals(message.TrackKey, _current.State.TrackKey, StringComparison.Ordinal)) return false;
      if (message.Sequence < 0 || message.Sequence < _lastAnchorSequence) return false;
      if (message.Sequence > _lastAnchorSequence)
      {
        _lastAnchorSequence = message.Sequence;
        _current = new ClassLinkRuntimeSnapshot(_current.State, message.Playback, Environment.TickCount64);
      }
      _lastSeenUtc = DateTime.UtcNow;
      _reportedConnected = true;
    }

    RaiseChanged();
    return true;
  }

  public async Task<bool> ApplyCoverAsync(
      string instanceId,
      long startedAtUnixMs,
      long revision,
      string trackKey,
      string hash,
      string mimeType,
      byte[] data)
  {
    var needsDecode = true;
    lock (_gate)
    {
      if (!IsActiveInstance(instanceId, startedAtUnixMs) ||
          _current == null ||
          !string.Equals(trackKey, _current.State.TrackKey, StringComparison.Ordinal) ||
          revision < 0 ||
          revision < _lastCoverRevision ||
          !string.Equals(hash, _current.State.Cover?.Hash, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(mimeType, _current.State.Cover?.MimeType, StringComparison.OrdinalIgnoreCase))
        return false;
      if (revision == _lastCoverRevision &&
          _cover != null &&
          string.Equals(hash, _coverHash, StringComparison.OrdinalIgnoreCase) &&
          string.Equals(mimeType, _coverMimeType, StringComparison.OrdinalIgnoreCase))
      {
        _lastSeenUtc = DateTime.UtcNow;
        _reportedConnected = true;
        needsDecode = false;
      }
    }

    if (!needsDecode)
    {
      RaiseChanged();
      return true;
    }

    Bitmap? bitmap;
    try
    {
      bitmap = await Task.Run(() =>
      {
        using var stream = new MemoryStream(data, writable: false);
        return new Bitmap(stream);
      });
    }
    catch
    {
      return false;
    }

    Bitmap? oldCover = null;
    lock (_gate)
    {
      if (!IsActiveInstance(instanceId, startedAtUnixMs) ||
          _current == null ||
          !string.Equals(trackKey, _current.State.TrackKey, StringComparison.Ordinal) ||
          revision < _lastCoverRevision ||
          !string.Equals(hash, _current.State.Cover?.Hash, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(mimeType, _current.State.Cover?.MimeType, StringComparison.OrdinalIgnoreCase))
      {
        bitmap.Dispose();
        return false;
      }

      if (revision == _lastCoverRevision &&
          _cover != null &&
          string.Equals(hash, _coverHash, StringComparison.OrdinalIgnoreCase) &&
          string.Equals(mimeType, _coverMimeType, StringComparison.OrdinalIgnoreCase))
      {
        bitmap.Dispose();
        _lastSeenUtc = DateTime.UtcNow;
        _reportedConnected = true;
      }
      else
      {
        _lastCoverRevision = revision;
        _lastSeenUtc = DateTime.UtcNow;
        _reportedConnected = true;
        oldCover = _cover;
        _cover = bitmap;
        _coverHash = hash;
        _coverMimeType = mimeType;
      }
    }

    RaiseChanged(oldCover);
    return true;
  }

  public bool Touch(HeartbeatMessage message)
  {
    lock (_gate)
    {
      if (!IsActiveInstance(message.InstanceId, message.StartedAtUnixMs)) return false;
      if (_current != null &&
          !string.Equals(message.TrackKey, _current.State.TrackKey, StringComparison.Ordinal)) return false;
      _lastSeenUtc = DateTime.UtcNow;
      _reportedConnected = true;
    }

    RaiseChanged();
    return true;
  }

  public void SetListenerStatus(string status)
  {
    lock (_gate) _listenerStatus = status;
    RaiseChanged();
  }

  public void MarkDisconnectedIfStale()
  {
    var changed = false;
    lock (_gate)
    {
      if (_reportedConnected && !IsConnectedCore(DateTime.UtcNow))
      {
        _reportedConnected = false;
        changed = true;
      }
    }
    if (changed) RaiseChanged();
  }

  public double GetCurrentPositionMs()
  {
    lock (_gate)
    {
      if (_current == null) return 0;
      var playback = _current.Playback;
      if (!string.Equals(playback.State, "playing", StringComparison.OrdinalIgnoreCase))
        return Math.Max(0, playback.PositionMs);
      var elapsed = Math.Max(0, Environment.TickCount64 - _current.PlaybackReceivedAtTick);
      return Math.Max(0, playback.PositionMs + elapsed * Math.Max(0.1, playback.Speed));
    }
  }

  public static bool TokenEquals(string expected, string actual)
  {
    var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
    var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
    return expectedBytes.Length == actualBytes.Length &&
           CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
  }

  private bool TryActivateInstance(string instanceId, long startedAtUnixMs, out bool replaced)
  {
    replaced = false;
    if (_activeInstanceId == null)
    {
      _activeInstanceId = instanceId;
      _activeStartedAt = startedAtUnixMs;
      return true;
    }

    if (string.Equals(_activeInstanceId, instanceId, StringComparison.Ordinal) &&
        _activeStartedAt == startedAtUnixMs) return true;
    if (IsConnectedCore(DateTime.UtcNow) && startedAtUnixMs <= _activeStartedAt) return false;

    replaced = true;
    _activeInstanceId = instanceId;
    _activeStartedAt = startedAtUnixMs;
    _lastStateRevision = -1;
    _lastAnchorSequence = -1;
    _lastCoverRevision = -1;
    _current = null;
    return true;
  }

  private bool IsActiveInstance(string instanceId, long startedAtUnixMs) =>
      string.Equals(_activeInstanceId, instanceId, StringComparison.Ordinal) &&
      _activeStartedAt == startedAtUnixMs;

  private bool IsConnectedCore(DateTime now) =>
      _lastSeenUtc != DateTime.MinValue && now - _lastSeenUtc <= ConnectionTimeout;

  private Bitmap? ClearCoverCore()
  {
    var cover = _cover;
    _cover = null;
    _lastCoverRevision = -1;
    _coverHash = null;
    _coverMimeType = null;
    return cover;
  }

  private void RaiseChanged(Bitmap? disposeAfterUpdate = null)
  {
    Dispatcher.UIThread.Post(() =>
    {
      Changed?.Invoke(this, EventArgs.Empty);
      if (disposeAfterUpdate != null) _ = DisposeLaterAsync(disposeAfterUpdate);
    });
  }

  private static async Task DisposeLaterAsync(Bitmap bitmap)
  {
    await Task.Delay(300);
    Dispatcher.UIThread.Post(bitmap.Dispose);
  }
}
