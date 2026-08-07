using VectorPilot.Serial;

namespace VectorPilot.Engine;

/// <summary>
/// Streaming state for the ok-wait G-code sender.
/// </summary>
public enum StreamPhase
{
    Idle,
    Streaming,
    Paused,
    Completed,
    Cancelled,
    Failed
}

/// <summary>Event payload for stream lifecycle.</summary>
public sealed record StreamProgress(int CurrentLine, int TotalLines, StreamPhase Phase);

/// <summary>
/// Ok-wait G-code streamer over IMachineTransport (mirrors ShopPilot GCodeStreamer semantics):
/// one line at a time, waits for "ok" (with timeout), supports hold/resume/cancel, throttled progress.
/// </summary>
public sealed class GCodeStreamer : IAsyncDisposable
{
    private readonly IMachineTransport _transport;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private bool _paused;

    public event Action<StreamProgress>? ProgressChanged;
    public StreamPhase Phase { get; private set; } = StreamPhase.Idle;
    public int CurrentLine { get; private set; }
    public int TotalLines { get; private set; }
    public TimeSpan LineTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public IMachineTransport TransportBinding => _transport;

    public GCodeStreamer(IMachineTransport transport) => _transport = transport;

    public async Task StartAsync(IEnumerable<string> lines, CancellationToken ct = default)
    {
        var all = lines.Where(l => !string.IsNullOrWhiteSpace(l.Trim()) && !l.TrimStart().StartsWith('(') && !l.TrimStart().StartsWith(';')).ToList();
        if (all.Count == 0) return;

        lock (_lock)
        {
            if (Phase is StreamPhase.Streaming or StreamPhase.Paused) return;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _paused = false;
        }
        TotalLines = all.Count;
        CurrentLine = 0;
        Phase = StreamPhase.Streaming;
        Emit();

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<TransportEvent> handler = ev =>
        {
            if (ev.Type == TransportEventType.Ok) tcs.TrySetResult(ev.Payload);
            else if (ev.Type == TransportEventType.Alarm) tcs.TrySetException(new IOException($"Alarm during stream: {ev.Payload}"));
            else if (ev.Type is TransportEventType.Error or TransportEventType.ConnectionError) tcs.TrySetException(new IOException(ev.Payload));
        };
        _transport.EventReceived += handler;

        try
        {
            var lastProgress = DateTime.UtcNow;
            foreach (var line in all)
            {
                _cts.Token.ThrowIfCancellationRequested();
                while (_paused)
                {
                    await Task.Delay(50, _cts.Token).ConfigureAwait(false);
                }

                await _transport.WriteLineAsync(line, _cts.Token).ConfigureAwait(false);
                CurrentLine++;

                var now = DateTime.UtcNow;
                if (now - lastProgress > TimeSpan.FromMilliseconds(100))
                {
                    lastProgress = now;
                    Emit();
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                cts.CancelAfter(LineTimeout);
                try
                {
                    await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                {
                    throw new TimeoutException($"No 'ok' from controller after {LineTimeout.TotalSeconds}s (line {CurrentLine}). Stream halted.");
                }
                finally
                {
                    tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            Phase = StreamPhase.Completed;
        }
        catch (OperationCanceledException)
        {
            Phase = StreamPhase.Cancelled;
        }
        catch (Exception)
        {
            Phase = StreamPhase.Failed;
            throw;
        }
        finally
        {
            _transport.EventReceived -= handler;
            Emit();
        }
    }

    public void Pause()
    {
        lock (_lock) _paused = true;
        Phase = StreamPhase.Paused;
        Emit();
    }

    public void Resume()
    {
        lock (_lock) _paused = false;
        Phase = StreamPhase.Streaming;
        Emit();
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch { /* already cancelled */ }
        Phase = StreamPhase.Cancelled;
        Emit();
    }

    private void Emit() => ProgressChanged?.Invoke(new StreamProgress(CurrentLine, TotalLines, Phase));

    public async ValueTask DisposeAsync()
    {
        Cancel();
        await Task.CompletedTask;
    }
}
