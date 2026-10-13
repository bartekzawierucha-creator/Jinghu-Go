using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JianghuGo.Infrastructure.InMemory;

/// <summary>
/// JSON form of the in-memory tables: one entry per table with its key and row types and the rows as key/value pairs
/// (keys may be value objects or tuples, which JSON object keys cannot hold). Format is tied to the build that wrote it.
/// </summary>
public static class InMemorySnapshot
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        IncludeFields = true, // tuple keys (Item1, Item2)
        WriteIndented = false,
    };

    private static readonly MethodInfo BuildMethod = typeof(InMemorySnapshot).GetMethod(nameof(Build), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo RowsMethod = typeof(InMemorySnapshot).GetMethod(nameof(Rows), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static string Serialize(ImmutableDictionary<string, object> tables)
    {
        var root = new JsonObject { ["format"] = FormatVersion };
        var list = new JsonArray();
        foreach (var (name, table) in tables.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var args = table.GetType().GetGenericArguments();
            var pairsType = typeof(List<>).MakeGenericType(typeof(SnapshotRow<,>).MakeGenericType(args));
            var pairs = RowsMethod.MakeGenericMethod(args).Invoke(null, [table])!;
            list.Add(new JsonObject
            {
                ["name"] = name,
                ["key"] = args[0].AssemblyQualifiedName,
                ["row"] = args[1].AssemblyQualifiedName,
                ["rows"] = JsonSerializer.SerializeToNode(pairs, pairsType, Options),
            });
        }

        root["tables"] = list;
        return root.ToJsonString();
    }

    public static ImmutableDictionary<string, object> Deserialize(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        if ((int?)root["format"] != FormatVersion)
        {
            throw new InvalidDataException("Unsupported in-memory snapshot format.");
        }

        var tables = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var node in root["tables"]!.AsArray())
        {
            var key = Type.GetType((string)node!["key"]!, throwOnError: true)!;
            var row = Type.GetType((string)node["row"]!, throwOnError: true)!;
            var pairsType = typeof(List<>).MakeGenericType(typeof(SnapshotRow<,>).MakeGenericType(key, row));
            var pairs = node["rows"].Deserialize(pairsType, Options)!;
            tables[(string)node["name"]!] = BuildMethod.MakeGenericMethod(key, row).Invoke(null, [pairs])!;
        }

        return tables.ToImmutable();
    }

    private static List<SnapshotRow<TKey, TRow>> Rows<TKey, TRow>(ImmutableDictionary<TKey, TRow> table)
        where TKey : notnull => table.Select(kv => new SnapshotRow<TKey, TRow>(kv.Key, kv.Value)).ToList();

    private static ImmutableDictionary<TKey, TRow> Build<TKey, TRow>(List<SnapshotRow<TKey, TRow>> rows)
        where TKey : notnull => ImmutableDictionary.CreateRange(rows.Select(r => new KeyValuePair<TKey, TRow>(r.Key, r.Row)));
}

/// <summary>
/// One stored row. Our own type rather than KeyValuePair: on a trimmed runtime (the Android build) the base library's
/// constructor parameter names are gone, which System.Text.Json needs for KeyValuePair.
/// </summary>
public sealed record SnapshotRow<TKey, TRow>(TKey Key, TRow Row);

/// <summary>Writes the newest committed state in the background (coalescing bursts of commits), atomically.</summary>
internal sealed class InMemorySnapshotWriter : IDisposable
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private ImmutableDictionary<string, object>? _pending;
    private bool _disposed;

    public InMemorySnapshotWriter(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _loop = Task.Run(LoopAsync);
    }

    public void Schedule(ImmutableDictionary<string, object> tables)
    {
        lock (_sync)
        {
            _pending = tables;
        }

        _signal.Release();
    }

    /// <summary>The last background write failure, if any (diagnostics).</summary>
    public Exception? LastError { get; private set; }

    public void Flush()
    {
        ImmutableDictionary<string, object>? tables;
        lock (_sync)
        {
            tables = _pending;
            _pending = null;
            if (tables is not null)
            {
                Write(tables);
            }
        }
    }

    private async Task LoopAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_stop.Token);
                await Task.Delay(300, _stop.Token); // coalesce a burst of commits into one write
                try
                {
                    Flush();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // keep the writer alive (the next commit retries); the failure is visible in the process log
                    LastError = e;
                    Console.Error.WriteLine("jianghu: snapshot write failed: " + e);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    private void Write(ImmutableDictionary<string, object> tables)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, InMemorySnapshot.Serialize(tables));
        File.Move(temp, _path, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // the loop only ends by cancellation
        }

        Flush();
        _stop.Dispose();
        _signal.Dispose();
    }
}
