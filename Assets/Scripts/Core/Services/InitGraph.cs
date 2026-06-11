using System;
using System.Collections.Generic;
using System.Text;

public sealed class InitGraph<T> where T : class
{
    public IReadOnlyList<T> Order { get; }
    public IReadOnlyList<T> TeardownOrder { get; }

    public InitGraph(IReadOnlyList<T> services, Func<T, IReadOnlyList<Type>> dependenciesOf)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (dependenciesOf == null) throw new ArgumentNullException(nameof(dependenciesOf));

        var depEdges = new Dictionary<T, List<T>>(services.Count);
        foreach (var service in services)
            depEdges[service] = ResolveDependencies(service, services, dependenciesOf);

        var dependents = new Dictionary<T, List<T>>(services.Count);
        foreach (var service in services) dependents[service] = new List<T>();
        foreach (var service in services)
            foreach (var dep in depEdges[service])
                dependents[dep].Add(service);

        var unresolvedCount = new Dictionary<T, int>(services.Count);
        foreach (var service in services) unresolvedCount[service] = depEdges[service].Count;

        var order = new List<T>(services.Count);
        var ready = new Queue<T>();
        foreach (var service in services)
            if (unresolvedCount[service] == 0) ready.Enqueue(service);

        while (ready.Count > 0)
        {
            var service = ready.Dequeue();
            order.Add(service);
            foreach (var dependent in dependents[service])
                if (--unresolvedCount[dependent] == 0)
                    ready.Enqueue(dependent);
        }

        if (order.Count < services.Count)
        {
            var remaining = new HashSet<T>();
            foreach (var service in services)
                if (unresolvedCount[service] > 0) remaining.Add(service);

            T start = default;
            foreach (var s in remaining) { start = s; break; }
            var cycle = FindCycle(start, depEdges, remaining);
            throw new InvalidOperationException(
                "InitGraph: dependency cycle detected.\n" + FormatCycle(cycle));
        }

        Order = order;
        var reversed = new List<T>(order);
        reversed.Reverse();
        TeardownOrder = reversed;
    }

    static List<T> ResolveDependencies(T service, IReadOnlyList<T> services, Func<T, IReadOnlyList<Type>> dependenciesOf)
    {
        var declared = dependenciesOf(service);
        if (declared == null || declared.Count == 0) return new List<T>();

        var resolved = new List<T>(declared.Count);
        for (int i = 0; i < declared.Count; i++)
        {
            var depType = declared[i];
            T match = null;
            for (int j = 0; j < services.Count; j++)
            {
                if (depType.IsAssignableFrom(services[j].GetType()))
                {
                    match = services[j];
                    break;
                }
            }
            if (match == null)
                throw new InvalidOperationException(
                    $"InitGraph: service {service.GetType().Name} declares dependency on " +
                    $"{depType.Name}, no registered service implements it.");
            resolved.Add(match);
        }
        return resolved;
    }

    static List<T> FindCycle(T start, Dictionary<T, List<T>> edges, HashSet<T> withinSet)
    {
        var path = new List<T>();
        var indexOf = new Dictionary<T, int>();
        var current = start;
        while (true)
        {
            if (indexOf.TryGetValue(current, out int existing))
            {
                var cycle = path.GetRange(existing, path.Count - existing);
                cycle.Add(current);
                return cycle;
            }
            indexOf[current] = path.Count;
            path.Add(current);

            T next = null;
            foreach (var dep in edges[current])
                if (withinSet.Contains(dep)) { next = dep; break; }
            if (next == null) return path;
            current = next;
        }
    }

    static string FormatCycle(List<T> cycle)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cycle.Count - 1; i++)
        {
            var from = cycle[i].GetType().Name;
            var to = cycle[i + 1].GetType().Name;
            sb.Append("  - ").Append(from).Append(" depends on ").Append(to).Append('\n');
        }
        sb.Append("  - ").Append(cycle[cycle.Count - 1].GetType().Name).Append(" ← cycle closes here");
        return sb.ToString();
    }
}
