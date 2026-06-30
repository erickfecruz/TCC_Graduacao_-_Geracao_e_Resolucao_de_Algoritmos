using System.Collections.Generic;
using UnityEngine;

namespace MazeDOTS
{
    /// <summary>A single request flowing from the UI (MonoBehaviour world) to the ECS orchestrator.</summary>
    public struct MazeCommand
    {
        public enum Kind : byte { Generate, Solve, ResetSolution, Clear }

        public Kind kind;
        public int width;
        public int height;
        public float stepInterval;
        public uint seed;
        public GenAlgorithm gen;
        public SolveAlgorithm solve;
    }

    /// <summary>
    /// Lock-light hand-off channel between the UI thread (always Unity main thread) and the
    /// orchestrator system. Both live on the main thread, so a plain queue is sufficient.
    /// </summary>
    public static class MazeCommands
    {
        static readonly Queue<MazeCommand> Pending = new Queue<MazeCommand>();

        public static void Enqueue(MazeCommand cmd) => Pending.Enqueue(cmd);
        public static bool TryDequeue(out MazeCommand cmd)
        {
            if (Pending.Count > 0) { cmd = Pending.Dequeue(); return true; }
            cmd = default;
            return false;
        }
        public static void Clear() => Pending.Clear();
    }

    /// <summary>Status text published by the orchestrator (main thread) and shown by the UI.</summary>
    public static class MazeStatus
    {
        public static string Text = "Ready";
    }

    /// <summary>
    /// Shared rendering resources (meshes + materials) created once by the bootstrap and consumed
    /// by the orchestrator when it spawns the floor/wall entities.
    /// </summary>
    public static class MazeResources
    {
        public static Mesh FloorMesh;
        public static Mesh VerticalWallMesh;
        public static Mesh HorizontalWallMesh;
        public static Material FloorMaterial;
        public static Material WallMaterial;
        public static bool Ready;

        public static void EnsureBuilt(float cellSize, float wallThickness, float wallHeight, float floorHeight)
        {
            if (Ready) return;

            FloorMesh = ProceduralMesh.Box(cellSize, floorHeight, cellSize);
            VerticalWallMesh = ProceduralMesh.Box(wallThickness, wallHeight, cellSize + wallThickness);
            HorizontalWallMesh = ProceduralMesh.Box(cellSize + wallThickness, wallHeight, wallThickness);

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) urpLit = Shader.Find("Standard"); // fallback so the project still runs

            FloorMaterial = new Material(urpLit) { enableInstancing = true };
            WallMaterial = new Material(urpLit) { enableInstancing = true };
            WallMaterial.color = new Color(0.6f, 0.6f, 0.62f);

            Ready = true;
        }
    }

    /// <summary>Generates simple axis-aligned box meshes at runtime (no asset authoring required).</summary>
    public static class ProceduralMesh
    {
        public static Mesh Box(float sx, float sy, float sz)
        {
            float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;

            Vector3[] v =
            {
                // -X
                new Vector3(-hx,-hy,-hz), new Vector3(-hx,-hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, hy,-hz),
                // +X
                new Vector3( hx,-hy, hz), new Vector3( hx,-hy,-hz), new Vector3( hx, hy,-hz), new Vector3( hx, hy, hz),
                // -Y
                new Vector3(-hx,-hy,-hz), new Vector3( hx,-hy,-hz), new Vector3( hx,-hy, hz), new Vector3(-hx,-hy, hz),
                // +Y
                new Vector3(-hx, hy, hz), new Vector3( hx, hy, hz), new Vector3( hx, hy,-hz), new Vector3(-hx, hy,-hz),
                // -Z
                new Vector3( hx,-hy,-hz), new Vector3(-hx,-hy,-hz), new Vector3(-hx, hy,-hz), new Vector3( hx, hy,-hz),
                // +Z
                new Vector3(-hx,-hy, hz), new Vector3( hx,-hy, hz), new Vector3( hx, hy, hz), new Vector3(-hx, hy, hz),
            };

            Vector3[] n =
            {
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            };

            int[] t = new int[36];
            for (int f = 0; f < 6; f++)
            {
                int o = f * 4;
                int ti = f * 6;
                t[ti + 0] = o + 0; t[ti + 1] = o + 1; t[ti + 2] = o + 2;
                t[ti + 3] = o + 0; t[ti + 4] = o + 2; t[ti + 5] = o + 3;
            }

            var mesh = new Mesh { name = "MazeBox" };
            mesh.vertices = v;
            mesh.normals = n;
            mesh.triangles = t;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
