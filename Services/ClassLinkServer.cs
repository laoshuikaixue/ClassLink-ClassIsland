using System.Net;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ClassLink.Models;
using ClassLink.Protocol;

namespace ClassLink.Services;

public sealed class ClassLinkServer : BackgroundService
{
  private sealed record StateResult(bool Accepted, bool CoverRequired);

  private const int MaxStateBytes = 4 * 1024 * 1024;
  private const int MaxCoverBytes = 2 * 1024 * 1024;
  private static readonly HashSet<string> SupportedCoverTypes =
      ["image/jpeg", "image/png", "image/webp"];
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  private readonly ClassLinkSettings _settings;
  private readonly ClassLinkStateService _state;
  private readonly ILogger<ClassLinkServer> _logger;
  private readonly object _listenerGate = new();
  private readonly SemaphoreSlim _restartSignal = new(0, 1);
  private HttpListener? _listener;

  public ClassLinkServer(
      ClassLinkSettings settings,
      ClassLinkStateService state,
      ILogger<ClassLinkServer> logger)
  {
    _settings = settings;
    _state = state;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _settings.PropertyChanged += SettingsOnPropertyChanged;
    var monitorTask = MonitorConnectionAsync(stoppingToken);
    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        while (_restartSignal.Wait(0)) { }
        var port = _settings.Port;
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        SetCurrentListener(listener);
        _state.SetListenerStatus($"正在启动 127.0.0.1:{port}");

        try
        {
          listener.Start();
        }
        catch (Exception ex)
        {
          ClearCurrentListener(listener);
          listener.Close();
          _state.SetListenerStatus(FormatListenerFailure(port, ex));
          _logger.LogError(ex, "ClassLink 启动监听失败，端口 {Port}", port);
          try
          {
            await _restartSignal.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
          }
          catch (OperationCanceledException)
          {
            break;
          }
          continue;
        }

        if (_settings.Port != port || _restartSignal.Wait(0))
        {
          listener.Stop();
          ClearCurrentListener(listener);
          continue;
        }

        _state.SetListenerStatus($"正在监听 127.0.0.1:{port}");
        _logger.LogInformation("ClassLink 正在监听 127.0.0.1:{Port}", port);
        await ListenAsync(listener, stoppingToken);
        ClearCurrentListener(listener);

        if (!stoppingToken.IsCancellationRequested && _settings.Port == port)
        {
          _state.SetListenerStatus("监听意外中断，正在重试…");
          try
          {
            await Task.Delay(1000, stoppingToken);
          }
          catch (OperationCanceledException)
          {
            break;
          }
        }
      }
    }
    finally
    {
      _settings.PropertyChanged -= SettingsOnPropertyChanged;
      StopCurrentListener();
      await monitorTask;
      _state.SetListenerStatus("监听已停止");
    }
  }

  private async Task ListenAsync(HttpListener listener, CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested && listener.IsListening)
    {
      HttpListenerContext context;
      try
      {
        context = await listener.GetContextAsync().WaitAsync(stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (HttpListenerException) when (!listener.IsListening)
      {
        break;
      }
      catch (HttpListenerException ex)
      {
        _logger.LogWarning(ex, "ClassLink 监听请求时发生异常");
        break;
      }

      try
      {
        await HandleRequestAsync(context, stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "ClassLink 请求连接已提前关闭");
      }
    }
  }

  public override Task StopAsync(CancellationToken cancellationToken)
  {
    StopCurrentListener();
    return base.StopAsync(cancellationToken);
  }

  private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName != nameof(ClassLinkSettings.Port)) return;
    _state.SetListenerStatus($"正在切换到 127.0.0.1:{_settings.Port}");
    try
    {
      _restartSignal.Release();
    }
    catch (SemaphoreFullException)
    {
    }
    StopCurrentListener();
  }

  private void SetCurrentListener(HttpListener listener)
  {
    lock (_listenerGate) _listener = listener;
  }

  private void ClearCurrentListener(HttpListener listener)
  {
    lock (_listenerGate)
    {
      if (ReferenceEquals(_listener, listener)) _listener = null;
    }
  }

  private void StopCurrentListener()
  {
    lock (_listenerGate)
    {
      try
      {
        _listener?.Stop();
      }
      catch
      {
      }
    }
  }

  private static string FormatListenerFailure(int port, Exception exception) =>
      exception is HttpListenerException { ErrorCode: 5 or 32 or 10013 or 10048 }
          ? $"监听失败：端口 {port} 不可用，请更换端口"
          : $"监听失败：{exception.Message}";

  private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
  {
    try
    {
      if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
      {
        await WriteResponseAsync(context.Response, HttpStatusCode.MethodNotAllowed, "method_not_allowed");
        return;
      }
      if (!IsAuthorized(context.Request))
      {
        await WriteResponseAsync(context.Response, HttpStatusCode.Unauthorized, "invalid_token");
        return;
      }

      var path = context.Request.Url?.AbsolutePath;
      if (path == "/v1/state")
      {
        var result = await HandleStateAsync(context.Request, cancellationToken);
        await WriteResponseAsync(
            context.Response,
            result.Accepted ? HttpStatusCode.OK : HttpStatusCode.Conflict,
            result.Accepted ? "ok" : "stale_or_invalid",
            result.Accepted ? result.CoverRequired : null);
        return;
      }

      var accepted = path switch
      {
        "/v1/anchor" => await HandleAnchorAsync(context.Request, cancellationToken),
        "/v1/cover" => await HandleCoverAsync(context.Request, cancellationToken),
        "/v1/heartbeat" => await HandleHeartbeatAsync(context.Request, cancellationToken),
        _ => false
      };
      await WriteResponseAsync(
          context.Response,
          accepted ? HttpStatusCode.OK : HttpStatusCode.Conflict,
          accepted ? "ok" : "stale_or_invalid");
    }
    catch (InvalidDataException ex)
    {
      await WriteResponseAsync(context.Response, HttpStatusCode.RequestEntityTooLarge, ex.Message);
    }
    catch (JsonException ex)
    {
      await WriteResponseAsync(context.Response, HttpStatusCode.BadRequest, ex.Message);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "ClassLink 请求处理失败");
      await WriteResponseAsync(context.Response, HttpStatusCode.InternalServerError, "internal_error");
    }
  }

  private async Task<StateResult> HandleStateAsync(
      HttpListenerRequest request,
      CancellationToken cancellationToken)
  {
    if (!HasContentType(request, "application/json")) return new StateResult(false, false);
    var message = await ReadJsonAsync<StateMessage>(request, MaxStateBytes, cancellationToken);
    if (message == null || !_state.ApplyState(message)) return new StateResult(false, false);
    return new StateResult(true, _state.NeedsCurrentCover());
  }

  private async Task<bool> HandleAnchorAsync(HttpListenerRequest request, CancellationToken cancellationToken)
  {
    if (!HasContentType(request, "application/json")) return false;
    var message = await ReadJsonAsync<AnchorMessage>(request, 64 * 1024, cancellationToken);
    return message != null && message.ProtocolVersion == 1 && _state.ApplyAnchor(message);
  }

  private async Task<bool> HandleHeartbeatAsync(HttpListenerRequest request, CancellationToken cancellationToken)
  {
    if (!HasContentType(request, "application/json")) return false;
    var message = await ReadJsonAsync<HeartbeatMessage>(request, 32 * 1024, cancellationToken);
    return message != null && message.ProtocolVersion == 1 && _state.Touch(message);
  }

  private async Task<bool> HandleCoverAsync(HttpListenerRequest request, CancellationToken cancellationToken)
  {
    var mimeType = request.ContentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
    if (mimeType == null || !SupportedCoverTypes.Contains(mimeType)) return false;
    var query = request.QueryString;
    if (!int.TryParse(query["protocolVersion"], out var protocolVersion) || protocolVersion != 1 ||
        !long.TryParse(query["startedAtUnixMs"], out var startedAtUnixMs) ||
        !long.TryParse(query["revision"], out var revision)) return false;
    var instanceId = query["instanceId"];
    var trackKey = query["trackKey"];
    var hash = query["hash"];
    if (string.IsNullOrWhiteSpace(instanceId) ||
        string.IsNullOrWhiteSpace(trackKey) ||
        string.IsNullOrWhiteSpace(hash)) return false;
    var bytes = await ReadBodyAsync(request, MaxCoverBytes, cancellationToken);
    var accepted = await _state.ApplyCoverAsync(
        instanceId,
        startedAtUnixMs,
        revision,
        trackKey,
        hash,
        mimeType,
        bytes);
    if (accepted)
      _logger.LogDebug("ClassLink 已接收封面 {TrackKey}，{Bytes} bytes", trackKey, bytes.Length);
    return accepted;
  }

  private bool IsAuthorized(HttpListenerRequest request)
  {
    var authorization = request.Headers["Authorization"];
    if (string.IsNullOrWhiteSpace(authorization) ||
        !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
    return ClassLinkStateService.TokenEquals(_settings.Token, authorization[7..].Trim());
  }

  private static bool HasContentType(HttpListenerRequest request, string expected) =>
      request.ContentType?.StartsWith(expected, StringComparison.OrdinalIgnoreCase) == true;

  private static async Task<T?> ReadJsonAsync<T>(
      HttpListenerRequest request,
      int maxBytes,
      CancellationToken cancellationToken)
  {
    var bytes = await ReadBodyAsync(request, maxBytes, cancellationToken);
    return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
  }

  private static async Task<byte[]> ReadBodyAsync(
      HttpListenerRequest request,
      int maxBytes,
      CancellationToken cancellationToken)
  {
    if (request.ContentLength64 > maxBytes) throw new InvalidDataException("request_too_large");
    await using var output = new MemoryStream();
    var buffer = new byte[16 * 1024];
    while (true)
    {
      var read = await request.InputStream.ReadAsync(buffer, cancellationToken);
      if (read == 0) break;
      if (output.Length + read > maxBytes) throw new InvalidDataException("request_too_large");
      await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
    }
    return output.ToArray();
  }

  private static async Task WriteResponseAsync(
      HttpListenerResponse response,
      HttpStatusCode status,
      string message,
      bool? coverRequired = null)
  {
    try
    {
      if (!response.OutputStream.CanWrite) return;
      var body = JsonSerializer.SerializeToUtf8Bytes(new ClassLinkResponseMessage
      {
        Message = message,
        CoverRequired = coverRequired
      }, JsonOptions);
      response.StatusCode = (int)status;
      response.ContentType = "application/json; charset=utf-8";
      response.ContentLength64 = body.Length;
      await response.OutputStream.WriteAsync(body);
    }
    catch (HttpListenerException)
    {
    }
    catch (ObjectDisposedException)
    {
    }
    finally
    {
      try
      {
        response.Close();
      }
      catch
      {
      }
    }
  }

  private async Task MonitorConnectionAsync(CancellationToken cancellationToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
    try
    {
      while (await timer.WaitForNextTickAsync(cancellationToken))
        _state.MarkDisconnectedIfStale();
    }
    catch (OperationCanceledException)
    {
    }
  }
}
