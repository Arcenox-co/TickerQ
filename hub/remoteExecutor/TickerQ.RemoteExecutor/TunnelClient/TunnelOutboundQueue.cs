using System.Threading.Channels;
using Google.Protobuf;
using TickerQ.RemoteExecutor.Tunnel;

namespace TickerQ.RemoteExecutor.TunnelClient;

/// <summary>
/// One bounded best-effort stream per connection. Producers never wait for a slow
/// gRPC writer. Limits include the in-flight frame; disconnect cancels and drains it.
/// </summary>
internal sealed class TunnelOutboundQueue : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Channel<(SchedulerEvent Message, int Bytes)> _channel;
    private readonly CancellationTokenSource _stop;
    private readonly Func<SchedulerEvent, CancellationToken, Task> _write;
    private readonly Action _onDrop;
    private readonly int _maxFrames;
    private readonly long _maxBytes;
    private readonly Task _pump;
    private int _frames;
    private long _bytes;
    private bool _completed;

    internal TunnelOutboundQueue(Func<SchedulerEvent, CancellationToken, Task> write,
        Action onDrop, CancellationToken stoppingToken, int maxFrames = 256, long maxBytes = 2 * 1024 * 1024)
    {
        if (maxFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _write = write;
        _onDrop = onDrop;
        _maxFrames = maxFrames;
        _maxBytes = maxBytes;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _channel = Channel.CreateBounded<(SchedulerEvent, int)>(new BoundedChannelOptions(maxFrames)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _pump = PumpAsync();
    }

    internal int PendingFrames { get { lock (_gate) return _frames; } }
    internal long PendingBytes { get { lock (_gate) return _bytes; } }

    public bool TryEnqueue(SchedulerEvent message)
    {
        // Protobuf bytes are a conservative proxy for payload size after the sender's
        // string caps. Include object overhead so even empty events consume budget.
        var bytes = checked(2 * message.CalculateSize() + 256);
        lock (_gate)
        {
            if (_completed || _stop.IsCancellationRequested || _frames >= _maxFrames || bytes > _maxBytes - _bytes)
            {
                _onDrop();
                return false;
            }
            _frames++;
            _bytes += bytes;
            if (_channel.Writer.TryWrite((message, bytes))) return true;
            _frames--;
            _bytes -= bytes;
            _onDrop();
            return false;
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try { await _write(item.Message, _stop.Token).ConfigureAwait(false); }
                catch (Exception) { _onDrop(); throw; }
                finally { Release(item.Bytes); }
            }
        }
        catch (Exception)
        {
            // A failed/cancelled stream cannot send the remaining frames. Reconnect
            // creates a fresh queue and the dashboard refreshes its durable state.
        }
        finally
        {
            lock (_gate)
            {
                _completed = true;
                _channel.Writer.TryComplete();
                while (_channel.Reader.TryRead(out var item))
                {
                    _frames--;
                    _bytes -= item.Bytes;
                    _onDrop();
                }
            }
        }
    }

    private void Release(int bytes)
    {
        lock (_gate) { _frames--; _bytes -= bytes; }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { _completed = true; _channel.Writer.TryComplete(); }
        await _stop.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _stop.Dispose();
    }
}
