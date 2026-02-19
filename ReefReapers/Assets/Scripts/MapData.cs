using UnityEngine;

public enum DepthZone { Shallow, Mid, Deep }

public class MapCell
{
    public bool isWall;
    public float height;       // world-space Y of the floor surface
    public float wallHeight;   // how tall the wall rises above floor
    public DepthZone zone;
}

public static class MapData
{
    public static int Width;
    public static int Height;
    public static MapCell[,] Cells;

    public static MapCell Get(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        return Cells[x, y];
    }

    public static bool IsWall(int x, int y)
    {
        var c = Get(x, y);
        return c == null || c.isWall;
    }
}