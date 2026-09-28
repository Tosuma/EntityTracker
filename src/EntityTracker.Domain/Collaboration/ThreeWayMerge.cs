namespace EntityTracker.Domain.Collaboration;

public enum MergeSide { Local, Remote }

public sealed record MergeConflict<T>(string Path, T? Base, T? Local, T? Remote);

/// <summary>Pure three-way rules shared by Project snapshot fields and keyed relationships.</summary>
public static class ThreeWayMerge
{
    public static T? Value<T>(string path, T? basis, T? local, T? remote,
        Func<MergeConflict<T>, MergeSide?> choose, IEqualityComparer<T?>? comparer = null)
    {
        comparer ??= EqualityComparer<T?>.Default;
        if (comparer.Equals(local, remote)) return local;
        if (comparer.Equals(local, basis)) return remote;
        if (comparer.Equals(remote, basis)) return local;
        return choose(new MergeConflict<T>(path, basis, local, remote)) switch
        {
            MergeSide.Local => local,
            MergeSide.Remote => remote,
            _ => default
        };
    }

    public static IReadOnlyList<T> Keyed<T, TKey>(string path, IEnumerable<T>? basis,
        IEnumerable<T> local, IEnumerable<T> remote, Func<T, TKey> key,
        Func<string, T?, T?, T?, T?> merge, IComparer<TKey>? order = null)
        where TKey : notnull
    {
        Dictionary<TKey, T> b = (basis ?? []).ToDictionary(key);
        Dictionary<TKey, T> l = local.ToDictionary(key);
        Dictionary<TKey, T> r = remote.ToDictionary(key);
        return b.Keys.Union(l.Keys).Union(r.Keys).OrderBy(x => x, order ?? Comparer<TKey>.Default)
            .Select(id => merge($"{path}/{id}", b.GetValueOrDefault(id),
                l.GetValueOrDefault(id), r.GetValueOrDefault(id)))
            .Where(value => value is not null).Cast<T>().ToArray();
    }
}
