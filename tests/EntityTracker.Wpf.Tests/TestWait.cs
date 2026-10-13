using System.Runtime.CompilerServices;

namespace EntityTracker.Wpf.Tests;

/// <summary>
/// Waits for work the code under test finishes on the thread pool, such as a debounced search.
/// A passing test returns as soon as the condition holds. The limit is generous because a busy CI
/// runner (other test projects run at the same time) can delay thread-pool work by seconds.
/// </summary>
internal static class TestWait
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public static Task UntilAsync(Func<bool> condition,
        [CallerArgumentExpression(nameof(condition))] string description = "") =>
        UntilAsync(() => Task.FromResult(condition()), description);

    public static async Task UntilAsync(Func<Task<bool>> condition,
        [CallerArgumentExpression(nameof(condition))] string description = "")
    {
        DateTime deadline = DateTime.UtcNow + Limit;
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Still not true after {Limit.TotalSeconds:0} s: {description}");
            await Task.Delay(10);
        }
    }
}
