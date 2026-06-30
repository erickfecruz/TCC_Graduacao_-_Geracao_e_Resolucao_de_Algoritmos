# CLAUDE.md — Maze Generation & Solving (Unity 6 LTS / DOTS)

Guidance for future Claude/AI sessions working in this repository. Read this fully before editing.

## What this project is

A modern **Unity 6 LTS (6000.0.78f1)** re-implementation of an undergraduate-thesis (TCC) project that
**generates** and **solves** mazes and visualizes the algorithms step by step. The original was a Unity
2018 project built entirely from `MonoBehaviour`s that instantiated thousands of `GameObject` walls and
drove everything from coroutines. This version rebuilds the same feature set on **DOTS / ECS** so it scales
to large grids: the algorithms run as **Burst-compiled jobs** and the maze is drawn with **Entities Graphics
instancing** (one entity per floor/wall, GPU-instanced).

The folder is the same git repository as the original (`MazeAlgorithm/` inside
`TCC_Graduacao_-_Geracao_e_Resolucao_de_Algoritmos`). The legacy scripts/scene/prefabs were removed.

## Algorithms (parity with the original)

Generators (`Assets/Scripts/Runtime/Generators.cs`):
- **Binary Tree** (`BinaryTreeJob`) — North/West biased, same rule set as the original `ArvoreBinaria`.
- **Recursive Backtracker** (`RecursiveBacktrackerJob`) — DFS stack, same as `BacktrackingAlgorithm`.
- **Eller** (`EllerJob`) — row-by-row set merging, same as `EllerAlgorithm`.

Solvers (`Assets/Scripts/Runtime/Solvers.cs`):
- **Wall Follower** left & right hand (`WallFollowerJob`, `rightHand` flag) — original `AlwaysLeft`/`AlwaysRight`.
- **Dead-end Filling** (`DeadEndFillJob`) — original `DeadWay`.
- **Flood Fill / BFS** (`FloodFillJob`) — original `FloodFill`; labels distances then back-tracks the shortest path.

Each generator is `[BurstCompile]` and writes the final wall bitmask; solvers write a list of *steps*
(`SolveStep`) so the solution is "played back" one step per `stepInterval` seconds, reproducing the original
coroutine animation while keeping the heavy compute off the main thread.

### Scalability (large mazes)
The pipeline is built to take large sizes without freezing the editor:
- **Generation runs asynchronously** — the generator job is `Schedule()`d and completed across frames in
  `OnUpdate` (never `.Run()` on the main thread).
- **Requested size is clamped with 64-bit math** to `MazeOrchestratorSystem.MaxCells` (default 500k cells), so a
  request like 100000x100000 cannot overflow the 32-bit `NativeArray` index or allocate tens of GB. The clamp is
  reported to the UI via `MazeStatus.Text`. Raise/lower `MaxCells` to trade scale vs. framerate.
- **Entities are built in bulk** with a prototype + `EntityManager.Instantiate(prototype, array)` rather than
  per-cell `CreateEntity`, and only walls that exist in the *final* maze become entities (no build-all-then-destroy).
  Generation is therefore instant/final-state (no per-wall carve animation); the solve animation remains.
- Reminder: 100000x100000 = 10^10 cells is physically impossible to store (10 GB) or render (~20e9 objects).
  `MaxCells` is the honest ceiling for a fully stored, rendered, solvable maze.

## Architecture / data flow

```
MazeBootstrap (MonoBehaviour, the only one)         Scene: Assets/Scenes/Maze.unity
  builds runtime uGUI, enqueues MazeCommand  ─┐
                                              ▼
MazeCommands (static queue)  ──►  MazeOrchestratorSystem (SystemBase, default world)
                                       │  schedules Burst jobs (generate / solve)
                                       │  owns all render entities (floors + walls)
                                       │  plays back carve/solve steps over time
                                       ▼
                              Entities Graphics  →  GPU-instanced floors & walls
```

Key files:
- `MazeTypes.cs` — `Wall` bitmask, `Dir`, algorithm enums, `MazePalette` colors.
- `MazeGrid.cs` — Burst-friendly topology helpers and the canonical **wall-id addressing** used by the renderer.
- `Generators.cs` / `Solvers.cs` — the six algorithms as `IJob`s.
- `MazeOrchestratorSystem.cs` — the brain: jobs + entity construction + playback.
- `MazeRuntime.cs` — command queue, shared `MazeResources` (procedural meshes + URP materials), `ProceduralMesh`.
- `MazeBootstrap.cs` — runtime UI + camera framing; forwards input as commands.
- `Editor/MazeProjectSetup.cs` — headless URP + scene configuration (see below).

