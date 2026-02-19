using UnityEngine;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    [Header("Map Size")]
    public int width = 200;
    public int height = 200;

    [Header("Noise")]
    public float noiseScale = 40f;
    public int octaves = 4;
    public float persistence = 0.5f;
    public float lacunarity = 2f;
    public int seed = 42;

    [Header("Corridors")]
    public int corridorWalkers = 20;
    public int walkerSteps = 800;
    public int minBrushSize = 1;
    public int maxBrushSize = 4;

    [Header("Floor Heights")]
    public float shallowHeight = 0f;
    public float midHeight = -3f;
    public float deepHeight = -6f;
    public int heightSmoothPasses = 12;

    [Header("Walls")]
    public float wallRise = 10f;
    public float perimeterWallHeight = 20f;
    public float deepThreshold = 0.35f;
    public float midThreshold = 0.65f;

    [Header("Spike Removal")]
    public int minWallNeighbors = 5;
    public int erosionPasses = 5;

    public void Generate()
    {
        MapData.Width  = width;
        MapData.Height = height;
        MapData.Cells  = new MapCell[width, height];

        float[,] noiseMap = GenerateNoiseMap();
        bool[,]  carved   = CarveCorridors();

        // Erosion passes to clean up thin walls
        carved = ErodeWalls(carved, erosionPasses);
        carved = ErodeWallColumns(carved);

        // Flood fill from center — kill any floor not connected to main area
        carved = RemoveIsolatedFloor(carved);

        // Run column erosion again after flood fill in case new spikes exposed
        carved = ErodeWallColumns(carved);

        float[,] floorHeights = new float[width, height];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            if (!carved[x, y]) { floorHeights[x, y] = shallowHeight; continue; }
            float n = noiseMap[x, y];
            floorHeights[x, y] = n < deepThreshold ? deepHeight
                                : n < midThreshold  ? midHeight
                                : shallowHeight;
        }

        floorHeights = SmoothHeights(floorHeights, carved, heightSmoothPasses);

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var cell     = new MapCell();
            bool isPerim = x==0 || y==0 || x==width-1 || y==height-1;
            cell.isWall  = isPerim || !carved[x, y];

            float n = noiseMap[x, y];
            cell.zone = n < deepThreshold ? DepthZone.Deep
                      : n < midThreshold  ? DepthZone.Mid
                      : DepthZone.Shallow;

            if (!cell.isWall)
            {
                cell.height     = floorHeights[x, y];
                cell.wallHeight = 0f;
            }
            else
            {
                cell.height     = deepHeight - 2f;
                cell.wallHeight = isPerim
                    ? perimeterWallHeight
                    : wallRise + Mathf.Abs(deepHeight) + 2f;
            }

            MapData.Cells[x, y] = cell;
        }

        Object.FindFirstObjectByType<MeshBuilder>()?.Build();
    }

    // Flood fill from the first floor cell near center
    // Any floor cell not reachable = isolated pocket = convert to wall
    bool[,] RemoveIsolatedFloor(bool[,] carved)
    {
        bool[,] result  = (bool[,])carved.Clone();
        bool[,] visited = new bool[width, height];

        // Find a guaranteed floor cell near center to start flood fill
        int startX = -1, startY = -1;
        for (int r = 0; r < Mathf.Max(width, height) && startX == -1; r++)
        {
            for (int dx = -r; dx <= r && startX == -1; dx++)
            for (int dy = -r; dy <= r && startX == -1; dy++)
            {
                int cx = width/2 + dx, cy = height/2 + dy;
                if (cx < 0 || cy < 0 || cx >= width || cy >= height) continue;
                if (result[cx, cy]) { startX = cx; startY = cy; }
            }
        }

        if (startX == -1) return result; // no floor found, bail

        // BFS flood fill marking all reachable floor cells
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int x = cur.x, y = cur.y;

            // Check 4 cardinal neighbors
            TryEnqueue(result, visited, queue, x+1, y);
            TryEnqueue(result, visited, queue, x-1, y);
            TryEnqueue(result, visited, queue, x, y+1);
            TryEnqueue(result, visited, queue, x, y-1);
        }

        // Any floor cell not visited is isolated — convert to wall
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (result[x, y] && !visited[x, y])
                result[x, y] = false;

        return result;
    }

    void TryEnqueue(bool[,] carved, bool[,] visited, Queue<Vector2Int> queue, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        if (!carved[x, y]) return;   // wall
        if (visited[x, y]) return;   // already seen
        visited[x, y] = true;
        queue.Enqueue(new Vector2Int(x, y));
    }

    bool[,] ErodeWalls(bool[,] carved, int passes)
    {
        bool[,] result = (bool[,])carved.Clone();
        for (int p = 0; p < passes; p++)
        {
            bool[,] next = (bool[,])result.Clone();
            for (int x = 1; x < width-1; x++)
            for (int y = 1; y < height-1; y++)
            {
                if (result[x, y]) continue;
                int wallNeighbors = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx==0 && dy==0) continue;
                    if (!result[x+dx, y+dy]) wallNeighbors++;
                }
                if (wallNeighbors < minWallNeighbors)
                    next[x, y] = true;
            }
            result = next;
        }
        return result;
    }

    bool[,] ErodeWallColumns(bool[,] carved)
    {
        bool[,] result = (bool[,])carved.Clone();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int x = 1; x < width-1; x++)
            for (int y = 1; y < height-1; y++)
            {
                if (result[x, y]) continue;
                int cardinalWalls = 0;
                if (!result[x+1, y]) cardinalWalls++;
                if (!result[x-1, y]) cardinalWalls++;
                if (!result[x, y+1]) cardinalWalls++;
                if (!result[x, y-1]) cardinalWalls++;
                if (cardinalWalls < 2)
                {
                    result[x, y] = true;
                    changed = true;
                }
            }
        }
        return result;
    }

    float[,] SmoothHeights(float[,] heights, bool[,] carved, int passes)
    {
        float[,] result = heights;
        for (int p = 0; p < passes; p++)
        {
            float[,] next = new float[width, height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (!carved[x, y]) { next[x, y] = result[x, y]; continue; }
                float sum = 0; int count = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x+dx, ny = y+dy;
                    if (nx<0||ny<0||nx>=width||ny>=height) continue;
                    sum += result[nx, ny]; count++;
                }
                next[x, y] = sum / count;
            }
            result = next;
        }
        return result;
    }

    void Start() => Generate();

    float[,] GenerateNoiseMap()
    {
        var map  = new float[width, height];
        var rng  = new System.Random(seed);
        var offs = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
            offs[i] = new Vector2(rng.Next(-10000,10000), rng.Next(-10000,10000));

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            float amp=1, freq=1, val=0;
            for (int o = 0; o < octaves; o++)
            {
                val  += Mathf.PerlinNoise((x+offs[o].x)/noiseScale*freq,
                                          (y+offs[o].y)/noiseScale*freq) * amp;
                amp  *= persistence; freq *= lacunarity;
            }
            map[x,y] = Mathf.Clamp01(val/octaves);
        }
        return map;
    }

    bool[,] CarveCorridors()
    {
        var carved = new bool[width, height];
        var rng    = new System.Random(seed);
        for (int w = 0; w < corridorWalkers; w++)
        {
            int cx = rng.Next(5, width-5), cy = rng.Next(5, height-5);
            for (int step = 0; step < walkerSteps; step++)
            {
                float t = (float)step/walkerSteps;
                int brush = Mathf.RoundToInt(Mathf.Lerp(minBrushSize, maxBrushSize,
                    Mathf.Sin(t*Mathf.PI*6f+w)*0.5f+0.5f));
                CarveCircle(carved, cx, cy, brush);
                int dir = rng.Next(4);
                cx = Mathf.Clamp(cx+(dir==0?1:dir==1?-1:0), 2, width-3);
                cy = Mathf.Clamp(cy+(dir==2?1:dir==3?-1:0), 2, height-3);
            }
        }
        return carved;
    }

    void CarveCircle(bool[,] carved, int cx, int cy, int radius)
    {
        for (int dx=-radius; dx<=radius; dx++)
        for (int dy=-radius; dy<=radius; dy++)
            if (dx*dx+dy*dy<=radius*radius)
                carved[Mathf.Clamp(cx+dx,1,width-2),
                       Mathf.Clamp(cy+dy,1,height-2)] = true;
    }
}
// using UnityEngine;
// using System.Collections.Generic;

