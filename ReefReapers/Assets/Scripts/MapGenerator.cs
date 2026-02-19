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

    [Header("Mesa Formations")]
    public int mesaCount = 65;
    public int mesaRadiusMin = 3;
    public int mesaRadiusMax = 12;
    public float mesaMinSpacing = 7f;
    public float mesaHeightMin = 4f;
    public float mesaHeightMax = 18f;

    [Header("Formation Type Chances (must add to 1)")]
    [Range(0,1)] public float chanceRoundMesa   = 0.35f;
    [Range(0,1)] public float chanceHourglass   = 0.20f;
    [Range(0,1)] public float chanceCanyon      = 0.20f;
    [Range(0,1)] public float chanceChunky      = 0.15f;
    [Range(0,1)] public float chanceBoulder     = 0.10f;

    [Header("Sand Floor")]
    public float floorVariation  = 0.4f;
    public float floorNoiseScale = 20f;

    [Header("Depth Zones")]
    public float shallowHeight  =  0f;
    public float midHeight      = -4f;
    public float deepHeight     = -8f;
    public float zoneNoiseScale = 80f;
    public float deepThreshold  = 0.35f;
    public float midThreshold   = 0.65f;
    public int   heightSmoothPasses = 6;

    [Header("Perimeter")]
    public float perimeterWallHeight = 22f;
    [Tooltip("Base thickness of the perimeter band")]
    public int   perimeterThickness  = 6;
    [Tooltip("How much the perimeter edge deforms — bigger = more organic")]
    public float perimeterNoiseAmount = 5f;
    [Tooltip("Scale of the perimeter deformation noise")]
    public float perimeterNoiseScale  = 0.08f;

    void Start() => Generate();

    public void Generate()
    {
        MapData.Width  = width;
        MapData.Height = height;
        MapData.Cells  = new MapCell[width, height];

        float[,] zoneNoise  = GenerateZoneNoise();
        float[,] floorNoise = GenerateFloorNoise();
        bool[,]  isMesa     = PlaceMesas();
        bool[,]  isPerim    = BuildOrganicPerimeter();

        float[,] floorHeights = new float[width, height];
        DepthZone[,] zones    = new DepthZone[width, height];

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            float zn = zoneNoise[x, y];
            DepthZone zone;
            float baseH;

            if      (zn < deepThreshold) { zone=DepthZone.Deep;    baseH=deepHeight;    }
            else if (zn < midThreshold)  { zone=DepthZone.Mid;     baseH=midHeight;     }
            else                         { zone=DepthZone.Shallow; baseH=shallowHeight; }

            zones[x,y] = zone;
            float detail = (floorNoise[x,y]-0.5f)*2f*floorVariation;
            floorHeights[x,y] = baseH+detail;
        }

        floorHeights = SmoothHeightsWithinZones(floorHeights, zones, heightSmoothPasses);

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var cell  = new MapCell();
            cell.zone = zones[x,y];

            if (isPerim[x,y])
            {
                cell.isWall     = true;
                cell.height     = deepHeight - 2f;
                cell.wallHeight = perimeterWallHeight + Mathf.Abs(deepHeight) + 2f;
            }
            else if (isMesa[x,y])
            {
                cell.isWall = true;
                float rng01 = Mathf.PerlinNoise(x*0.3f+seed, y*0.3f+seed);
                cell.height     = floorHeights[x,y];
                cell.wallHeight = Mathf.Lerp(mesaHeightMin, mesaHeightMax, rng01);
            }
            else
            {
                cell.isWall     = false;
                cell.height     = floorHeights[x,y];
                cell.wallHeight = 0f;
            }

            MapData.Cells[x,y] = cell;
        }

        Object.FindFirstObjectByType<MeshBuilder>()?.Build();
    }

    // Organic perimeter: instead of a flat band, we use noise to push the
    // inner edge of the perimeter inward/outward so it looks like natural rock
    bool[,] BuildOrganicPerimeter()
    {
        var isPerim = new bool[width, height];

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            // Distance from nearest edge (0 at edge, increases inward)
            float distFromEdge = Mathf.Min(
                Mathf.Min(x, width-1-x),
                Mathf.Min(y, height-1-y));

            // Sample noise at this point to deform the inner boundary
            float n = Mathf.PerlinNoise(
                x * perimeterNoiseScale + seed * 0.1f,
                y * perimeterNoiseScale + seed * 0.1f);

            // Deformed thickness: base thickness +/- noise
            float deformedThickness = perimeterThickness + (n - 0.5f) * 2f * perimeterNoiseAmount;

            if (distFromEdge < deformedThickness)
                isPerim[x, y] = true;
        }

        return isPerim;
    }

    bool[,] PlaceMesas()
    {
        var rng     = new System.Random(seed);
        var isMesa  = new bool[width, height];
        var centers = new List<Vector2>();

        // Height map for per-cell mesa height (varied per formation)
        // We store it temporarily to drive wallHeight in Generate()
        int safeMargin = perimeterThickness + mesaRadiusMax + 2;
        int attempts   = mesaCount * 12;
        int placed     = 0;

        for (int a = 0; a < attempts && placed < mesaCount; a++)
        {
            int cx = rng.Next(safeMargin, width  - safeMargin);
            int cy = rng.Next(safeMargin, height - safeMargin);

            bool tooClose = false;
            foreach (var c in centers)
                if (Vector2.Distance(c, new Vector2(cx,cy)) < mesaMinSpacing)
                { tooClose=true; break; }
            if (tooClose) continue;

            int radius = rng.Next(mesaRadiusMin, mesaRadiusMax);

            // Pick formation type
            double roll = rng.NextDouble();
            float cumChance = 0f;

            if (roll < (cumChance += chanceRoundMesa))
                PlaceRoundMesa(isMesa, rng, cx, cy, radius);
            else if (roll < (cumChance += chanceHourglass))
                PlaceHourglass(isMesa, rng, cx, cy, radius);
            else if (roll < (cumChance += chanceCanyon))
                PlaceCanyon(isMesa, rng, cx, cy, radius);
            else if (roll < (cumChance += chanceChunky))
                PlaceChunky(isMesa, rng, cx, cy, radius);
            else
                PlaceBoulders(isMesa, rng, cx, cy, radius);

            centers.Add(new Vector2(cx, cy));
            placed++;
        }

        return isMesa;
    }

    // Standard irregular mesa — roughly circular with noisy edge
    void PlaceRoundMesa(bool[,] m, System.Random rng, int cx, int cy, int radius)
    {
        for (int dx=-radius;dx<=radius;dx++)
        for (int dy=-radius;dy<=radius;dy++)
        {
            float dist = Mathf.Sqrt(dx*dx+dy*dy);
            if (dist > radius) continue;
            float edgeNoise = (float)(rng.NextDouble()*2-1) * radius * 0.3f;
            if (dist > radius + edgeNoise) continue;
            SetMesa(m, cx+dx, cy+dy);
        }
    }

    // Hourglass: two wide lobes connected by a narrow waist
    void PlaceHourglass(bool[,] m, System.Random rng, int cx, int cy, int radius)
    {
        // Top lobe
        int topCY  = cy - radius/2;
        int botCY  = cy + radius/2;
        int lobeR  = radius;
        int waistR = Mathf.Max(1, radius/3);

        for (int dx=-lobeR;dx<=lobeR;dx++)
        for (int dy=-lobeR;dy<=lobeR;dy++)
        {
            float dist = Mathf.Sqrt(dx*dx+dy*dy);
            if (dist <= lobeR)
            {
                SetMesa(m, cx+dx, topCY+dy);
                SetMesa(m, cx+dx, botCY+dy);
            }
        }

        // Waist connecting them — narrow vertical band
        int waistTop = topCY + lobeR/2;
        int waistBot = botCY - lobeR/2;
        for (int wy = waistTop; wy <= waistBot; wy++)
        for (int dx = -waistR; dx <= waistR; dx++)
        {
            // Curve the waist inward using sine
            float t    = (float)(wy-waistTop)/(waistBot-waistTop+1);
            float curveR = waistR * (1f - Mathf.Sin(t*Mathf.PI) * 0.5f);
            if (Mathf.Abs(dx) <= curveR)
                SetMesa(m, cx+dx, wy);
        }
    }

    // Canyon: a long stretched formation with an open gap through the middle
    void PlaceCanyon(bool[,] m, System.Random rng, int cx, int cy, int radius)
    {
        bool horizontal = rng.Next(2) == 0;
        int length = radius * 2 + rng.Next(0, radius);
        int wallW  = Mathf.Max(2, radius/3);
        int gapW   = Mathf.Max(1, radius/4);

        for (int i = -length; i <= length; i++)
        {
            // Add some meandering to the canyon walls
            float meander = Mathf.Sin(i * 0.3f) * 1.5f;

            for (int w = -wallW; w <= wallW; w++)
            {
                // Skip the gap in the middle
                if (Mathf.Abs(w + meander) < gapW) continue;

                int px = horizontal ? cx+i : cx+w;
                int py = horizontal ? cy+w : cy+i;
                SetMesa(m, px, py);
            }
        }
    }

    // Chunky: cluster of overlapping rectangular blocks like mesa photo
    void PlaceChunky(bool[,] m, System.Random rng, int cx, int cy, int radius)
    {
        int blockCount = 3 + rng.Next(0, 4);
        for (int b = 0; b < blockCount; b++)
        {
            int bx = cx + rng.Next(-radius, radius);
            int by = cy + rng.Next(-radius, radius);
            int bw = rng.Next(radius/3, radius);
            int bh = rng.Next(radius/3, radius);

            for (int dx=-bw;dx<=bw;dx++)
            for (int dy=-bh;dy<=bh;dy++)
                SetMesa(m, bx+dx, by+dy);
        }
    }

    // Boulders: scattered small circles of varying size
    void PlaceBoulders(bool[,] m, System.Random rng, int cx, int cy, int radius)
    {
        int boulderCount = 3 + rng.Next(0, 5);
        for (int b = 0; b < boulderCount; b++)
        {
            int bx  = cx + rng.Next(-radius, radius);
            int by  = cy + rng.Next(-radius, radius);
            int br  = rng.Next(1, Mathf.Max(2, radius/2));

            for (int dx=-br;dx<=br;dx++)
            for (int dy=-br;dy<=br;dy++)
                if (dx*dx+dy*dy <= br*br)
                    SetMesa(m, bx+dx, by+dy);
        }
    }

    void SetMesa(bool[,] m, int x, int y)
    {
        if (x<1||y<1||x>=width-1||y>=height-1) return;
        m[x,y]=true;
    }

    float[,] SmoothHeightsWithinZones(float[,] heights, DepthZone[,] zones, int passes)
    {
        float[,] result=heights;
        for (int p=0;p<passes;p++)
        {
            float[,] next=new float[width,height];
            for (int x=0;x<width;x++)
            for (int y=0;y<height;y++)
            {
                float sum=result[x,y]*2f,tot=2f;
                for (int dx=-2;dx<=2;dx++)
                for (int dy=-2;dy<=2;dy++)
                {
                    if (dx==0&&dy==0) continue;
                    int nx=x+dx,ny=y+dy;
                    if (nx<0||ny<0||nx>=width||ny>=height) continue;
                    float dist=Mathf.Sqrt(dx*dx+dy*dy);
                    float w=1f/dist;
                    if (zones[nx,ny]!=zones[x,y]) w*=0.25f;
                    sum+=result[nx,ny]*w; tot+=w;
                }
                next[x,y]=sum/tot;
            }
            result=next;
        }
        return result;
    }

    float[,] GenerateZoneNoise()
    {
        var map=new float[width,height];
        var rng=new System.Random(seed+1337);
        var offs=new Vector2[2];
        for (int i=0;i<2;i++)
            offs[i]=new Vector2(rng.Next(-10000,10000),rng.Next(-10000,10000));
        for (int x=0;x<width;x++)
        for (int y=0;y<height;y++)
        {
            float amp=1,freq=1,val=0,tot=0;
            for (int o=0;o<2;o++)
            {
                val+=Mathf.PerlinNoise(
                    (x+offs[o].x)/zoneNoiseScale*freq,
                    (y+offs[o].y)/zoneNoiseScale*freq)*amp;
                tot+=amp; amp*=0.5f; freq*=2f;
            }
            map[x,y]=Mathf.Clamp01(val/tot);
        }
        return map;
    }

    float[,] GenerateFloorNoise()
    {
        var map=new float[width,height];
        var rng=new System.Random(seed+777);
        Vector2 off=new Vector2(rng.Next(-10000,10000),rng.Next(-10000,10000));
        for (int x=0;x<width;x++)
        for (int y=0;y<height;y++)
            map[x,y]=Mathf.PerlinNoise((x+off.x)/floorNoiseScale,(y+off.y)/floorNoiseScale);
        return map;
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

//     [Header("Mesa Formations")]
//     [Tooltip("How many mesa clusters to attempt placing")]
//     public int mesaCount = 60;
//     [Tooltip("Min radius of a mesa base in cells")]
//     public int mesaRadiusMin = 3;
//     [Tooltip("Max radius of a mesa base in cells")]
//     public int mesaRadiusMax = 10;
//     [Tooltip("Minimum distance between mesa centers")]
//     public float mesaMinSpacing = 8f;
//     [Tooltip("How tall mesas rise above the floor")]
//     public float mesaHeightMin = 6f;
//     public float mesaHeightMax = 14f;
//     [Tooltip("How much the top of each mesa is eroded inward — creates the flat top cliff look")]
//     public float mesaTopErosion = 0.3f;

//     [Header("Sand Floor")]
//     public float floorVariation = 0.4f;   // gentle dune-like bumps
//     public float floorNoiseScale = 20f;

//     [Header("Depth Zones (sand floor height)")]
//     public float shallowHeight =  0f;
//     public float midHeight     = -4f;
//     public float deepHeight    = -8f;
//     public float zoneNoiseScale = 80f;
//     public float deepThreshold  = 0.35f;
//     public float midThreshold   = 0.65f;
//     public int   heightSmoothPasses = 6;

//     [Header("Perimeter Wall")]
//     public float perimeterWallHeight = 20f;
//     public int   perimeterThickness  = 4;

//     void Start() => Generate();

//     public void Generate()
//     {
//         MapData.Width  = width;
//         MapData.Height = height;
//         MapData.Cells  = new MapCell[width, height];

//         float[,] zoneNoise  = GenerateZoneNoise();
//         float[,] floorNoise = GenerateFloorNoise();
//         bool[,]  isMesa     = PlaceMesas();
//         bool[,]  isPerim    = BuildPerimeter();

//         // Build floor heights from zone noise
//         float[,] floorHeights = new float[width, height];
//         DepthZone[,] zones    = new DepthZone[width, height];

//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//         {
//             float zn = zoneNoise[x, y];
//             DepthZone zone;
//             float baseH;

//             if (zn < deepThreshold)       { zone = DepthZone.Deep;    baseH = deepHeight;    }
//             else if (zn < midThreshold)   { zone = DepthZone.Mid;     baseH = midHeight;     }
//             else                          { zone = DepthZone.Shallow; baseH = shallowHeight; }

//             zones[x, y] = zone;
//             float detail = (floorNoise[x, y] - 0.5f) * 2f * floorVariation;
//             floorHeights[x, y] = baseH + detail;
//         }

//         // Smooth floor within zones
//         floorHeights = SmoothHeightsWithinZones(floorHeights, zones, heightSmoothPasses);

//         // Populate cells
//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//         {
//             var cell = new MapCell();
//             cell.zone = zones[x, y];

//             if (isPerim[x, y])
//             {
//                 // Solid perimeter rock wall
//                 cell.isWall     = true;
//                 cell.height     = deepHeight - 2f;
//                 cell.wallHeight = perimeterWallHeight + Mathf.Abs(deepHeight) + 2f;
//             }
//             else if (isMesa[x, y])
//             {
//                 // Mesa formation — treated as wall so player can't walk on it
//                 cell.isWall     = true;
//                 float rng01     = Mathf.PerlinNoise(x * 0.3f + seed, y * 0.3f + seed);
//                 float mesaH     = Mathf.Lerp(mesaHeightMin, mesaHeightMax, rng01);
//                 cell.height     = floorHeights[x, y];
//                 cell.wallHeight = mesaH;
//             }
//             else
//             {
//                 // Open sand floor
//                 cell.isWall     = false;
//                 cell.height     = floorHeights[x, y];
//                 cell.wallHeight = 0f;
//             }

//             MapData.Cells[x, y] = cell;
//         }

//         Object.FindFirstObjectByType<MeshBuilder>()?.Build();
//     }

//     bool[,] PlaceMesas()
//     {
//         var rng      = new System.Random(seed);
//         var isMesa   = new bool[width, height];
//         var centers  = new List<Vector2>();

//         int attempts = mesaCount * 10;
//         int placed   = 0;

//         for (int a = 0; a < attempts && placed < mesaCount; a++)
//         {
//             int cx = rng.Next(perimeterThickness + mesaRadiusMax,
//                               width  - perimeterThickness - mesaRadiusMax);
//             int cy = rng.Next(perimeterThickness + mesaRadiusMax,
//                               height - perimeterThickness - mesaRadiusMax);

//             // Check spacing from existing mesas
//             bool tooClose = false;
//             foreach (var c in centers)
//                 if (Vector2.Distance(c, new Vector2(cx, cy)) < mesaMinSpacing)
//                 { tooClose = true; break; }

//             if (tooClose) continue;

//             int radius = rng.Next(mesaRadiusMin, mesaRadiusMax);

//             // Carve the mesa shape — slightly irregular circle
//             for (int dx = -radius; dx <= radius; dx++)
//             for (int dy = -radius; dy <= radius; dy++)
//             {
//                 float dist = Mathf.Sqrt(dx*dx + dy*dy);
//                 if (dist > radius) continue;

//                 // Add noise to edge to make it irregular
//                 float edgeNoise = (float)(rng.NextDouble() * 2 - 1) * radius * 0.25f;
//                 if (dist > radius + edgeNoise) continue;

//                 int mx = cx+dx, my = cy+dy;
//                 if (mx<0||my<0||mx>=width||my>=height) continue;
//                 isMesa[mx, my] = true;
//             }

//             centers.Add(new Vector2(cx, cy));
//             placed++;
//         }

//         return isMesa;
//     }

//     bool[,] BuildPerimeter()
//     {
//         var isPerim = new bool[width, height];
//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//             if (x < perimeterThickness || y < perimeterThickness ||
//                 x >= width-perimeterThickness || y >= height-perimeterThickness)
//                 isPerim[x, y] = true;
//         return isPerim;
//     }

//     float[,] SmoothHeightsWithinZones(float[,] heights, DepthZone[,] zones, int passes)
//     {
//         float[,] result = heights;
//         for (int p = 0; p < passes; p++)
//         {
//             float[,] next = new float[width, height];
//             for (int x = 0; x < width; x++)
//             for (int y = 0; y < height; y++)
//             {
//                 float sum = result[x,y] * 2f, tot = 2f;
//                 for (int dx=-2;dx<=2;dx++)
//                 for (int dy=-2;dy<=2;dy++)
//                 {
//                     if (dx==0&&dy==0) continue;
//                     int nx=x+dx, ny=y+dy;
//                     if (nx<0||ny<0||nx>=width||ny>=height) continue;
//                     float dist = Mathf.Sqrt(dx*dx+dy*dy);
//                     float w = 1f/dist;
//                     if (zones[nx,ny] != zones[x,y]) w *= 0.25f;
//                     sum += result[nx,ny]*w; tot += w;
//                 }
//                 next[x,y] = sum/tot;
//             }
//             result = next;
//         }
//         return result;
//     }

//     float[,] GenerateZoneNoise()
//     {
//         var map = new float[width, height];
//         var rng = new System.Random(seed + 1337);
//         var offs = new Vector2[2];
//         for (int i=0;i<2;i++)
//             offs[i] = new Vector2(rng.Next(-10000,10000), rng.Next(-10000,10000));
//         for (int x=0;x<width;x++)
//         for (int y=0;y<height;y++)
//         {
//             float amp=1,freq=1,val=0,tot=0;
//             for (int o=0;o<2;o++)
//             {
//                 val += Mathf.PerlinNoise(
//                     (x+offs[o].x)/zoneNoiseScale*freq,
//                     (y+offs[o].y)/zoneNoiseScale*freq)*amp;
//                 tot+=amp; amp*=0.5f; freq*=2f;
//             }
//             map[x,y]=Mathf.Clamp01(val/tot);
//         }
//         return map;
//     }

//     float[,] GenerateFloorNoise()
//     {
//         var map = new float[width, height];
//         var rng = new System.Random(seed + 777);
//         Vector2 off = new Vector2(rng.Next(-10000,10000), rng.Next(-10000,10000));
//         for (int x=0;x<width;x++)
//         for (int y=0;y<height;y++)
//             map[x,y] = Mathf.PerlinNoise((x+off.x)/floorNoiseScale, (y+off.y)/floorNoiseScale);
//         return map;
//     }

//     float[,] GenerateNoiseMap()
//     {
//         var map=new float[width,height];
//         var rng=new System.Random(seed);
//         var offs=new Vector2[octaves];
//         for (int i=0;i<octaves;i++)
//             offs[i]=new Vector2(rng.Next(-10000,10000),rng.Next(-10000,10000));
//         for (int x=0;x<width;x++)
//         for (int y=0;y<height;y++)
//         {
//             float amp=1,freq=1,val=0;
//             for (int o=0;o<octaves;o++)
//             {
//                 val+=Mathf.PerlinNoise((x+offs[o].x)/noiseScale*freq,
//                                        (y+offs[o].y)/noiseScale*freq)*amp;
//                 amp*=persistence; freq*=lacunarity;
//             }
//             map[x,y]=Mathf.Clamp01(val/octaves);
//         }
//         return map;
//     }
// }


//canyons
// using UnityEngine;
// using System.Collections.Generic;

// public class MapGenerator : MonoBehaviour
// {
//     [Header("Map Size")]
//     public int width = 200;
//     public int height = 200;

//     [Header("Noise")]
//     public float noiseScale = 25f;
//     public int octaves = 4;
//     public float persistence = 0.5f;
//     public float lacunarity = 2f;
//     public int seed = 42;

//     [Header("Corridors")]
//     public int corridorWalkers = 20;
//     public int walkerSteps = 800;
//     public int minBrushSize = 1;
//     public int maxBrushSize = 4;

//     [Header("Zone Heights — these are the shelf levels")]
//     public float shallowHeight =  0f;
//     public float midHeight     = -8f;
//     public float deepHeight    = -16f;

//     [Header("Zone Noise — controls how big each zone region is")]
//     [Tooltip("Large scale = big broad zones. Small scale = fragmented zones")]
//     public float zoneNoiseScale = 80f;
//     public float deepThreshold  = 0.35f;
//     public float midThreshold   = 0.65f;

//     [Header("Within-Zone Variation")]
//     [Tooltip("Gentle height noise applied within each zone so floor isn't totally flat")]
//     public float intraZoneVariation = 1.2f;
//     public float intraZoneScale     = 15f;
//     [Tooltip("Smoothing within zones only — keeps cliff edges sharp")]
//     public int heightSmoothPasses = 4;

//     [Header("Walls")]
//     public float wallRise            = 12f;
//     public float perimeterWallHeight = 24f;

//     [Header("Spike Removal")]
//     public int minWallNeighbors = 5;
//     public int erosionPasses    = 5;

//     // Exposed so MeshBuilder can read zone per cell for texturing
//     public float DeepHeight   => deepHeight;
//     public float ShallowHeight => shallowHeight;

//     public void Generate()
//     {
//         MapData.Width  = width;
//         MapData.Height = height;
//         MapData.Cells  = new MapCell[width, height];

//         float[,] zoneNoise  = GenerateZoneNoise();   // large scale — defines zones
//         float[,] detailNoise = GenerateDetailNoise(); // small scale — within-zone bumps
//         bool[,]  carved      = CarveCorridors();

//         carved = ErodeWalls(carved, erosionPasses);
//         carved = ErodeWallColumns(carved);
//         carved = RemoveIsolatedFloor(carved);
//         carved = ErodeWallColumns(carved);

//         // Assign each floor cell a base height from its zone
//         // plus a small detail variation so it's not totally flat
//         float[,] floorHeights = new float[width, height];
//         DepthZone[,] zones = new DepthZone[width, height];

//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//         {
//             float zn = zoneNoise[x, y];
//             DepthZone zone;
//             float baseH;

//             if (zn < deepThreshold)
//             {
//                 zone  = DepthZone.Deep;
//                 baseH = deepHeight;
//             }
//             else if (zn < midThreshold)
//             {
//                 zone  = DepthZone.Mid;
//                 baseH = midHeight;
//             }
//             else
//             {
//                 zone  = DepthZone.Shallow;
//                 baseH = shallowHeight;
//             }

//             zones[x, y] = zone;

//             // Add gentle bumps within the zone
//             float detail = (detailNoise[x, y] - 0.5f) * 2f * intraZoneVariation;
//             floorHeights[x, y] = carved[x, y] ? baseH + detail : baseH;
//         }

//         // Smooth heights BUT only average with cells in the SAME zone
//         // This keeps inter-zone boundaries sharp while smoothing within zones
//         floorHeights = SmoothHeightsWithinZones(floorHeights, carved, zones, heightSmoothPasses);

//         // Populate MapData cells
//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//         {
//             var cell     = new MapCell();
//             bool isPerim = x==0 || y==0 || x==width-1 || y==height-1;
//             cell.isWall  = isPerim || !carved[x, y];
//             cell.zone    = zones[x, y];

//             if (!cell.isWall)
//             {
//                 cell.height     = floorHeights[x, y];
//                 cell.wallHeight = 0f;
//             }
//             else
//             {
//                 // Wall base drops below the deepest zone floor
//                 cell.height     = deepHeight - 3f;
//                 cell.wallHeight = isPerim
//                     ? perimeterWallHeight
//                     : wallRise + Mathf.Abs(deepHeight) + 3f;
//             }

//             MapData.Cells[x, y] = cell;
//         }

//         Object.FindFirstObjectByType<MeshBuilder>()?.Build();
//     }

//     // Smooth only averaging neighbors in the same zone
//     // Neighbors in a different zone are ignored — preserving the sharp drop
// float[,] SmoothHeightsWithinZones(float[,] heights, bool[,] carved,
//     DepthZone[,] zones, int passes)
// {
//     float[,] result = heights;
//     for (int p = 0; p < passes; p++)
//     {
//         float[,] next = new float[width, height];
//         for (int x = 0; x < width; x++)
//         for (int y = 0; y < height; y++)
//         {
//             if (!carved[x, y]) { next[x,y]=result[x,y]; continue; }

//             float sum = result[x,y] * 1f;
//             float tot = 1f;

//             // Sample a wider radius so transitions spread over more cells
//             for (int dx=-2;dx<=2;dx++)
//             for (int dy=-2;dy<=2;dy++)
//             {
//                 if (dx==0&&dy==0) continue;
//                 int nx=x+dx, ny=y+dy;
//                 if (nx<0||ny<0||nx>=width||ny>=height) continue;
//                 if (!carved[nx,ny]) continue;

//                 // Weight by distance
//                 float dist = Mathf.Sqrt(dx*dx+dy*dy);
//                 float w = 1f / dist;

//                 // Cross-zone blends freely — just weighted by distance
//                 // No zone penalty at all — pure distance-weighted average
//                 sum += result[nx,ny] * w;
//                 tot += w;
//             }
//             next[x,y] = sum/tot;
//         }
//         result = next;
//     }
//     return result;
// }

//     // Large scale noise — determines which zone a region belongs to
//     float[,] GenerateZoneNoise()
//     {
//         var map = new float[width, height];
//         var rng = new System.Random(seed + 1337);
//         var offs = new Vector2[2]; // fewer octaves = broader blobs
//         for (int i=0;i<2;i++)
//             offs[i] = new Vector2(rng.Next(-10000,10000), rng.Next(-10000,10000));

//         for (int x=0;x<width;x++)
//         for (int y=0;y<height;y++)
//         {
//             float amp=1,freq=1,val=0,tot=0;
//             for (int o=0;o<2;o++)
//             {
//                 val += Mathf.PerlinNoise(
//                     (x+offs[o].x)/zoneNoiseScale*freq,
//                     (y+offs[o].y)/zoneNoiseScale*freq)*amp;
//                 tot += amp; amp*=0.5f; freq*=2f;
//             }
//             map[x,y] = Mathf.Clamp01(val/tot);
//         }
//         return map;
//     }

//     // Small scale noise — gentle bumps within each zone
//     float[,] GenerateDetailNoise()
//     {
//         var map = new float[width, height];
//         var rng = new System.Random(seed + 777);
//         Vector2 off = new Vector2(rng.Next(-10000,10000), rng.Next(-10000,10000));
//         for (int x=0;x<width;x++)
//         for (int y=0;y<height;y++)
//             map[x,y] = Mathf.PerlinNoise((x+off.x)/intraZoneScale, (y+off.y)/intraZoneScale);
//         return map;
//     }

//     bool[,] RemoveIsolatedFloor(bool[,] carved)
//     {
//         bool[,] result  = (bool[,])carved.Clone();
//         bool[,] visited = new bool[width, height];
//         int startX=-1, startY=-1;

//         for (int r=0;r<Mathf.Max(width,height)&&startX==-1;r++)
//         for (int dx=-r;dx<=r&&startX==-1;dx++)
//         for (int dy=-r;dy<=r&&startX==-1;dy++)
//         {
//             int cx=width/2+dx, cy=height/2+dy;
//             if (cx<0||cy<0||cx>=width||cy>=height) continue;
//             if (result[cx,cy]) { startX=cx; startY=cy; }
//         }

//         if (startX==-1) return result;

//         var queue = new Queue<Vector2Int>();
//         queue.Enqueue(new Vector2Int(startX,startY));
//         visited[startX,startY]=true;

//         while (queue.Count>0)
//         {
//             var cur=queue.Dequeue();
//             TryEnqueue(result,visited,queue,cur.x+1,cur.y);
//             TryEnqueue(result,visited,queue,cur.x-1,cur.y);
//             TryEnqueue(result,visited,queue,cur.x,cur.y+1);
//             TryEnqueue(result,visited,queue,cur.x,cur.y-1);
//         }

//         for (int x=0;x<width;x++)
//         for (int y=0;y<height;y++)
//             if (result[x,y]&&!visited[x,y])
//                 result[x,y]=false;

//         return result;
//     }

//     void TryEnqueue(bool[,] c, bool[,] v, Queue<Vector2Int> q, int x, int y)
//     {
//         if (x<0||y<0||x>=width||y>=height||!c[x,y]||v[x,y]) return;
//         v[x,y]=true; q.Enqueue(new Vector2Int(x,y));
//     }

//     bool[,] ErodeWalls(bool[,] carved, int passes)
//     {
//         bool[,] result=(bool[,])carved.Clone();
//         for (int p=0;p<passes;p++)
//         {
//             bool[,] next=(bool[,])result.Clone();
//             for (int x=1;x<width-1;x++)
//             for (int y=1;y<height-1;y++)
//             {
//                 if (result[x,y]) continue;
//                 int wn=0;
//                 for (int dx=-1;dx<=1;dx++)
//                 for (int dy=-1;dy<=1;dy++)
//                 {
//                     if (dx==0&&dy==0) continue;
//                     if (!result[x+dx,y+dy]) wn++;
//                 }
//                 if (wn<minWallNeighbors) next[x,y]=true;
//             }
//             result=next;
//         }
//         return result;
//     }

//     bool[,] ErodeWallColumns(bool[,] carved)
//     {
//         bool[,] result=(bool[,])carved.Clone();
//         bool changed=true;
//         while (changed)
//         {
//             changed=false;
//             for (int x=1;x<width-1;x++)
//             for (int y=1;y<height-1;y++)
//             {
//                 if (result[x,y]) continue;
//                 int cw=0;
//                 if (!result[x+1,y]) cw++;
//                 if (!result[x-1,y]) cw++;
//                 if (!result[x,y+1]) cw++;
//                 if (!result[x,y-1]) cw++;
//                 if (cw<2){result[x,y]=true;changed=true;}
//             }
//         }
//         return result;
//     }

//     void Start() => Generate();

//     float[,] GenerateNoiseMap()
//     {
//         var map=new float[width,height];
//         var rng=new System.Random(seed);
//         var offs=new Vector2[octaves];
//         for (int i=0;i<octaves;i++)
//             offs[i]=new Vector2(rng.Next(-10000,10000),rng.Next(-10000,10000));
//         for (int x=0;x<width;x++)
//         for (int y=0;y<height;y++)
//         {
//             float amp=1,freq=1,val=0;
//             for (int o=0;o<octaves;o++)
//             {
//                 val+=Mathf.PerlinNoise((x+offs[o].x)/noiseScale*freq,
//                                        (y+offs[o].y)/noiseScale*freq)*amp;
//                 amp*=persistence;freq*=lacunarity;
//             }
//             map[x,y]=Mathf.Clamp01(val/octaves);
//         }
//         return map;
//     }

//     bool[,] CarveCorridors()
//     {
//         var carved=new bool[width,height];
//         var rng=new System.Random(seed);
//         for (int w=0;w<corridorWalkers;w++)
//         {
//             int cx=rng.Next(5,width-5),cy=rng.Next(5,height-5);
//             for (int step=0;step<walkerSteps;step++)
//             {
//                 float t=(float)step/walkerSteps;
//                 int brush=Mathf.RoundToInt(Mathf.Lerp(minBrushSize,maxBrushSize,
//                     Mathf.Sin(t*Mathf.PI*6f+w)*0.5f+0.5f));
//                 CarveCircle(carved,cx,cy,brush);
//                 int dir=rng.Next(4);
//                 cx=Mathf.Clamp(cx+(dir==0?1:dir==1?-1:0),2,width-3);
//                 cy=Mathf.Clamp(cy+(dir==2?1:dir==3?-1:0),2,height-3);
//             }
//         }
//         return carved;
//     }

//     void CarveCircle(bool[,] carved,int cx,int cy,int radius)
//     {
//         for (int dx=-radius;dx<=radius;dx++)
//         for (int dy=-radius;dy<=radius;dy++)
//             if (dx*dx+dy*dy<=radius*radius)
//                 carved[Mathf.Clamp(cx+dx,1,width-2),
//                        Mathf.Clamp(cy+dy,1,height-2)]=true;
//     }
// }
