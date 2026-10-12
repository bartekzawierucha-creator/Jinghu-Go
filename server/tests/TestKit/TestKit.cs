using System.Diagnostics;
using System.Reflection;

namespace JianghuGo.TestKit;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CategoryAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Per-test setup/teardown (a fresh class instance is created for every test).</summary>
public interface IAsyncLifetime
{
    Task InitializeAsync();
    Task DisposeAsync();
}

public sealed class AssertionException(string message) : Exception(message);

/// <summary>Thrown to mark a test NOT RUN (e.g. required database not configured). Never counted as passed.</summary>
public sealed class SkipException(string reason) : Exception(reason);

public static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition) throw new AssertionException(message ?? "Expected true.");
    }

    public static void False(bool condition, string? message = null) => True(!condition, message ?? "Expected false.");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{message ?? "Values differ"}.{Environment.NewLine}  Expected: {expected}{Environment.NewLine}  Actual:   {actual}");
    }

    public static void NotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new AssertionException($"{message ?? "Values should differ"}: both are {actual}");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        if (!e.SequenceEqual(a))
            throw new AssertionException($"{message ?? "Sequences differ"}.{Environment.NewLine}  Expected: [{string.Join(", ", e)}]{Environment.NewLine}  Actual:   [{string.Join(", ", a)}]");
    }

    public static T NotNull<T>(T? value, string? message = null) where T : class =>
        value ?? throw new AssertionException(message ?? "Expected non-null.");

    public static void Null(object? value, string? message = null)
    {
        if (value is not null) throw new AssertionException(message ?? $"Expected null, got {value}.");
    }

    public static void Contains(string expectedSubstring, string? actual)
    {
        if (actual is null || !actual.Contains(expectedSubstring, StringComparison.Ordinal))
            throw new AssertionException($"Expected to contain '{expectedSubstring}', got: {actual}");
    }

    public static void InRange(int value, int min, int max, string? message = null)
    {
        if (value < min || value > max) throw new AssertionException($"{message ?? "Out of range"}: {value} not in [{min}, {max}]");
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"Expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
        }

        throw new AssertionException($"Expected {typeof(TException).Name}, nothing was thrown.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"Expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
        }

        throw new AssertionException($"Expected {typeof(TException).Name}, nothing was thrown.");
    }

    public static Exception Fail(string message) => throw new AssertionException(message);
}

/// <summary>Controllable clock for expiry/lockout tests.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

public static class TestRunner
{
    /// <summary>
    /// Runs every public [Fact] method in the assembly. Args: --filter &lt;substring&gt;, --category &lt;name&gt;.
    /// Exit code 0 only if at least one test ran and none failed. Never reports skipped tests as passed.
    /// </summary>
    public static async Task<int> RunAsync(Assembly assembly, string[] args)
    {
        string? filter = ArgValue(args, "--filter");
        string? category = ArgValue(args, "--category");

        var tests = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<FactAttribute>() is not null)
                .Select(m => (Type: t, Method: m, Category: t.GetCustomAttribute<CategoryAttribute>()?.Name ?? "Unit")))
            .Where(x => filter is null || $"{x.Type.FullName}.{x.Method.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Where(x => category is null || string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Type.FullName, StringComparer.Ordinal).ThenBy(x => x.Method.Name, StringComparer.Ordinal)
            .ToList();

        var failures = new List<string>();
        var skipped = new List<string>();
        var total = Stopwatch.StartNew();
        foreach (var (type, method, cat) in tests)
        {
            var name = $"[{cat}] {type.Name}.{method.Name}";
            var sw = Stopwatch.StartNew();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await RunOneAsync(type, method).WaitAsync(timeout.Token);
                Console.WriteLine($"  PASS  {name} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex) when ((ex is TargetInvocationException { InnerException: SkipException } ? ex.InnerException : ex) is SkipException skip)
            {
                skipped.Add(name);
                Console.WriteLine($"  SKIP  {name} — NOT RUN: {skip.Message}");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } ie } ? ie : ex;
                if (inner is OperationCanceledException && sw.Elapsed >= TimeSpan.FromSeconds(60))
                {
                    inner = new TimeoutException("Test exceeded 60 s.");
                }

                failures.Add(name);
                Console.WriteLine($"  FAIL  {name} ({sw.ElapsedMilliseconds} ms)");
                Console.WriteLine("        " + inner.GetType().Name + ": " + inner.Message.Replace(Environment.NewLine, Environment.NewLine + "        ", StringComparison.Ordinal));
                if (inner is not AssertionException)
                {
                    Console.WriteLine(inner.StackTrace);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{assembly.GetName().Name}: {tests.Count - failures.Count - skipped.Count} passed, {failures.Count} failed, {skipped.Count} NOT RUN, {tests.Count} total in {total.Elapsed.TotalSeconds:0.0}s");
        if (tests.Count - skipped.Count == 0)
        {
            Console.WriteLine("ERROR: no tests matched; treating as failure.");
            return 2;
        }

        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task RunOneAsync(Type type, MethodInfo method)
    {
        var instance = Activator.CreateInstance(type)!;
        var lifetime = instance as IAsyncLifetime;
        if (lifetime is not null) await lifetime.InitializeAsync();
        try
        {
            if (method.Invoke(instance, null) is Task task) await task;
        }
        finally
        {
            if (lifetime is not null) await lifetime.DisposeAsync();
            if (instance is IDisposable d) d.Dispose();
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
