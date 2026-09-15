namespace RF4Catches.HostedServices;


public sealed class HotkeyOptions
{
    public int StartVirtualKey { get; init; } = 119; // F8
    public int EndVirtualKey { get; init; } = 120; // F9
}

public sealed class HotkeyListenerService(
    Services.SessionService sessionService,
    Microsoft.Extensions.Options.IOptions<HotkeyOptions> options,
    ILogger<HotkeyListenerService> logger) : BackgroundService
{
    private readonly HotkeyOptions _options = options.Value;
    private bool _startWasDown;
    private bool _endWasDown;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var modifiersDown = IsKeyDown(0x11) && IsKeyDown(0x12); // Ctrl + Alt
            var startDown = modifiersDown && IsKeyDown(_options.StartVirtualKey);
            var endDown = modifiersDown && IsKeyDown(_options.EndVirtualKey);
            if (startDown && !_startWasDown) await RunActionAsync(sessionService.StartAsync, "start", stoppingToken);
            if (endDown && !_endWasDown) await RunActionAsync(sessionService.CaptureAndProcessAsync, "capture", stoppingToken);
            _startWasDown = startDown;
            _endWasDown = endDown;
            await Task.Delay(50, stoppingToken);
        }
    }

    private async Task RunActionAsync(
        Func<CancellationToken, Task<Models.FishingSession>> action,
        string actionName,
        CancellationToken cancellationToken)
    {
        try { await action(cancellationToken); }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Unable to {Action} fishing session from hotkey.", actionName);
        }
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
