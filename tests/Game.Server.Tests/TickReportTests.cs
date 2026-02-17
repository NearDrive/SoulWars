using System.Text.Json;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class TickReportTests
{
    [Fact]
    public void TickReport_DoesNotAffectChecksum()
    {
        RunResult off = RunScenario(enableTickReports: false, ticks: 25, seed: 4040);
        RunResult on = RunScenario(enableTickReports: true, ticks: 25, seed: 4040);

        Assert.Equal(off.FinalChecksum, on.FinalChecksum);
        Assert.Equal(off.TickChecksums, on.TickChecksums);
        Assert.NotEmpty(on.TickReports);
    }

    [Fact]
    public void TwoRuns_SameTickReport()
    {
        RunResult runA = RunScenario(enableTickReports: true, ticks: 25, seed: 5050);
        RunResult runB = RunScenario(enableTickReports: true, ticks: 25, seed: 5050);

        Assert.Equal(runA.TickReports.Count, runB.TickReports.Count);

        for (int i = 0; i < runA.TickReports.Count; i++)
        {
            string jsonA = JsonSerializer.Serialize(runA.TickReports[i]);
            string jsonB = JsonSerializer.Serialize(runB.TickReports[i]);
            Assert.Equal(jsonA, jsonB);
        }
    }

    private static RunResult RunScenario(bool enableTickReports, int ticks, int seed)
    {
        ServerConfig config = ServerConfig.Default(seed) with
        {
            SnapshotEveryTicks = 1,
            EnableTickReports = enableTickReports
        };

        ServerHost host = new(config);
        List<TickReport> tickReports = new();
        if (enableTickReports)
        {
            host.TickReportSink = tickReports.Add;
        }

        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "tickreport-user")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));

        List<string> checksumsByTick = new();
        for (int i = 0; i < ticks; i++)
        {
            host.StepOnce();
            checksumsByTick.Add(StateChecksum.Compute(host.CurrentWorld));

            if ((i % 2) == 0)
            {
                endpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(i + 1, 1, 0)));
            }

            endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, i + 1)));

            while (endpoint.TryDequeueFromServer(out _))
            {
            }
        }

        return new RunResult(
            FinalChecksum: StateChecksum.Compute(host.CurrentWorld),
            TickChecksums: checksumsByTick,
            TickReports: tickReports);
    }

    private sealed record RunResult(string FinalChecksum, IReadOnlyList<string> TickChecksums, IReadOnlyList<TickReport> TickReports);
}
