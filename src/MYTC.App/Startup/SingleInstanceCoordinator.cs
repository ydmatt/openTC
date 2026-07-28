using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MYTC.App.Startup;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _serverTask;
    private bool _disposed;

    public SingleInstanceCoordinator(string instanceScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceScope);
        var identity = string.Join(
            "|",
            Environment.UserDomainName,
            Environment.UserName,
            Path.GetFullPath(instanceScope).ToUpperInvariant());
        var suffix = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        _pipeName = $"MYTC.SingleInstance.{suffix}";
        _mutex = new Mutex(
            initiallyOwned: true,
            $@"Local\MYTC.SingleInstance.{suffix}",
            out var createdNew);
        IsPrimary = createdNew;
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }
    }

    public bool IsPrimary { get; }

    public event Action<LaunchRequest>? RequestReceived;

    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimary)
        {
            throw new InvalidOperationException(
                "只有主实例可以监听启动请求。");
        }

        _serverTask ??= ListenLoopAsync(_cancellation.Token);
    }

    public async Task<bool> SendAsync(
        LaunchRequest request,
        TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsPrimary)
        {
            throw new InvalidOperationException(
                "主实例不应向自身转发启动请求。");
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        while (!timeoutSource.IsCancellationRequested)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous |
                    PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(500, timeoutSource.Token);
                await using var writer = new StreamWriter(
                    client,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    leaveOpen: true)
                {
                    AutoFlush = true,
                };
                using var reader = new StreamReader(
                    client,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                var primaryProcessLine = await reader.ReadLineAsync(
                    timeoutSource.Token);
                if (int.TryParse(
                        primaryProcessLine,
                        out var primaryProcessId))
                {
                    _ = AllowSetForegroundWindow(primaryProcessId);
                }

                await writer.WriteLineAsync(
                    JsonSerializer.Serialize(request));
                var response = await reader.ReadLineAsync(
                    timeoutSource.Token);
                return StringComparer.Ordinal.Equals(response, "OK");
            }
            catch (Exception exception) when (
                exception is TimeoutException or
                IOException or
                OperationCanceledException)
            {
                if (timeoutSource.IsCancellationRequested)
                {
                    break;
                }

                await Task.Delay(100);
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        if (IsPrimary)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ownership was already released during process shutdown.
            }
        }

        _mutex?.Dispose();
        _cancellation.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous |
                    PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(
                    server,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                await using var writer = new StreamWriter(
                    server,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    leaveOpen: true)
                {
                    AutoFlush = true,
                };
                await writer.WriteLineAsync(
                    Environment.ProcessId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                var line = await reader.ReadLineAsync(cancellationToken);
                var request = string.IsNullOrWhiteSpace(line)
                    ? null
                    : JsonSerializer.Deserialize<LaunchRequest>(line);
                if (request is not null)
                {
                    RequestReceived?.Invoke(request);
                }

                await writer.WriteLineAsync("OK");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(
        int processId);
}
