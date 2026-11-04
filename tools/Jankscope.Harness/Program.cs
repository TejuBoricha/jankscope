namespace Jankscope.Harness;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Ctrl+C stops the paced loop between frames, so the swap chain, device and window are released in the normal way.
        using var cancellation = new CancellationTokenSource();
        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += OnCancel;
        try
        {
            return HarnessCli.Run(args, Console.Out, Console.Error, cancellationToken: cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }
}