### Grid conventions (must stay consistent across all files)
- `index = y * width + x`; North `+width`, South `-width`, East `+1`, West `-1`.
- Start cell = `width * (height - 1)` (top-left); End cell = `width - 1` (bottom-right).
- `Wall` bit **set = wall present**. Carving clears the bit on **both** adjacent cells.

## Rendering model
- Floors: one entity per cell, mesh index 0, material = floor (per-instance `URPMaterialPropertyBaseColor`).
- Walls: one entity per grid-line segment. Vertical walls live on `(width+1)*height` lines, horizontal on
  `width*(height+1)`. `MazeGrid.WallIdFor` maps a `(cell, Dir)` to a canonical id + array; generation playback
  destroys those wall entities over time. All walls are built first, then carved away → final maze.
- Meshes/materials are generated at runtime (`ProceduralMesh.Box`, URP/Lit) — **no asset authoring required**,
  which is what lets the project be configured fully headless.

## How to build / verify (headless, no GUI needed)

Editor: `C:\Program Files\Unity\Hub\Editor\6000.0.78f1\Editor\Unity.exe`

1. **Compile + resolve packages**:
   ```
   Unity.exe -batchmode -quit -nographics \
     -projectPath "<repo>\MazeAlgorithm" -logFile compile.log
   ```
   Check `compile.log` for `error CS`, `cannot be found` (bad package version), or `Compilation failed`.

2. **Configure URP + build the scene** (run after a clean compile):
   ```
   Unity.exe -batchmode -quit -nographics \
     -projectPath "<repo>\MazeAlgorithm" \
     -executeMethod MazeDOTS.EditorTools.MazeProjectSetup.ConfigureProject -logFile setup.log
   ```
   This creates `Assets/Settings/MazeURP.asset`, assigns it, and writes `Assets/Scenes/Maze.unity`.

Always close any Editor instance before launching batchmode (Unity locks the project: `-projectPath` will
fail with "another Unity instance is running"). Delete `Library/` only if you need a clean reimport.

## Packages (`Packages/manifest.json`)
DOTS stack: `com.unity.entities`, `com.unity.entities.graphics` (pulls Burst/Collections/Mathematics/Transforms),
rendering: `com.unity.render-pipelines.universal` (required by Entities Graphics), UI: `com.unity.ugui`.
If package resolution fails on a pinned version, read the error in the log and adjust to a version available
for 6000.0 — do not guess blindly; the log names the closest valid versions.

## Unity MCP (for future AI-driven iterations)

The **MCP For Unity** bridge (`com.coplaydev.unity-mcp`, pinned by git URL in `manifest.json`) is installed so a
future Claude session can drive the running Editor (create objects, run menu items, read console, etc.). The
**Unity side** is fully set up — the package compiles (`MCPForUnity.Editor` / `MCPForUnity.Runtime` assemblies)
and the bridge auto-starts a listener when the Editor is opened interactively (`Window ▸ MCP For Unity`).

To finish wiring the **client side** (only needed when you actually want to use it):
1. Open the project once in the Editor so the bridge starts (it listens on a local TCP port).
2. In `Window ▸ MCP For Unity`, use the auto-setup to register the MCP server with your client, **or** add the
   server manually to your MCP config. The Python server lives in the package's `Server/` folder and is run with
   `uv` (install [uv](https://docs.astral.sh/uv/) first). Typical client entry:
   ```json
   { "mcpServers": { "unity": { "command": "uvx", "args": ["mcp-for-unity"] } } }
   ```
3. Confirm the client shows the `unity` tools connected, then drive the Editor through them.

The bridge is inert in batchmode and harmless if the client side is never configured — it does not affect the
build. If you do not want it, remove the `com.coplaydev.unity-mcp` line from `manifest.json`.

## Conventions for future work
- Keep all maze compute in Burst jobs; never reintroduce per-cell `GameObject`/`GetComponent` work.
- Any new algorithm = a new `IJob` + an enum value + a button in `MazeBootstrap` + a case in the orchestrator.
- Maintain the grid conventions above; they are shared by jobs and the renderer.
- The project must compile in batchmode before committing. **Do not commit unless explicitly asked.**
- Portuguese identifiers from the original (`celulas`, `norte/sul/leste/oeste`) were intentionally replaced
  with English domain terms; the algorithm semantics are preserved and cross-referenced in code comments.
