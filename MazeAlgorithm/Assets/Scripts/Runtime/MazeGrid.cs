using Unity.Burst;
using Unity.Collections;

namespace MazeDOTS
{
    /// <summary>
    /// Pure, Burst-friendly helpers describing the maze grid topology.
    ///
    /// Index convention (matches the original 2018 project):
    ///   index = y * Width + x        x in [0,Width)   y in [0,Height)
    ///   North neighbour = index + Width      (y + 1)
    ///   South neighbour = index - Width      (y - 1)
    ///   East  neighbour = index + 1          (x + 1)
    ///   West  neighbour = index - 1          (x - 1)
    ///   Start cell = Width * (Height - 1)    (top-left)
    ///   End   cell = Width - 1               (bottom-right)
    /// </summary>
    [BurstCompile]
    public static class MazeGrid
    {
        public static int Index(int x, int y, int width) => y * width + x;
        public static int X(int index, int width) => index % width;
        public static int Y(int index, int width) => index / width;

        public static int Start(int width, int height) => width * (height - 1);
        public static int End(int width) => width - 1;

        public static bool HasNorth(int index, int width, int total) => index + width < total;
        public static bool HasSouth(int index, int width) => index - width >= 0;
        public static bool HasEast(int index, int width) => (index % width) < width - 1;
        public static bool HasWest(int index, int width) => (index % width) > 0;

        /// <summary>Returns the neighbour index in a given direction, or -1 if out of bounds.</summary>
        public static int Neighbour(int index, Dir dir, int width, int total)
        {
            switch (dir)
            {
                case Dir.North: return HasNorth(index, width, total) ? index + width : -1;
                case Dir.South: return HasSouth(index, width) ? index - width : -1;
                case Dir.East: return HasEast(index, width) ? index + 1 : -1;
                case Dir.West: return HasWest(index, width) ? index - 1 : -1;
            }
            return -1;
        }

        public static Wall WallOf(Dir dir)
        {
            switch (dir)
            {
                case Dir.North: return Wall.North;
                case Dir.South: return Wall.South;
                case Dir.East: return Wall.East;
                default: return Wall.West;
            }
        }

        public static Dir Opposite(Dir dir)
        {
            switch (dir)
            {
                case Dir.North: return Dir.South;
                case Dir.South: return Dir.North;
                case Dir.East: return Dir.West;
                default: return Dir.East;
            }
        }

        /// <summary>True when the wall between the cell and the given direction is open (no wall).</summary>
        public static bool IsOpen(in NativeArray<byte> walls, int index, Dir dir)
        {
            return (walls[index] & (byte)WallOf(dir)) == 0;
        }

        /// <summary>Carve a passage from <paramref name="index"/> towards <paramref name="dir"/>, clearing both shared walls.</summary>
        public static void Carve(NativeArray<byte> walls, int index, Dir dir, int width, int total)
        {
            int n = Neighbour(index, dir, width, total);
            if (n < 0) return;
            walls[index] = (byte)(walls[index] & ~(byte)WallOf(dir));
            Dir opp = Opposite(dir);
            walls[n] = (byte)(walls[n] & ~(byte)WallOf(opp));
        }

        // ---- Wall-entity addressing (used by the renderer) -------------------
        // Vertical walls live on the (Width+1) vertical grid lines, Height each.
        // Horizontal walls live on the (Height+1) horizontal grid lines, Width each.

        public static int VerticalWallCount(int width, int height) => (width + 1) * height;
        public static int HorizontalWallCount(int width, int height) => width * (height + 1);

        public static int VerticalWallId(int lineX, int y, int width) => y * (width + 1) + lineX;
        public static int HorizontalWallId(int x, int lineY, int width) => lineY * width + x;

        /// <summary>Maps a (cell, direction) pair to a canonical wall id. isVertical distinguishes the two arrays.</summary>
        public static int WallIdFor(int index, Dir dir, int width, out bool isVertical)
        {
            int x = index % width;
            int y = index / width;
            switch (dir)
            {
                case Dir.West: isVertical = true; return VerticalWallId(x, y, width);
                case Dir.East: isVertical = true; return VerticalWallId(x + 1, y, width);
                case Dir.South: isVertical = false; return HorizontalWallId(x, y, width);
                default: isVertical = false; return HorizontalWallId(x, y + 1, width); // North
            }
        }
    }
}
