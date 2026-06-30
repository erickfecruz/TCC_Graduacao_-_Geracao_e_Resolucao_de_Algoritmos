using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MazeDOTS
{
    /// <summary>
    /// Binary Tree generator (North/West biased), faithful to the original ArvoreBinaria logic:
    /// for every cell, carve North or West depending on availability, random otherwise.
    /// </summary>
    [BurstCompile]
    public struct BinaryTreeJob : IJob
    {
        public int width;
        public int height;
        public uint seed;
        public NativeArray<byte> walls;

        public void Execute()
        {
            int total = width * height;
            var rng = new Random(seed == 0 ? 1u : seed);
            for (int i = 0; i < total; i++)
            {
                bool hasWest = MazeGrid.HasWest(i, width);
                bool hasNorth = MazeGrid.HasNorth(i, width, total);
                if (!hasWest && !hasNorth) continue;

                if (!hasWest) MazeGrid.Carve(walls, i, Dir.North, width, total);
                else if (!hasNorth) MazeGrid.Carve(walls, i, Dir.West, width, total);
                else if (rng.NextInt(0, 2) == 0) MazeGrid.Carve(walls, i, Dir.West, width, total);
                else MazeGrid.Carve(walls, i, Dir.North, width, total);
            }
        }
    }

    /// <summary>
    /// Recursive Backtracker (depth-first) generator, faithful to the original BacktrackingAlgorithm:
    /// random start, explicit stack, carve to a random unvisited neighbour. Sequential by nature.
    /// </summary>
    [BurstCompile]
    public struct RecursiveBacktrackerJob : IJob
    {
        public int width;
        public int height;
        public uint seed;
        public NativeArray<byte> walls;

        public void Execute()
        {
            int total = width * height;
            var rng = new Random(seed == 0 ? 1u : seed);
            var visited = new NativeArray<bool>(total, Allocator.Temp);
            var stack = new NativeList<int>(1024, Allocator.Temp);
            var neigh = new NativeArray<int>(4, Allocator.Temp);
            var ndir = new NativeArray<Dir>(4, Allocator.Temp);

            int current = rng.NextInt(0, total);
            visited[current] = true;
            stack.Add(current);

            while (stack.Length > 0)
            {
                current = stack[stack.Length - 1];
                int count = 0;
                Collect(current, total, visited, neigh, ndir, ref count, Dir.East);
                Collect(current, total, visited, neigh, ndir, ref count, Dir.West);
                Collect(current, total, visited, neigh, ndir, ref count, Dir.North);
                Collect(current, total, visited, neigh, ndir, ref count, Dir.South);

                if (count == 0) { stack.RemoveAt(stack.Length - 1); continue; }

                int pick = rng.NextInt(0, count);
                MazeGrid.Carve(walls, current, ndir[pick], width, total);
                visited[neigh[pick]] = true;
                stack.Add(neigh[pick]);
            }

            visited.Dispose();
            stack.Dispose();
            neigh.Dispose();
            ndir.Dispose();
        }

        void Collect(int cell, int total, NativeArray<bool> visited, NativeArray<int> neigh,
            NativeArray<Dir> ndir, ref int count, Dir dir)
        {
            int n = MazeGrid.Neighbour(cell, dir, width, total);
            if (n >= 0 && !visited[n]) { neigh[count] = n; ndir[count] = dir; count++; }
        }
    }

    /// <summary>
    /// Eller's algorithm generator, faithful to the original EllerAlgorithm: row-by-row set merging
    /// with random east links and at least one downward link per set. Sequential, O(width) extra state.
    /// </summary>
    [BurstCompile]
    public struct EllerJob : IJob
    {
        public int width;
        public int height;
        public uint seed;
        public NativeArray<byte> walls;

        public void Execute()
        {
            int total = width * height;
            var rng = new Random(seed == 0 ? 1u : seed);
            var set = new NativeArray<int>(total, Allocator.Temp);
            var carriedDown = new NativeArray<bool>(total, Allocator.Temp);
            var members = new NativeList<int>(width, Allocator.Temp);

            for (int row = 0; row < height - 1; row++)
            {
                int rowStart = row * width;
                for (int i = rowStart; i < rowStart + width; i++)
                    if (set[i] == 0) set[i] = i + 1;

                for (int i = rowStart; i < rowStart + width - 1; i++)
                {
                    if (set[i + 1] != set[i] && rng.NextInt(0, 2) == 0)
                    {
                        int from = set[i + 1], to = set[i];
                        for (int w = rowStart; w < rowStart + width; w++)
                            if (set[w] == from) set[w] = to;
                        MazeGrid.Carve(walls, i, Dir.East, width, total);
                    }
                }

                for (int i = rowStart; i < rowStart + width; i++) carriedDown[i] = false;

                for (int i = rowStart; i < rowStart + width; i++)
                {
                    if (carriedDown[i]) continue;
                    members.Clear();
                    for (int w = i; w < rowStart + width; w++)
                        if (set[w] == set[i]) members.Add(w);

                    int downCount = 0;
                    for (int m = 0; m < members.Length; m++)
                    {
                        bool last = m == members.Length - 1;
                        if (last && downCount == 0 || rng.NextInt(0, 2) == 0)
                        {
                            int cell = members[m];
                            MazeGrid.Carve(walls, cell, Dir.North, width, total);
                            set[cell + width] = set[cell];
                            downCount++;
                        }
                        carriedDown[members[m]] = true;
                    }
                }
            }

            int lastStart = (height - 1) * width;
            for (int i = lastStart; i < lastStart + width; i++)
                if (set[i] == 0) set[i] = i + 1;
            for (int i = lastStart; i < lastStart + width - 1; i++)
            {
                if (set[i + 1] != set[i])
                {
                    int from = set[i + 1], to = set[i];
                    for (int w = lastStart; w < lastStart + width; w++)
                        if (set[w] == from) set[w] = to;
                    MazeGrid.Carve(walls, i, Dir.East, width, total);
                }
            }

            set.Dispose();
            carriedDown.Dispose();
            members.Dispose();
        }
    }
}
