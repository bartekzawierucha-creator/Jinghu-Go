using System.Diagnostics;
using JianghuGo.Application;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>Runs the real JianghuGo.Worker executable (separate OS process) against the test database.</summary>
[Category("Process")]
public class WorkerProcessTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync()
    {
        TestDatabase.RequireConfigured();
        _host = await ApiHost.StartAsync(postgres: true);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    private static string WorkerDll()
    {
        // tests/IntegrationTests/bin/<Configuration>/net10.0  →  src/Worker/bin/<Configuration>/net10.0
        var here = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = here.Parent!.Name;
        var serverRoot = here.Parent!.Parent!.Parent!.Parent!.Parent!.FullName;
        var dll = Path.Combine(serverRoot, "src", "Worker", "bin", configuration, "net10.0", "JianghuGo.Worker.dll");
        return File.Exists(dll) ? dll : throw new SkipException($"worker not built at {dll}");
    }

    private Process StartWorker()
    {
        var psi = new ProcessStartInfo("dotnet", [WorkerDll()]) { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["ConnectionStrings__Jianghu"] = TestDatabase.ConnectionStringFor(_host.Database!);
        psi.Environment["Storage__Provider"] = "Postgres";
        psi.Environment["DOTNET_ENVIRONMENT"] = "Production";
        psi.Environment["Outbox__PollInterval"] = "00:00:00.200";
        var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private async Task<int> PendingAsync() =>
        (await _host.Service<IUnitOfWorkFactory>().RunAsync(uow => _host.Service<IOutbox>().GetStatsAsync(uow, DateTimeOffset.UtcNow, default), default)).Pending;

    private async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!await condition())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(30)) throw new AssertionException($"timed out waiting for: {what}");
            await Task.Delay(100);
        }
    }

    // Acceptance #10: events written while no worker runs are delivered once a worker (re)starts; a killed
    // worker loses nothing.
    [Fact]
    public async Task Worker_process_delivers_events_written_while_it_was_down_and_survives_kill()
    {
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Pierwszy Proces");
        Assert.Equal(1, await PendingAsync());

        using (var worker = StartWorker())
        {
            await WaitUntilAsync(async () => await PendingAsync() == 0, "first worker drains the outbox");
            worker.Kill(entireProcessTree: true);                           // hard kill, no graceful shutdown
            await worker.WaitForExitAsync();
        }

        await p.CreateCharacterAsync("Drugi Proces");                       // written while no worker is running
        await Task.Delay(500);
        Assert.Equal(1, await PendingAsync(), "nothing processes events while the worker is down");

        using (var restarted = StartWorker())
        {
            await WaitUntilAsync(async () => await PendingAsync() == 0, "restarted worker drains the outbox");
            restarted.Kill(entireProcessTree: true);
            await restarted.WaitForExitAsync();
        }

        var items = (await ApiHost.Json(await p.GetAsync("/api/v1/notifications")))["items"]!.AsArray();
        Assert.Equal(2, items.Count);
        Assert.Equal(2, items.Select(i => i!["payload"].Str("name")).Distinct().Count());
    }
}
