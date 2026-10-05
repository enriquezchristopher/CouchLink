using System.Diagnostics;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using static CouchLink.App.Input.NativeMethods;

namespace CouchLink.App.Input;

/// <summary>Ticks the mapper every ~1 ms and sends state on change or every 8 ms.</summary>
internal sealed class ClientInputLoop : IDisposable
{
    private readonly InputMapper _mapper;
    private readonly InputSender _sender;
    private readonly Thread _thread;
    private volatile bool _running = true;

    public ClientInputLoop(InputMapper mapper, InputSender sender)
    {
        _mapper = mapper;
        _sender = sender;
        _thread = new Thread(Run) { IsBackground = true, Name = "CouchLink input", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Last state sent; read by the UI for display.</summary>
    public PadState LastSent { get; private set; } = PadState.Neutral;

    private void Run()
    {
        timeBeginPeriod(1); // default Windows timer is ~15.6 ms; we need ~1 ms sleeps
        try
        {
            var policy = new SendPolicy();
            var clock = Stopwatch.StartNew();
            var lastTick = TimeSpan.Zero;
            while (_running)
            {
                var now = clock.Elapsed;
                var state = _mapper.Tick((now - lastTick).TotalSeconds);
                lastTick = now;
                if (policy.ShouldSend(state, now))
                {
                    _sender.Send(state);
                    LastSent = state;
                }
                Thread.Sleep(1);
            }
            _sender.Send(PadState.Neutral);
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        _sender.Dispose();
    }
}
