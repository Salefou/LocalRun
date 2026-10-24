namespace LrSample.Primitives;

using System.Net;
using System.Text;

/// <summary>
/// A minimal long-running service for hand testing `lrun run`: serves /health and one endpoint on a
/// localhost port, prints a heartbeat, and stops cleanly on Ctrl+C (or when its process is stopped).
/// The port can be overridden with LRSAMPLE_&lt;NAME&gt;_PORT, e.g. LRSAMPLE_ORDERGATEWAY_PORT=6101.
/// </summary>
public static class SampleService
{
    public static async Task<int> RunAsync(string name, int defaultPort, Func<string> respond, TimeSpan? heartbeat = null)
    {
        int port = int.TryParse(Environment.GetEnvironmentVariable($"LRSAMPLE_{name.ToUpperInvariant()}_PORT"), out var p) ? p : defaultPort;
        string prefix = $"http://localhost:{port}/";

        // Not disposed: the Ctrl+C and ProcessExit handlers may still fire while the process exits
        var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // shut down cleanly instead of being killed mid-request
            stopping.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stopping.Cancel();

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.Error.WriteLine($"{name} cannot listen on {prefix}: {ex.Message} (port already in use?)");
            return 1;
        }
        Console.WriteLine($"{name} listening on {prefix} (try {prefix}health)");

        int requests = 0;
        var heartbeatLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(heartbeat ?? TimeSpan.FromSeconds(10));
            try
            {
                while (await timer.WaitForNextTickAsync(stopping.Token))
                    Console.WriteLine($"{name} alive, {Volatile.Read(ref requests)} request(s) served");
            }
            catch (OperationCanceledException)
            {
            }
        });

        using (stopping.Token.Register(listener.Stop))
        {
            while (!stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception) when (stopping.IsCancellationRequested)
                {
                    break; // listener.Stop() aborts the pending wait
                }

                Interlocked.Increment(ref requests);
                string body = context.Request.Url?.AbsolutePath == "/health" ? "ok" : respond();
                byte[] bytes = Encoding.UTF8.GetBytes(body + "\n");
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        await heartbeatLoop;
        Console.WriteLine($"{name} stopped");
        return 0;
    }
}
