namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Checks a <see cref="PowerGraph"/> for structural problems in the STORED links only. It reports
/// missing endpoints, cross-save links, loops and unanchored branches. It does not judge whether a
/// network would actually power anything (that needs rules that are not verified), and it never
/// treats a finding as proof of corruption: an endpoint "missing" here may live in a save that was
/// not supplied.
/// </summary>
public static class PowerGraphValidator
{
    /// <summary>Runs every check. Findings are ordered by kind, then subject, so output is stable.</summary>
    public static IReadOnlyList<PowerIssue> Validate(PowerGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var issues = new List<PowerIssue>();

        foreach (var socket in graph.Sockets.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (socket.Kind == PowerSocketKind.Unrecognized)
            {
                issues.Add(new PowerIssue(PowerIssueKind.UnrecognizedSocketKey, PowerIssueSeverity.Warning,
                    "Socket key is neither a level actor path nor a device GUID plus a socket number.",
                    [socket.Key], socket.Location));
            }
            if (socket.OwnerStatus == PowerEndpointStatus.MissingInSuppliedSaves)
            {
                var plugged = graph.LinksOfSocket(socket.Key).Count > 0;
                issues.Add(new PowerIssue(PowerIssueKind.SocketOwnerMissing,
                    plugged ? PowerIssueSeverity.Warning : PowerIssueSeverity.Info,
                    plugged
                        ? "This outlet's owning device is in none of the supplied saves, yet the outlet still supplies a device."
                        : "This outlet's owning device is in none of the supplied saves (an orphaned, unplugged socket record).",
                    [socket.Key, socket.OwnerDeviceId!], socket.Location));
            }
        }

        foreach (var link in graph.Links.OrderBy(l => l.Socket.Key, StringComparer.Ordinal).ThenBy(l => l.DeviceId, StringComparer.Ordinal))
        {
            if (link.DeviceStatus == PowerEndpointStatus.MissingInSuppliedSaves)
            {
                issues.Add(new PowerIssue(PowerIssueKind.LinkedDeviceMissing, PowerIssueSeverity.Warning,
                    "A socket names a device that is in none of the supplied saves.",
                    [link.Socket.Key, link.DeviceId], link.OwningRecord));
            }
            else if (link.IsCrossSave)
            {
                issues.Add(new PowerIssue(PowerIssueKind.CrossSaveLink, PowerIssueSeverity.Info,
                    "The socket and the device it supplies are in different save files.",
                    [link.Socket.Key, link.DeviceId], link.OwningRecord));
            }
            if (link.Slot == PowerLinkSlot.ExtraPowered)
            {
                issues.Add(new PowerIssue(PowerIssueKind.ExtraPoweredDevicesUsed, PowerIssueSeverity.Info,
                    "This socket uses the extra powered devices list; its meaning is not verified.",
                    [link.Socket.Key, link.DeviceId], link.OwningRecord));
            }
            if (link.Socket.OwnerDeviceId is { } owner
                && string.Equals(owner, link.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new PowerIssue(PowerIssueKind.SelfLink, PowerIssueSeverity.Warning,
                    "A device is plugged into an outlet that it owns.",
                    [link.Socket.Key, link.DeviceId], link.OwningRecord));
            }
        }

        foreach (var group in graph.Links.GroupBy(l => l.DeviceId, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Select(l => l.Socket.Key).Distinct(StringComparer.Ordinal).Count() > 1)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            issues.Add(new PowerIssue(PowerIssueKind.DeviceFedByMultipleSockets, PowerIssueSeverity.Info,
                "More than one socket names this device. Whether the game allows this is not verified.",
                group.Select(l => l.Socket.Key).Prepend(group.Key).ToList(), null));
        }

        AddCycles(graph, issues);
        AddDisconnectedBranches(graph, issues);
        return issues;
    }

    // Device-level directed graph: owner -> plugged device. A cycle means power would loop.
    private static void AddCycles(PowerGraph graph, List<PowerIssue> issues)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in graph.Links)
        {
            if (link.Socket.OwnerDeviceId is { } owner && link.DeviceStatus != PowerEndpointStatus.MissingInSuppliedSaves
                && !string.Equals(owner, link.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                if (!edges.TryGetValue(owner, out var list))
                {
                    edges[owner] = list = [];
                }
                list.Add(link.DeviceId);
            }
        }
        // Iterative three-colour DFS; report each cycle once via its first-found back edge target.
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 1 = on stack, 2 = done
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in edges.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (state.ContainsKey(root))
            {
                continue;
            }
            var stack = new Stack<(string Node, int Next)>();
            stack.Push((root, 0));
            state[root] = 1;
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                var children = edges.GetValueOrDefault(node);
                if (children is null || next >= children.Count)
                {
                    state[node] = 2;
                    continue;
                }
                stack.Push((node, next + 1));
                var child = children[next];
                if (!state.TryGetValue(child, out var childState))
                {
                    state[child] = 1;
                    stack.Push((child, 0));
                }
                else if (childState == 1 && reported.Add(child))
                {
                    issues.Add(new PowerIssue(PowerIssueKind.LinkCycle, PowerIssueSeverity.Warning,
                        "The stored links loop back to this device.", [child, node], null));
                }
            }
        }
    }

    // A branch = connected group of things joined by stored links. It is "disconnected" when it
    // holds no level-placed socket. That is only a structural note: whether batteries or other
    // devices can be sources on their own is not verified.
    private static void AddDisconnectedBranches(PowerGraph graph, List<PowerIssue> issues)
    {
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Find(string x)
        {
            if (!parent.TryGetValue(x, out var p))
            {
                parent[x] = x;
                return x;
            }
            while (!string.Equals(p, parent[p], StringComparison.OrdinalIgnoreCase))
            {
                parent[p] = parent[parent[p]];
                p = parent[p];
            }
            parent[x] = p;
            return p;
        }
        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (!string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase))
            {
                parent[ra] = rb;
            }
        }

        var anchored = new List<string>();
        foreach (var link in graph.Links.Where(l => l.DeviceStatus != PowerEndpointStatus.MissingInSuppliedSaves))
        {
            var from = link.Socket.OwnerDeviceId ?? link.Socket.Key;
            Union(from, link.DeviceId);
            if (link.Socket.Kind == PowerSocketKind.LevelPlaced)
            {
                anchored.Add(from);
            }
        }
        var anchoredRoots = new HashSet<string>(anchored.Select(Find), StringComparer.OrdinalIgnoreCase);
        foreach (var group in parent.Keys.GroupBy(Find, StringComparer.OrdinalIgnoreCase)
                     .Where(g => !anchoredRoots.Contains(g.Key))
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var members = group.OrderBy(m => m, StringComparer.Ordinal).ToList();
            issues.Add(new PowerIssue(PowerIssueKind.DisconnectedBranch, PowerIssueSeverity.Info,
                $"A group of {members.Count} linked items never reaches a level-placed socket in the supplied saves.",
                members, null));
        }
    }
}
