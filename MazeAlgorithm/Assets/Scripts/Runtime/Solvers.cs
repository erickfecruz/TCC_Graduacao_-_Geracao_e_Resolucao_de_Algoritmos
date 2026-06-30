using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MazeDOTS
{
    /// <summary>One visualization event: paint <see cref="cell"/>'s floor with <see cref="color"/>.</summary>
    public struct SolveStep
    {
        public int cell;
        public float4 color;
    }

    internal static class SolveUtil
    {
        public static Dir TurnLeft(Dir d)
        {
            switch (d)
            {
                case Dir.North: return Dir.West;
                case Dir.West: return Dir.South;
                case Dir.South: return Dir.East;
                default: return Dir.North; // East
            }
        }

        public static Dir TurnRight(Dir d)
        {
            switch (d)
            {
                case Dir.North: return Dir.East;
                case Dir.East: return Dir.South;
                case Dir.South: return Dir.West;
                default: return Dir.North; // West
            }
        }

        public static int OpenDegree(in NativeArray<byte> walls, int cell, int width, int total)
        {
            int d = 0;
            if (MazeGrid.Neighbour(cell, Dir.North, width, total) >= 0 && MazeGrid.IsOpen(walls, cell, Dir.North)) d++;
            if (MazeGrid.Neighbour(cell, Dir.South, width, total) >= 0 && MazeGrid.IsOpen(walls, cell, Dir.South)) d++;
            if (MazeGrid.Neighbour(cell, Dir.East, width, total) >= 0 && MazeGrid.IsOpen(walls, cell, Dir.East)) d++;
            if (MazeGrid.Neighbour(cell, Dir.West, width, total) >= 0 && MazeGrid.IsOpen(walls, cell, Dir.West)) d++;
            return d;
        }
    }

    /// <summary>
    /// Wall follower (hand-on-wall) solver. <see cref="rightHand"/> selects the right-hand rule,
    /// otherwise the left-hand rule is used. Mirrors AlwaysLeft / AlwaysRight from the original.
    /// </summary>
    [BurstCompile]
    public struct WallFollowerJob : IJob
    {
        public int width;
        public int height;
        public bool rightHand;
        [ReadOnly] public NativeArray<byte> walls;
        public NativeList<SolveStep> steps;

        public void Execute()
        {
            int total = width * height;
            int start = MazeGrid.Start(width, height);
            int end = MazeGrid.End(width);

            steps.Add(new SolveStep { cell = start, color = MazePalette.Start });
            steps.Add(new SolveStep { cell = end, color = MazePalette.End });

            int pos = start;
            Dir heading = Dir.East;
            int guard = total * 4 + 8;

            while (pos != end && guard-- > 0)
            {
                Dir d0, d1, d2, d3;
                if (rightHand)
                {
                    d0 = SolveUtil.TurnRight(heading);
                    d1 = heading;
                    d2 = SolveUtil.TurnLeft(heading);
                    d3 = MazeGrid.Opposite(heading);
                }
                else
                {
                    d0 = SolveUtil.TurnLeft(heading);
                    d1 = heading;
                    d2 = SolveUtil.TurnRight(heading);
                    d3 = MazeGrid.Opposite(heading);
                }

                Dir chosen = TryStep(pos, total, d0, d1, d2, d3);
                heading = chosen;
                pos = MazeGrid.Neighbour(pos, chosen, width, total);

                if (pos != end)
                    steps.Add(new SolveStep { cell = pos, color = MazePalette.Path });
            }
        }

        private Dir TryStep(int pos, int total, Dir a, Dir b, Dir c, Dir d)
        {
            if (Walkable(pos, a, total)) return a;
            if (Walkable(pos, b, total)) return b;
            if (Walkable(pos, c, total)) return c;
            return d;
        }

        private bool Walkable(int pos, Dir dir, int total)
        {
            return MazeGrid.Neighbour(pos, dir, width, total) >= 0 && MazeGrid.IsOpen(walls, pos, dir);
        }
    }

    /// <summary>
    /// Dead-end filling solver. Fills every dead end (degree-1 cell) back to a junction, leaving the
    /// unique start-to-end corridor, then walks it. Mirrors DeadWay from the original.
    /// </summary>
    [BurstCompile]
    public struct DeadEndFillJob : IJob
    {
        public int width;
        public int height;
        [ReadOnly] public NativeArray<byte> walls;
        public NativeList<SolveStep> steps;

        public void Execute()
        {
            int total = width * height;
            int start = MazeGrid.Start(width, height);
            int end = MazeGrid.End(width);

            steps.Add(new SolveStep { cell = start, color = MazePalette.Start });
            steps.Add(new SolveStep { cell = end, color = MazePalette.End });

            var degree = new NativeArray<int>(total, Allocator.Temp);
            var filled = new NativeArray<bool>(total, Allocator.Temp);
            for (int i = 0; i < total; i++) degree[i] = SolveUtil.OpenDegree(walls, i, width, total);

            bool active = true;
            while (active)
            {
                active = false;
                for (int i = 0; i < total; i++)
                {
                    if (filled[i] || i == start || i == end || degree[i] != 1) continue;

                    int cur = i;
                    // Walk the dead-end corridor until a junction or an endpoint is reached.
                    while (!filled[cur] && cur != start && cur != end && degree[cur] == 1)
                    {
                        filled[cur] = true;
                        active = true;
                        steps.Add(new SolveStep { cell = cur, color = MazePalette.Filled });
                        degree[cur] = 0;

                        int next = OpenUnfilledNeighbour(cur, total, filled);
                        if (next < 0) break;
                        degree[next]--;
                        cur = next;
                    }
                }
            }

            // The remaining unfilled cells form the solution corridor; walk it start -> end.
            int pos = start;
            int prev = -1;
            int guard = total + 8;
            while (pos != end && guard-- > 0)
            {
                steps.Add(new SolveStep { cell = pos, color = pos == start ? MazePalette.Start : MazePalette.Path });
                int next = NextOnPath(pos, total, filled, prev);
                if (next < 0) break;
                prev = pos;
                pos = next;
            }
            steps.Add(new SolveStep { cell = end, color = MazePalette.Path });

            degree.Dispose();
            filled.Dispose();
        }

        private int OpenUnfilledNeighbour(int cell, int total, NativeArray<bool> filled)
        {
            for (int k = 0; k < 4; k++)
            {
                Dir dir = (Dir)k;
                int n = MazeGrid.Neighbour(cell, dir, width, total);
                if (n >= 0 && MazeGrid.IsOpen(walls, cell, dir) && !filled[n]) return n;
            }
            return -1;
        }

        private int NextOnPath(int cell, int total, NativeArray<bool> filled, int prev)
        {
            for (int k = 0; k < 4; k++)
            {
                Dir dir = (Dir)k;
                int n = MazeGrid.Neighbour(cell, dir, width, total);
                if (n >= 0 && n != prev && MazeGrid.IsOpen(walls, cell, dir) && !filled[n]) return n;
            }
            return -1;
        }
    }

    /// <summary>
    /// Flood fill / breadth-first search solver. Labels every cell with its distance from the start,
    /// then back-tracks the shortest path. Mirrors FloodFill from the original.
    /// </summary>
    [BurstCompile]
    public struct FloodFillJob : IJob
    {
        public int width;
        public int height;
        [ReadOnly] public NativeArray<byte> walls;
        public NativeList<SolveStep> steps;

        public void Execute()
        {
            int total = width * height;
            int start = MazeGrid.Start(width, height);
            int end = MazeGrid.End(width);

            steps.Add(new SolveStep { cell = start, color = MazePalette.Start });
            steps.Add(new SolveStep { cell = end, color = MazePalette.End });

            var dist = new NativeArray<int>(total, Allocator.Temp);
            var visited = new NativeArray<bool>(total, Allocator.Temp);
            for (int i = 0; i < total; i++) dist[i] = int.MaxValue;

            var queue = new NativeList<int>(total, Allocator.Temp);
            int head = 0;
            dist[start] = 0;
            visited[start] = true;
            queue.Add(start);

            while (head < queue.Length)
            {
                int cur = queue[head++];
                if (cur == end) break;
                for (int k = 0; k < 4; k++)
                {
                    Dir dir = (Dir)k;
                    int n = MazeGrid.Neighbour(cur, dir, width, total);
                    if (n < 0 || visited[n] || !MazeGrid.IsOpen(walls, cur, dir)) continue;
                    visited[n] = true;
                    dist[n] = dist[cur] + 1;
                    queue.Add(n);
                    if (n != end)
                        steps.Add(new SolveStep { cell = n, color = MazePalette.Visited });
                }
            }

            // Back-track the shortest path from end to start following strictly decreasing distance.
            int pos = end;
            int guard = total + 8;
            while (pos != start && guard-- > 0)
            {
                steps.Add(new SolveStep { cell = pos, color = pos == end ? MazePalette.End : MazePalette.Path });
                int wanted = dist[pos] - 1;
                int step = -1;
                for (int k = 0; k < 4; k++)
                {
                    Dir dir = (Dir)k;
                    int n = MazeGrid.Neighbour(pos, dir, width, total);
                    if (n >= 0 && MazeGrid.IsOpen(walls, pos, dir) && dist[n] == wanted) { step = n; break; }
                }
                if (step < 0) break;
                pos = step;
            }
            steps.Add(new SolveStep { cell = start, color = MazePalette.Path });

            dist.Dispose();
            visited.Dispose();
            queue.Dispose();
        }
    }
}
