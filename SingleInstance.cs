using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Nimbus;

internal sealed class SingleInstance : IDisposable
{
    private const int ConnectTimeoutMs = 5000;

    private static readonly string UserKey = WindowsIdentity.GetCurrent().User!.Value;
    private static readonly string PipeName = $"Nimbus-{UserKey}";

    private readonly Mutex _mutex;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\Nimbus-{UserKey}", out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    // Her satır bir dosya yolu; boş satır "çalışan örnek öne çıksın" demektir.
    public static bool TrySend(IReadOnlyList<string> paths)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(ConnectTimeoutMs);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            foreach (var path in paths.DefaultIfEmpty(""))
                writer.WriteLine(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task ListenAsync(Action<string> onMessage, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(server, Encoding.UTF8);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                    onMessage(line);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // Karşı taraf yarıda koptu; dinlemeye devam.
            }
        }
    }

    public void Dispose() => _mutex.Dispose();
}
