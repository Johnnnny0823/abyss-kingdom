using System.Diagnostics;
using System.Reflection;

namespace AbyssKingdom.Tests;

/// <summary>標記一個測試方法（可為 void 或 Task）。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public sealed class AssertException : Exception
{
    public AssertException(string message) : base(message) { }
}

public static class Assert
{
    public static void Equal<T>(T expected, T actual, string? what = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertException($"{what ?? "值"} 不符：預期 {expected}，實際 {actual}");
    }

    public static void True(bool condition, string message)
    {
        if (!condition) throw new AssertException(message);
    }

    public static T NotNull<T>(T? value, string what) where T : class
    {
        return value ?? throw new AssertException($"{what} 不應為 null");
    }
}

/// <summary>極簡測試執行器：找出所有 [Test] 方法逐一執行，回報結果。</summary>
public static class TestRunner
{
    public static async Task<int> Main(string[] args)
    {
        var filter = args.FirstOrDefault();
        var tests = typeof(TestRunner).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Where(m => filter == null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();

        int passed = 0, failed = 0;
        foreach (var m in tests)
        {
            var name = $"{m.DeclaringType!.Name}.{m.Name}";
            var sw = Stopwatch.StartNew();
            try
            {
                var instance = m.IsStatic ? null : Activator.CreateInstance(m.DeclaringType!);
                var ret = m.Invoke(instance, null);
                if (ret is Task task) await task.WaitAsync(TimeSpan.FromSeconds(30));
                passed++;
                Console.WriteLine($"  PASS  {name} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                failed++;
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Console.WriteLine($"  FAIL  {name}: {inner.GetType().Name}: {inner.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{passed} passed, {failed} failed, {tests.Count} total");
        return failed == 0 && tests.Count > 0 ? 0 : 1;
    }
}
