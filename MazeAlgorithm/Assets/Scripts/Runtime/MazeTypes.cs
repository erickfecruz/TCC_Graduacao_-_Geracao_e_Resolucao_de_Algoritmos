using System;
using Unity.Mathematics;

namespace MazeDOTS
{
    /// <summary>
    /// Wall bitmask for a single cell. A set bit means the wall is PRESENT (closed).
    /// Carving a passage clears the bit on both adjacent cells.
    /// </summary>
    [Flags]
    public enum Wall : byte
    {
        None = 0,
        North = 1,
        South = 2,
        East = 4,
        West = 8,
        All = North | South | East | West
    }

    /// <summary>Cardinal direction, indexed so that (dir ^ 1) gives the opposite for N/S and E/W pairs.</summary>
    public enum Dir : byte
    {
        North = 0,
        South = 1,
        East = 2,
        West = 3
    }

    public enum GenAlgorithm : byte
    {
        BinaryTree = 0,
        RecursiveBacktracker = 1,
        Eller = 2
    }

    public enum SolveAlgorithm : byte
    {
        WallFollowerLeft = 0,
        WallFollowerRight = 1,
        DeadEndFilling = 2,
        FloodFill = 3
    }

    /// <summary>High level lifecycle phase of the maze pipeline.</summary>
    public enum MazePhase : byte
    {
        Idle = 0,
        Generating = 1,
        GenerationPlayback = 2,
        Ready = 3,
        Solving = 4,
        SolvePlayback = 5,
        Done = 6
    }

    /// <summary>
    /// Palette reused across solvers, expressed as linear float4 colors. Implemented as properties
    /// (not static fields) so the values are fully Burst-compatible when read inside jobs.
    /// </summary>
    public static class MazePalette
    {
        public static float4 Floor => new float4(0.05f, 0.05f, 0.05f, 1f);  // near black
        public static float4 Start => new float4(0.15f, 0.85f, 0.20f, 1f);  // green
        public static float4 End => new float4(0.90f, 0.15f, 0.15f, 1f);    // red
        public static float4 Visited => new float4(0.15f, 0.35f, 0.95f, 1f);// blue
        public static float4 Path => new float4(0.20f, 0.95f, 0.30f, 1f);   // bright green
        public static float4 Filled => new float4(0.10f, 0.15f, 0.45f, 1f); // dark blue (dead-ends)
        public static float4 Wall => new float4(0.6f, 0.6f, 0.62f, 1f);     // light grey
    }
}