// public class MapGenerator : MonoBehaviour
// {
//     [Header("Map Size")]
//     public int width = 200;
//     public int height = 200;

//     [Header("Noise")]
//     public float noiseScale = 40f;
//     public int octaves = 4;
//     public float persistence = 0.5f;
//     public float lacunarity = 2f;
//     public int seed = 42;

//     [Header("Corridors")]
//     public int corridorWalkers = 20;
//     public int walkerSteps = 800;
//     public int minBrushSize = 1;
//     public int maxBrushSize = 4;

//     [Header("Floor Heights")]
//     public float shallowHeight = 0f;
//     public float midHeight = -3f;
//     public float deepHeight = -6f;
//     public int heightSmoothPasses = 12;

//     [Header("Walls")]
//     public float wallRise = 10f;
//     public float perimeterWallHeight = 20f;
//     public float deepThreshold = 0.35f;
//     public float midThreshold = 0.65f;

//     [Header("Spike Removal")]
//     public int minWallNeighbors = 5;
//     public int erosionPasses = 5;

//     public void Generate()
//     {
//         MapData.Width  = width;
//         MapData.Height = height;
//         MapData.Cells  = new MapCell[width, height];

//         float[,] noiseMap = GenerateNoiseMap();
//         bool[,]  carved   = CarveCorridors();

