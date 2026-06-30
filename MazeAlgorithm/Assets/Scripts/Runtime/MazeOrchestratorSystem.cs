using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace MazeDOTS
{
    /// <summary>
    /// Central managed system that turns UI commands into work. The heavy maze compute runs in
    /// Burst jobs scheduled OFF the main thread (so the editor never freezes), and the maze is drawn
    /// with Entities Graphics instancing built via bulk <see cref="EntityManager.Instantiate"/>.
    ///
    /// Scalability guarantees:
    ///  - Requested sizes are clamped (with 64-bit math) to <see cref="MaxCells"/> so a giant request
    ///    like 100000x100000 can never overflow the 32-bit array index nor allocate tens of GB.
    ///  - Generation is asynchronous: the job is scheduled and completed across frames.
    ///  - Only walls that actually exist in the final maze become entities (no "build all then destroy").
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default)]
    public partial class MazeOrchestratorSystem : SystemBase
    {
        // ---- view constants ----
        public const float CellSize = 1f;
        public const float WallThickness = 0.12f;
        public const float WallHeight = 1f;
        public const float FloorHeight = 0.1f;

        // Upper bound on cells that are generated AND rendered. Keeps memory and entity counts
        // feasible (≈ 2x this many entities). Tunable; the limit is communicated to the user on clamp.
        public const int MaxCells = 500_000;

        // ---- maze state ----
        int width, height, total;
        float3 origin;
        NativeArray<byte> walls;

        // ---- render entities ----
        NativeArray<Entity> floorEntities;   // indexed by cell, used for solve coloring
        NativeList<Entity> wallEntities;      // present-wall instances, kept only for cleanup
        bool hasMaze;

        // ---- generation (async) ----
        JobHandle genHandle;
        bool genRunning;
        GenAlgorithm pendingGen;

        // ---- solve playback ----
        NativeList<SolveStep> solveSteps;
        int solveIndex;
        float stepInterval;
        float timer;
        MazePhase phase;

        // ---- rendering ----
        RenderMeshArray renderMeshArray;
        RenderMeshDescription renderDesc;
        bool renderReady;

        public MazePhase Phase => phase;

        protected override void OnCreate()
        {
            solveSteps = new NativeList<SolveStep>(256, Allocator.Persistent);
            phase = MazePhase.Idle;
            RequireForUpdate<MazeBootstrapTag>();
        }

        protected override void OnDestroy()
        {
            if (genRunning) genHandle.Complete();
            if (solveSteps.IsCreated) solveSteps.Dispose();
            if (walls.IsCreated) walls.Dispose();
            if (floorEntities.IsCreated) floorEntities.Dispose();
            if (wallEntities.IsCreated) wallEntities.Dispose();
        }

        protected override void OnUpdate()
        {
            // Finish an in-flight generation once its job reports done (non-blocking).
            if (genRunning && genHandle.IsCompleted)
            {
                genHandle.Complete();
                genRunning = false;
                BuildEntities();
                FrameCamera();
                phase = MazePhase.Ready;
                MazeStatus.Text = $"Ready: {width}x{height} ({total:N0} cells)";
            }

            while (MazeCommands.TryDequeue(out var cmd))
                Handle(cmd);

            AdvancePlayback(SystemAPI.Time.DeltaTime);
        }

        // ------------------------------------------------------------------ commands

        void Handle(MazeCommand cmd)
        {
            switch (cmd.kind)
            {
                case MazeCommand.Kind.Generate: Generate(cmd); break;
                case MazeCommand.Kind.Solve: Solve(cmd.solve); break;
                case MazeCommand.Kind.ResetSolution:
                    ResetFloors();
                    phase = hasMaze ? MazePhase.Ready : MazePhase.Idle;
                    MazeStatus.Text = "Solution cleared";
                    break;
                case MazeCommand.Kind.Clear: DestroyMaze(); phase = MazePhase.Idle; MazeStatus.Text = "Cleared"; break;
            }
        }

        void Generate(MazeCommand cmd)
        {
            EnsureRendering();
            DestroyMaze();

            // 64-bit clamp so width*height can never overflow int nor blow past the feasible budget.
            long reqW = math.max(1, cmd.width);
            long reqH = math.max(1, cmd.height);
            long reqCells = reqW * reqH;
            bool clamped = false;
            if (reqCells > MaxCells)
            {
                double scale = math.sqrt(MaxCells / (double)reqCells);
                reqW = math.max(1, (long)(reqW * scale));
                reqH = math.max(1, (long)(reqH * scale));
                clamped = true;
            }

            width = (int)reqW;
            height = (int)reqH;
            total = width * height;
            origin = new float3(-width * 0.5f, 0f, -height * 0.5f) * CellSize;
            stepInterval = math.max(0f, cmd.stepInterval);

            walls = new NativeArray<byte>(total, Allocator.Persistent);
            for (int i = 0; i < total; i++) walls[i] = (byte)Wall.All;

            pendingGen = cmd.gen;
            switch (cmd.gen)
            {
                case GenAlgorithm.BinaryTree:
                    genHandle = new BinaryTreeJob { width = width, height = height, seed = cmd.seed, walls = walls }.Schedule();
                    break;
                case GenAlgorithm.RecursiveBacktracker:
                    genHandle = new RecursiveBacktrackerJob { width = width, height = height, seed = cmd.seed, walls = walls }.Schedule();
                    break;
                case GenAlgorithm.Eller:
                    genHandle = new EllerJob { width = width, height = height, seed = cmd.seed, walls = walls }.Schedule();
                    break;
            }
            genRunning = true;
            phase = MazePhase.Generating;
            hasMaze = true;

            string note = clamped ? $"clamped from {cmd.width}x{cmd.height} (max {MaxCells:N0} cells)" : "";
            MazeStatus.Text = $"Generating {cmd.gen} {width}x{height}... {note}";
        }

        void Solve(SolveAlgorithm algo)
        {
            if (!hasMaze || phase == MazePhase.Generating) return;
            ResetFloors();
            solveSteps.Clear();

            switch (algo)
            {
                case SolveAlgorithm.WallFollowerLeft:
                    new WallFollowerJob { width = width, height = height, rightHand = false, walls = walls, steps = solveSteps }.Run();
                    break;
                case SolveAlgorithm.WallFollowerRight:
                    new WallFollowerJob { width = width, height = height, rightHand = true, walls = walls, steps = solveSteps }.Run();
                    break;
                case SolveAlgorithm.DeadEndFilling:
                    new DeadEndFillJob { width = width, height = height, walls = walls, steps = solveSteps }.Run();
                    break;
                case SolveAlgorithm.FloodFill:
                    new FloodFillJob { width = width, height = height, walls = walls, steps = solveSteps }.Run();
                    break;
            }

            solveIndex = 0;
            timer = 0f;
            phase = MazePhase.SolvePlayback;
            MazeStatus.Text = $"Solving: {algo} ({solveSteps.Length:N0} steps)";
        }

        // ------------------------------------------------------------------ solve playback

        void AdvancePlayback(float dt)
        {
            if (phase != MazePhase.SolvePlayback) return;

            if (stepInterval <= 1e-4f)
            {
                while (solveIndex < solveSteps.Length) ApplySolveStep(solveSteps[solveIndex++]);
            }
            else
            {
                timer += dt;
                while (timer >= stepInterval && solveIndex < solveSteps.Length)
                {
                    ApplySolveStep(solveSteps[solveIndex++]);
                    timer -= stepInterval;
                }
            }

            if (solveIndex >= solveSteps.Length)
            {
                phase = MazePhase.Done;
                MazeStatus.Text = "Solved";
            }
        }

        void ApplySolveStep(SolveStep s)
        {
            if (s.cell < 0 || s.cell >= floorEntities.Length) return;
            Entity e = floorEntities[s.cell];
            if (e != Entity.Null && EntityManager.Exists(e))
                EntityManager.SetComponentData(e, new URPMaterialPropertyBaseColor { Value = s.color });
        }

        void ResetFloors()
        {
            if (!floorEntities.IsCreated) return;
            for (int i = 0; i < floorEntities.Length; i++)
            {
                Entity e = floorEntities[i];
                if (e != Entity.Null && EntityManager.Exists(e))
                    EntityManager.SetComponentData(e, new URPMaterialPropertyBaseColor { Value = MazePalette.Floor });
            }
        }

        // ------------------------------------------------------------------ entity construction

        void EnsureRendering()
        {
            MazeResources.EnsureBuilt(CellSize, WallThickness, WallHeight, FloorHeight);
            if (renderReady) return;

            renderDesc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);
            renderMeshArray = new RenderMeshArray(
                new Material[] { MazeResources.WallMaterial, MazeResources.FloorMaterial },
                new Mesh[] { MazeResources.FloorMesh, MazeResources.VerticalWallMesh, MazeResources.HorizontalWallMesh });
            renderReady = true;
        }

        // material indices: 0 = wall, 1 = floor   |   mesh indices: 0 = floor, 1 = vWall, 2 = hWall
        Entity MakePrototype(int materialIndex, int meshIndex, bool withColor)
        {
            Entity e = EntityManager.CreateEntity();
            RenderMeshUtility.AddComponents(e, EntityManager, renderDesc, renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(materialIndex, meshIndex));
            SetOrAdd(e, LocalTransform.FromPosition(float3.zero));
            SetOrAdd(e, new LocalToWorld { Value = float4x4.identity });
            if (withColor) SetOrAdd(e, new URPMaterialPropertyBaseColor { Value = MazePalette.Floor });
            return e;
        }

        void BuildEntities()
        {
            // --- floors: one bulk Instantiate, then per-instance position ---
            floorEntities = new NativeArray<Entity>(total, Allocator.Persistent);
            Entity floorProto = MakePrototype(1, 0, true);
            EntityManager.Instantiate(floorProto, floorEntities);
            EntityManager.DestroyEntity(floorProto);
            for (int i = 0; i < total; i++)
            {
                int x = i % width, y = i / width;
                float3 p = origin + new float3((x + 0.5f) * CellSize, 0f, (y + 0.5f) * CellSize);
                EntityManager.SetComponentData(floorEntities[i], LocalTransform.FromPosition(p));
            }

            // --- walls: gather only the walls that exist in the final maze, then bulk Instantiate ---
            var vPos = new NativeList<float3>(total, Allocator.TempJob);
            var hPos = new NativeList<float3>(total, Allocator.TempJob);
            for (int i = 0; i < total; i++)
            {
                int x = i % width, y = i / width;
                byte w = walls[i];
                if ((w & (byte)Wall.West) != 0)
                    vPos.Add(origin + new float3(x * CellSize, WallHeight * 0.5f, (y + 0.5f) * CellSize));
                if ((w & (byte)Wall.South) != 0)
                    hPos.Add(origin + new float3((x + 0.5f) * CellSize, WallHeight * 0.5f, y * CellSize));
                if (x == width - 1 && (w & (byte)Wall.East) != 0)
                    vPos.Add(origin + new float3((x + 1) * CellSize, WallHeight * 0.5f, (y + 0.5f) * CellSize));
                if (y == height - 1 && (w & (byte)Wall.North) != 0)
                    hPos.Add(origin + new float3((x + 0.5f) * CellSize, WallHeight * 0.5f, (y + 1) * CellSize));
            }

            wallEntities = new NativeList<Entity>(vPos.Length + hPos.Length, Allocator.Persistent);
            InstantiateWalls(vPos, 0, 1);
            InstantiateWalls(hPos, 0, 2);
            vPos.Dispose();
            hPos.Dispose();
        }

        void InstantiateWalls(NativeList<float3> positions, int materialIndex, int meshIndex)
        {
            if (positions.Length == 0) return;
            Entity proto = MakePrototype(materialIndex, meshIndex, false);
            var arr = new NativeArray<Entity>(positions.Length, Allocator.TempJob);
            EntityManager.Instantiate(proto, arr);
            EntityManager.DestroyEntity(proto);
            for (int i = 0; i < arr.Length; i++)
            {
                EntityManager.SetComponentData(arr[i], LocalTransform.FromPosition(positions[i]));
                wallEntities.Add(arr[i]);
            }
            arr.Dispose();
        }

        void SetOrAdd<T>(Entity e, T value) where T : unmanaged, IComponentData
        {
            if (EntityManager.HasComponent<T>(e)) EntityManager.SetComponentData(e, value);
            else EntityManager.AddComponentData(e, value);
        }

        void DestroyMaze()
        {
            if (genRunning) { genHandle.Complete(); genRunning = false; }

            if (floorEntities.IsCreated)
            {
                EntityManager.DestroyEntity(floorEntities);
                floorEntities.Dispose();
            }
            if (wallEntities.IsCreated)
            {
                EntityManager.DestroyEntity(wallEntities.AsArray());
                wallEntities.Dispose();
            }
            if (walls.IsCreated) walls.Dispose();
            hasMaze = false;
        }

        void FrameCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = new float3(0f, 50f, 0f);
            if (cam.orthographic)
                cam.orthographicSize = math.max(width, height) * 0.5f * CellSize + 2f;
        }
    }

    /// <summary>Tag added by the scene bootstrap so the orchestrator only runs inside a live maze scene.</summary>
    public struct MazeBootstrapTag : IComponentData { }
}
