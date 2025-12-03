using System;
using System.Timers;

namespace VenuePlus.Services;

public sealed class AutoPurgeService : IDisposable
{
    private readonly VipService _vipService;
    private readonly System.Timers.Timer _timer;
    private bool _disposed;

    public AutoPurgeService(VipService vipService, TimeSpan? interval = null)
    {
        _vipService = vipService;
        _timer = new System.Timers.Timer((interval ?? TimeSpan.FromHours(6)).TotalMilliseconds);
        _timer.Elapsed += OnElapsed;
        _timer.AutoReset = true;
        _timer.Enabled = true;
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        _vipService.PurgeExpired();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Elapsed -= OnElapsed;
        _timer.Dispose();
    }
}
