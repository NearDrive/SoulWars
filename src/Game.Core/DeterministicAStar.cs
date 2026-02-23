namespace Game.Core;

public sealed class DeterministicAStar
{
    // Deterministic neighbor order: North, East, South, West.
    private static readonly (int Dx, int Dy)[] NeighborOffsets =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0)
    ];

    public bool TryFindPath(
        NavGrid grid,
        TileCoord start,
        TileCoord goal,
        Span<TileCoord> buffer,
        out int len,
        out int expandedCount,
        int maxExpandedNodes)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedNodes);

        len = 0;
        expandedCount = 0;

        if (!grid.IsWalkable(start) || !grid.IsWalkable(goal))
        {
            return false;
        }

        if (start == goal)
        {
            if (buffer.IsEmpty)
            {
                return false;
            }

            buffer[0] = start;
            len = 1;
            return true;
        }

        int nodeCount = grid.NodeCount;
        int width = grid.Width;
        int startId = start.ToNodeId(width);
        int goalId = goal.ToNodeId(width);

        int[] gScore = new int[nodeCount];
        int[] parent = new int[nodeCount];
        bool[] closed = new bool[nodeCount];

        Array.Fill(gScore, int.MaxValue);
        Array.Fill(parent, -1);

        StableMinHeap open = new(nodeCount);
        long sequence = 0;

        gScore[startId] = 0;
        int startH = Manhattan(startId, goalId, width);
        open.Push(new OpenNode(startId, startH, startH, sequence++));

        int expanded = 0;

        while (open.Count > 0)
        {
            OpenNode current = open.Pop();
            int currentId = current.NodeId;

            if (closed[currentId])
            {
                continue;
            }

            if (expanded >= maxExpandedNodes)
            {
                len = 0;
                return false;
            }

            closed[currentId] = true;
            expanded++;
            expandedCount = expanded;

            if (currentId == goalId)
            {
                return TryBuildPath(parent, startId, goalId, width, buffer, out len);
            }

            TileCoord currentTile = TileCoord.FromNodeId(currentId, width);
            int tentativeGBase = gScore[currentId] + 1;

            for (int i = 0; i < NeighborOffsets.Length; i++)
            {
                (int dx, int dy) = NeighborOffsets[i];
                TileCoord neighbor = new(currentTile.X + dx, currentTile.Y + dy);
                if (!grid.IsInBounds(neighbor))
                {
                    continue;
                }

                int neighborId = neighbor.ToNodeId(width);
                if (closed[neighborId] || !grid.IsWalkableNode(neighborId))
                {
                    continue;
                }

                if (tentativeGBase >= gScore[neighborId])
                {
                    continue;
                }

                parent[neighborId] = currentId;
                gScore[neighborId] = tentativeGBase;

                int h = Manhattan(neighborId, goalId, width);
                int f = tentativeGBase + h;
                open.Push(new OpenNode(neighborId, f, h, sequence++));
            }
        }

        len = 0;
        return false;
    }

    private static int Manhattan(int nodeId, int goalId, int width)
    {
        TileCoord node = TileCoord.FromNodeId(nodeId, width);
        TileCoord goal = TileCoord.FromNodeId(goalId, width);
        return Math.Abs(node.X - goal.X) + Math.Abs(node.Y - goal.Y);
    }

    private static bool TryBuildPath(int[] parent, int startId, int goalId, int width, Span<TileCoord> buffer, out int len)
    {
        len = 0;

        int pathLen = 1;
        int cursor = goalId;
        while (cursor != startId)
        {
            cursor = parent[cursor];
            if (cursor < 0)
            {
                return false;
            }

            pathLen++;
        }

        if (pathLen > buffer.Length)
        {
            return false;
        }

        cursor = goalId;
        for (int i = pathLen - 1; i >= 0; i--)
        {
            buffer[i] = TileCoord.FromNodeId(cursor, width);
            if (cursor == startId)
            {
                break;
            }

            cursor = parent[cursor];
        }

        len = pathLen;
        return true;
    }

    private readonly record struct OpenNode(int NodeId, int F, int H, long Sequence);

    private sealed class StableMinHeap
    {
        private OpenNode[] data;

        public StableMinHeap(int capacity)
        {
            data = new OpenNode[Math.Max(4, capacity)];
        }

        public int Count { get; private set; }

        public void Push(OpenNode node)
        {
            if (Count == data.Length)
            {
                Array.Resize(ref data, data.Length * 2);
            }

            data[Count] = node;
            SiftUp(Count);
            Count++;
        }

        public OpenNode Pop()
        {
            OpenNode root = data[0];
            Count--;
            if (Count > 0)
            {
                data[0] = data[Count];
                SiftDown(0);
            }

            return root;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Less(data[index], data[parent]))
                {
                    break;
                }

                (data[index], data[parent]) = (data[parent], data[index]);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = (index * 2) + 1;
                if (left >= Count)
                {
                    return;
                }

                int right = left + 1;
                int smallest = left;
                if (right < Count && Less(data[right], data[left]))
                {
                    smallest = right;
                }

                if (!Less(data[smallest], data[index]))
                {
                    return;
                }

                (data[index], data[smallest]) = (data[smallest], data[index]);
                index = smallest;
            }
        }

        private static bool Less(OpenNode a, OpenNode b)
        {
            if (a.F != b.F)
            {
                return a.F < b.F;
            }

            if (a.H != b.H)
            {
                return a.H < b.H;
            }

            if (a.NodeId != b.NodeId)
            {
                return a.NodeId < b.NodeId;
            }

            return a.Sequence < b.Sequence;
        }
    }
}
