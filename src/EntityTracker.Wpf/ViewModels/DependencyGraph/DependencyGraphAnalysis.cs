using System.Numerics;

namespace EntityTracker.Wpf.ViewModels.DependencyGraph;

/// <summary>
/// Derives the structure the map is drawn from: which links are essential (not implied by a
/// longer chain), each entity's orbit level, and how many entities it depends on transitively.
/// </summary>
internal static class DependencyGraphAnalysis
{
    internal static void Analyze(IReadOnlyList<DependencyGraphNode> nodes, IReadOnlyList<DependencyGraphEdge> edges)
    {
        int count = nodes.Count;
        Dictionary<DependencyGraphNode, int> index = new(count, ReferenceEqualityComparer.Instance);
        for (int i = 0; i < count; i++) index[nodes[i]] = i;
        List<DependencyGraphEdge>[] incoming = new List<DependencyGraphEdge>[count];
        List<DependencyGraphEdge>[] outgoing = new List<DependencyGraphEdge>[count];
        for (int i = 0; i < count; i++)
        {
            incoming[i] = [];
            outgoing[i] = [];
        }

        foreach (DependencyGraphEdge edge in edges)
        {
            incoming[index[edge.To]].Add(edge);
            outgoing[index[edge.From]].Add(edge);
        }

        // Dependencies finish before their dependents; links closing a cycle are set aside.
        HashSet<DependencyGraphEdge> cycleEdges = new(ReferenceEqualityComparer.Instance);
        List<int> order = TopologicalOrder(count, index, incoming, cycleEdges);

        int words = (count + 63) / 64;
        ulong[][] reach = new ulong[count][];
        foreach (int node in order)
        {
            ulong[] bits = reach[node] = new ulong[words];
            foreach (DependencyGraphEdge edge in incoming[node])
            {
                if (cycleEdges.Contains(edge)) continue;
                int dependency = index[edge.From];
                Or(bits, reach[dependency]);
                Set(bits, dependency);
            }
        }

        foreach (int node in order)
        {
            nodes[node].TransitiveDependencyCount = reach[node].Sum(static word => BitOperations.PopCount(word));
            foreach (DependencyGraphEdge edge in incoming[node])
            {
                int dependency = index[edge.From];
                edge.IsEssential = cycleEdges.Contains(edge) || !incoming[node].Any(other =>
                    !ReferenceEquals(other, edge) && !cycleEdges.Contains(other) &&
                    IsSet(reach[index[other.From]], dependency));
            }
        }

        // Longest path to an entity nothing depends on: hubs sit at level 0, foundations furthest out.
        int[] level = new int[count];
        for (int position = order.Count - 1; position >= 0; position--)
        {
            int node = order[position];
            foreach (DependencyGraphEdge edge in outgoing[node])
            {
                if (cycleEdges.Contains(edge)) continue;
                level[node] = Math.Max(level[node], level[index[edge.To]] + 1);
            }
        }

        for (int i = 0; i < count; i++)
            nodes[i].Level = nodes[i].IsConnected ? level[i] : -1;
    }

    private static List<int> TopologicalOrder(int count, Dictionary<DependencyGraphNode, int> index,
        List<DependencyGraphEdge>[] incoming, HashSet<DependencyGraphEdge> cycleEdges)
    {
        List<int> order = new(count);
        byte[] state = new byte[count]; // 0 = new, 1 = on stack, 2 = done
        Stack<(int Node, int Next)> stack = new();
        for (int root = 0; root < count; root++)
        {
            if (state[root] != 0) continue;
            stack.Push((root, 0));
            state[root] = 1;
            while (stack.Count > 0)
            {
                (int node, int next) = stack.Pop();
                if (next < incoming[node].Count)
                {
                    stack.Push((node, next + 1));
                    DependencyGraphEdge edge = incoming[node][next];
                    int dependency = index[edge.From];
                    if (state[dependency] == 1)
                    {
                        cycleEdges.Add(edge);
                    }
                    else if (state[dependency] == 0)
                    {
                        state[dependency] = 1;
                        stack.Push((dependency, 0));
                    }

                    continue;
                }

                state[node] = 2;
                order.Add(node);
            }
        }

        return order;
    }

    private static void Or(ulong[] target, ulong[] source)
    {
        for (int i = 0; i < target.Length; i++) target[i] |= source[i];
    }

    private static void Set(ulong[] bits, int index) => bits[index >> 6] |= 1UL << (index & 63);

    private static bool IsSet(ulong[] bits, int index) => (bits[index >> 6] & (1UL << (index & 63))) != 0;
}