//         carved = ErodeWalls(carved, erosionPasses);
//         carved = ErodeWallColumns(carved);

//         float[,] floorHeights = new float[width, height];
//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//         {
//             if (!carved[x, y]) { floorHeights[x, y] = shallowHeight; continue; }
//             float n = noiseMap[x, y];
//             floorHeights[x, y] = n < deepThreshold ? deepHeight
//                                 : n < midThreshold  ? midHeight
//                                 : shallowHeight;
//         }

//         floorHeights = SmoothHeights(floorHeights, carved, heightSmoothPasses);

//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//         {
//             var cell     = new MapCell();
//             bool isPerim = x==0 || y==0 || x==width-1 || y==height-1;
//             cell.isWall  = isPerim || !carved[x, y];

//             float n = noiseMap[x, y];
//             cell.zone = n < deepThreshold ? DepthZone.Deep
//                       : n < midThreshold  ? DepthZone.Mid
//                       : DepthZone.Shallow;

//             if (!cell.isWall)
//             {
//                 cell.height     = floorHeights[x, y];
//                 cell.wallHeight = 0f;
//             }
//             else
//             {
//                 cell.height     = deepHeight - 2f;
//                 cell.wallHeight = isPerim
//                     ? perimeterWallHeight
//                     : wallRise + Mathf.Abs(deepHeight) + 2f;
//             }

//             MapData.Cells[x, y] = cell;
//         }

//         Object.FindFirstObjectByType<MeshBuilder>()?.Build();
//     }

//     bool[,] ErodeWalls(bool[,] carved, int passes)
//     {
//         bool[,] result = (bool[,])carved.Clone();
//         for (int p = 0; p < passes; p++)
//         {
//             bool[,] next = (bool[,])result.Clone();
//             for (int x = 1; x < width-1; x++)
//             for (int y = 1; y < height-1; y++)
//             {
//                 if (result[x, y]) continue;
//                 int wallNeighbors = 0;
//                 for (int dx = -1; dx <= 1; dx++)
//                 for (int dy = -1; dy <= 1; dy++)
//                 {
//                     if (dx==0 && dy==0) continue;
//                     if (!result[x+dx, y+dy]) wallNeighbors++;
//                 }
//                 if (wallNeighbors < minWallNeighbors)
//                     next[x, y] = true;
//             }
//             result = next;
//         }
//         return result;
//     }

