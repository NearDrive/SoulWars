using Game.Client.Headless;

ClientOptions options = ClientOptions.Parse(args);
await using TcpClientTransport transport = new();
HeadlessClientRunner runner = new(transport, options);

using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
HeadlessRunResult result = await runner.RunAsync(maxTicks: 200, cts.Token);

foreach (string line in result.Logs)
{
    Console.WriteLine(line);
}

return result.HitObserved ? 0 : 2;