//     bool[,] ErodeWallColumns(bool[,] carved)
//     {
//         bool[,] result = (bool[,])carved.Clone();
//         bool changed = true;
//         while (changed)
//         {
//             changed = false;
//             for (int x = 1; x < width-1; x++)
//             for (int y = 1; y < height-1; y++)
//             {
//                 if (result[x, y]) continue; // skip floor cells

//                 // Count cardinal wall neighbors only
//                 int cardinalWalls = 0;
//                 if (!result[x+1, y]) cardinalWalls++;
//                 if (!result[x-1, y]) cardinalWalls++;
//                 if (!result[x, y+1]) cardinalWalls++;
//                 if (!result[x, y-1]) cardinalWalls++;

//                 // Wall connected to fewer than 2 cardinal walls = spike, remove it
//                 if (cardinalWalls < 2)
//                 {
//                     result[x, y] = true;
//                     changed = true;
//                 }
//             }
//         }
//         return result;
//     }

//     float[,] SmoothHeights(float[,] heights, bool[,] carved, int passes)
//     {
//         float[,] result = heights;
//         for (int p = 0; p < passes; p++)
//         {
//             float[,] next = new float[width, height];
//             for (int x = 0; x < width; x++)
//             for (int y = 0; y < height; y++)
//             {
//                 if (!carved[x, y]) { next[x, y] = result[x, y]; continue; }
//                 float sum = 0; int count = 0;
//                 for (int dx = -1; dx <= 1; dx++)
//                 for (int dy = -1; dy <= 1; dy++)
//                 {
//                     int nx = x+dx, ny = y+dy;
//                     if (nx<0||ny<0||nx>=width||ny>=height) continue;
//                     sum += result[nx, ny]; count++;
//                 }
//                 next[x, y] = sum / count;
//             }
//             result = next;
//         }
//         return result;
//     }

//     void Start() => Generate();

//     float[,] GenerateNoiseMap()
//     {
//         var map  = new float[width, height];
//         var rng  = new System.Random(seed);
//         var offs = new Vector2[octaves];
//         for (int i = 0; i < octaves; i++)
//             offs[i] = new Vector2(rng.Next(-10000,10000), rng.Next(-10000,10000));

//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//         {
//             float amp=1, freq=1, val=0;
//             for (int o = 0; o < octaves; o++)
//             {
//                 val  += Mathf.PerlinNoise((x+offs[o].x)/noiseScale*freq,
//                                           (y+offs[o].y)/noiseScale*freq) * amp;
//                 amp  *= persistence; freq *= lacunarity;
//             }
//             map[x,y] = Mathf.Clamp01(val/octaves);
//         }
//         return map;
//     }

//     bool[,] CarveCorridors()
//     {
//         var carved = new bool[width, height];
//         var rng    = new System.Random(seed);
//         for (int w = 0; w < corridorWalkers; w++)
//         {
//             int cx = rng.Next(5, width-5), cy = rng.Next(5, height-5);
//             for (int step = 0; step < walkerSteps; step++)
//             {
//                 float t = (float)step/walkerSteps;
//                 int brush = Mathf.RoundToInt(Mathf.Lerp(minBrushSize, maxBrushSize,
//                     Mathf.Sin(t*Mathf.PI*6f+w)*0.5f+0.5f));
//                 CarveCircle(carved, cx, cy, brush);
//                 int dir = rng.Next(4);
//                 cx = Mathf.Clamp(cx+(dir==0?1:dir==1?-1:0), 2, width-3);
//                 cy = Mathf.Clamp(cy+(dir==2?1:dir==3?-1:0), 2, height-3);
//             }
//         }
//         return carved;
//     }

//     void CarveCircle(bool[,] carved, int cx, int cy, int radius)
//     {
//         for (int dx=-radius; dx<=radius; dx++)
//         for (int dy=-radius; dy<=radius; dy++)
//             if (dx*dx+dy*dy<=radius*radius)
//                 carved[Mathf.Clamp(cx+dx,1,width-2),
//                        Mathf.Clamp(cy+dy,1,height-2)] = true;
//     }
// }
