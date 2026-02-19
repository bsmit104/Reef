using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MapGenerator))]
public class MeshBuilder : MonoBehaviour
{
    [Header("Materials")]
    public Material wallMaterial;        // rock/mesa material
    public Material floorShallowMaterial;
    public Material floorMidMaterial;
    public Material floorDeepMaterial;

    [Header("Settings")]
    public float cellSize = 1f;
    public int chunkSize = 16;

    [Header("Mesa Shape")]
    [Tooltip("Noise applied to mesa top surfaces for rocky texture")]
    public float mesaTopNoise    = 1.2f;
    public float mesaNoiseScale  = 5f;
    public int   mesaNoiseOctaves = 2;
    public int   mesaSmoothPasses = 4;
    [Tooltip("Noise on mesa side faces for irregular cliff edges")]
    public float mesaSideVariance = 0.3f;

    private List<GameObject> spawnedObjects = new List<GameObject>();
    private float[,] vertHeight;
    private int[,]   vertSubmesh;
    private bool[,]  isWallVert;

    public void Build()
    {
        foreach (var go in spawnedObjects) Destroy(go);
        spawnedObjects.Clear();
        BuildVertexGrid();
        int cx = Mathf.CeilToInt((float)MapData.Width  / chunkSize);
        int cy = Mathf.CeilToInt((float)MapData.Height / chunkSize);
        for (int x = 0; x < cx; x++)
        for (int y = 0; y < cy; y++)
            BuildChunk(x, y);
    }

    void BuildVertexGrid()
    {
        int gw = MapData.Width  + 1;
        int gh = MapData.Height + 1;
        vertHeight  = new float[gw, gh];
        vertSubmesh = new int[gw, gh];
        isWallVert  = new bool[gw, gh];

        // Separate floor and wall heights
        float[,] floorH = new float[gw, gh];
        float[,] wallH  = new float[gw, gh];

        for (int vx = 0; vx < gw; vx++)
        for (int vy = 0; vy < gh; vy++)
        {
            float floorSum=0; int floorCount=0;
            float wallTopMax=float.MinValue;
            int   wallCount=0;
            DepthZone zone = DepthZone.Shallow;

            for (int dx=-1;dx<=0;dx++)
            for (int dy=-1;dy<=0;dy++)
            {
                var c = MapData.Get(vx+dx, vy+dy);
                if (c==null) continue;
                if (!c.isWall)
                {
                    floorSum += c.height; floorCount++;
                    zone = c.zone;
                }
                else
                {
                    wallTopMax = Mathf.Max(wallTopMax, c.height + c.wallHeight);
                    wallCount++;
                }
            }

            bool isWall = wallCount > 0;
            isWallVert[vx, vy] = isWall;
            vertSubmesh[vx, vy] = isWall ? 0 : 1+(int)zone;

            float fh = floorCount > 0 ? floorSum/floorCount : 0f;
            floorH[vx, vy] = fh;
            wallH[vx, vy]  = isWall
                ? wallTopMax + SampleNoise(vx*cellSize, vy*cellSize)
                : fh;
        }

        // Smooth wall (mesa top) heights only
        for (int pass=0; pass<mesaSmoothPasses; pass++)
        {
            float[,] next = new float[gw, gh];
            for (int vx=0;vx<gw;vx++)
            for (int vy=0;vy<gh;vy++)
            {
                if (!isWallVert[vx,vy]) { next[vx,vy]=wallH[vx,vy]; continue; }
                float wsum=wallH[vx,vy]*4f, wtot=4f;
                for (int dx=-1;dx<=1;dx++)
                for (int dy=-1;dy<=1;dy++)
                {
                    if (dx==0&&dy==0) continue;
                    int nx=vx+dx, ny=vy+dy;
                    if (nx<0||ny<0||nx>=gw||ny>=gh) continue;
                    float w=(dx==0||dy==0)?1f:0.5f;
                    wsum+=wallH[nx,ny]*w; wtot+=w;
                }
                next[vx,vy]=wsum/wtot;
            }
            wallH=next;
        }

        // Smooth floor heights independently, clamped to prevent dipping
        float[,] smoothFloor = (float[,])floorH.Clone();
        for (int pass=0;pass<4;pass++)
        {
            float[,] next=new float[gw,gh];
            for (int vx=0;vx<gw;vx++)
            for (int vy=0;vy<gh;vy++)
            {
                if (isWallVert[vx,vy]){next[vx,vy]=smoothFloor[vx,vy];continue;}
                float sum=smoothFloor[vx,vy]*2f,tot=2f;
                for (int dx=-1;dx<=1;dx++)
                for (int dy=-1;dy<=1;dy++)
                {
                    if (dx==0&&dy==0) continue;
                    int nx=vx+dx,ny=vy+dy;
                    if (nx<0||ny<0||nx>=gw||ny>=gh) continue;
                    if (isWallVert[nx,ny]) continue;
                    float w=(dx==0||dy==0)?1f:0.5f;
                    sum+=smoothFloor[nx,ny]*w; tot+=w;
                }
                next[vx,vy]=tot>0?sum/tot:smoothFloor[vx,vy];
            }
            smoothFloor=next;
        }

        // Clamp floor verts — can never go below lowest floor neighbor
        for (int vx=0;vx<gw;vx++)
        for (int vy=0;vy<gh;vy++)
        {
            if (isWallVert[vx,vy]) continue;
            float minN=float.MaxValue;
            for (int dx=-1;dx<=1;dx++)
            for (int dy=-1;dy<=1;dy++)
            {
                int nx=vx+dx,ny=vy+dy;
                if (nx<0||ny<0||nx>=gw||ny>=gh) continue;
                if (!isWallVert[nx,ny]) minN=Mathf.Min(minN,smoothFloor[nx,ny]);
            }
            smoothFloor[vx,vy]=Mathf.Max(smoothFloor[vx,vy],
                minN>float.MaxValue*0.5f?smoothFloor[vx,vy]:minN);
        }

        // Final: assign correct height per vertex
        for (int vx=0;vx<gw;vx++)
        for (int vy=0;vy<gh;vy++)
            vertHeight[vx,vy] = isWallVert[vx,vy] ? wallH[vx,vy] : smoothFloor[vx,vy];
    }

    void BuildChunk(int chunkX, int chunkY)
    {
        int startX=chunkX*chunkSize, startY=chunkY*chunkSize;

        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new[]{new List<int>(),new List<int>(),
                          new List<int>(),new List<int>()};

        for (int lx=0;lx<chunkSize;lx++)
        for (int ly=0;ly<chunkSize;ly++)
        {
            int gx=startX+lx, gy=startY+ly;
            if (gx>=MapData.Width||gy>=MapData.Height) continue;

            int vx0=gx,vx1=gx+1,vy0=gy,vy1=gy+1;
            float h00=vertHeight[vx0,vy0], h10=vertHeight[vx1,vy0];
            float h01=vertHeight[vx0,vy1], h11=vertHeight[vx1,vy1];

            int idx=verts.Count;
            verts.Add(new Vector3(vx0*cellSize,h00,vy0*cellSize));
            verts.Add(new Vector3(vx1*cellSize,h10,vy0*cellSize));
            verts.Add(new Vector3(vx0*cellSize,h01,vy1*cellSize));
            verts.Add(new Vector3(vx1*cellSize,h11,vy1*cellSize));

            uvs.Add(new Vector2(vx0,vy0)); uvs.Add(new Vector2(vx1,vy0));
            uvs.Add(new Vector2(vx0,vy1)); uvs.Add(new Vector2(vx1,vy1));

            bool anyWall = vertSubmesh[vx0,vy0]==0||vertSubmesh[vx1,vy0]==0||
                           vertSubmesh[vx0,vy1]==0||vertSubmesh[vx1,vy1]==0;
            var cell=MapData.Cells[gx,gy];
            int sub=anyWall?0:1+(int)cell.zone;
            var tl=tris[sub];

            if (Mathf.Abs(h00-h11)<=Mathf.Abs(h10-h01))
            { tl.Add(idx);tl.Add(idx+2);tl.Add(idx+1);
              tl.Add(idx+1);tl.Add(idx+2);tl.Add(idx+3); }
            else
            { tl.Add(idx);tl.Add(idx+3);tl.Add(idx+1);
              tl.Add(idx);tl.Add(idx+2);tl.Add(idx+3); }
        }

        if (verts.Count==0) return;

        var mesh=new Mesh();
        mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts); mesh.SetUVs(0,uvs);
        mesh.subMeshCount=4;
        for (int s=0;s<4;s++) mesh.SetTriangles(tris[s],s);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();

        var go=new GameObject($"Chunk_{chunkX}_{chunkY}");
        go.transform.SetParent(transform); spawnedObjects.Add(go);
        go.AddComponent<MeshFilter>().mesh=mesh;
        var mr=go.AddComponent<MeshRenderer>();
        mr.materials=new[]{wallMaterial,floorShallowMaterial,
                            floorMidMaterial,floorDeepMaterial};
        go.AddComponent<MeshCollider>().sharedMesh=mesh;
    }

    float SampleNoise(float wx, float wz)
    {
        float val=0,amp=mesaTopNoise,freq=1f/mesaNoiseScale;
        for (int o=0;o<mesaNoiseOctaves;o++)
        {
            val+=Mathf.PerlinNoise(wx*freq+o*31.7f,wz*freq+o*17.3f)*amp;
            amp*=0.5f; freq*=2.1f;
        }
        return val;
    }
}

// using UnityEngine;
// using System.Collections.Generic;

// [RequireComponent(typeof(MapGenerator))]
// public class MeshBuilder : MonoBehaviour
// {
//     [Header("Materials")]
//     public Material wallMaterial;
//     public Material floorShallowMaterial;
//     public Material floorMidMaterial;
//     public Material floorDeepMaterial;

//     [Header("Settings")]
//     public float cellSize = 1f;
//     public int chunkSize = 16;

//     [Header("Wall Shape")]
//     public float wallTopNoise     = 2.5f;
//     public float wallNoiseScale   = 8f;
//     public int   wallNoiseOctaves = 3;
//     public float wallHeightBoost  = 1f;
//     public int   wallSmoothPasses = 8;

//     private List<GameObject> spawnedObjects = new List<GameObject>();
//     private float[,] vertHeight;
//     private int[,]   vertSubmesh;

//     public void Build()
//     {
//         foreach (var go in spawnedObjects) Destroy(go);
//         spawnedObjects.Clear();
//         BuildVertexGrid();
//         int cx = Mathf.CeilToInt((float)MapData.Width  / chunkSize);
//         int cy = Mathf.CeilToInt((float)MapData.Height / chunkSize);
//         for (int x = 0; x < cx; x++)
//         for (int y = 0; y < cy; y++)
//             BuildChunk(x, y);
//     }

//     void BuildVertexGrid()
//     {
//         int gw = MapData.Width  + 1;
//         int gh = MapData.Height + 1;
//         vertHeight  = new float[gw, gh];
//         vertSubmesh = new int[gw, gh];

//         // Track per-vertex whether it sits on floor or wall
//         // and what the floor height should be separately from wall height
//         float[,] floorOnly = new float[gw, gh]; // pure floor heights, no wall influence
//         bool[,]  isWall    = new bool[gw, gh];

//         // Step 1: classify every vertex and get its base height
//         for (int vx = 0; vx < gw; vx++)
//         for (int vy = 0; vy < gh; vy++)
//         {
//             float floorSum = 0f; int floorCount = 0;
//             float wallTop  = float.MinValue;
//             int   wallCount = 0;
//             DepthZone zone = DepthZone.Shallow;

//             for (int dx = -1; dx <= 0; dx++)
//             for (int dy = -1; dy <= 0; dy++)
//             {
//                 var c = MapData.Get(vx+dx, vy+dy);
//                 if (c == null) continue;
//                 if (!c.isWall)
//                 {
//                     floorSum += c.height;
//                     floorCount++;
//                     zone = c.zone;
//                 }
//                 else
//                 {
//                     wallTop = Mathf.Max(wallTop, c.height + c.wallHeight);
//                     wallCount++;
//                 }
//             }

//             bool vertWall = wallCount > 0; // wall if ANY neighbor is wall
//             isWall[vx, vy] = vertWall;
//             vertSubmesh[vx, vy] = vertWall ? 0 : 1 + (int)zone;

//             float floorH = floorCount > 0 ? floorSum / floorCount : 0f;
//             floorOnly[vx, vy] = floorH;

//             if (vertWall)
//             {
//                 float wx = vx * cellSize;
//                 float wz = vy * cellSize;
//                 // Wall height = wall top + boulder noise
//                 vertHeight[vx, vy] = wallTop + SampleNoise(wx, wz) + wallHeightBoost;
//             }
//             else
//             {
//                 vertHeight[vx, vy] = floorH;
//             }
//         }

//         // Step 2: smooth wall heights only (rounds boulder tops)
//         for (int pass = 0; pass < wallSmoothPasses; pass++)
//         {
//             float[,] next = new float[gw, gh];
//             for (int vx = 0; vx < gw; vx++)
//             for (int vy = 0; vy < gh; vy++)
//             {
//                 if (!isWall[vx, vy])
//                 {
//                     next[vx, vy] = vertHeight[vx, vy];
//                     continue;
//                 }
//                 float wsum = vertHeight[vx, vy] * 4f;
//                 float wtot = 4f;
//                 for (int dx = -1; dx <= 1; dx++)
//                 for (int dy = -1; dy <= 1; dy++)
//                 {
//                     if (dx==0 && dy==0) continue;
//                     int nx = vx+dx, ny = vy+dy;
//                     if (nx<0||ny<0||nx>=gw||ny>=gh) continue;
//                     float w = (dx==0||dy==0) ? 1f : 0.5f;
//                     wsum += vertHeight[nx, ny] * w;
//                     wtot += w;
//                 }
//                 next[vx, vy] = wsum / wtot;
//             }
//             vertHeight = next;
//         }

//         // Step 3: CRITICAL — smooth floor vertices independently
//         // Average only from neighbouring FLOOR vertices
//         // This prevents floor dipping down near walls (the spike cause)
//         float[,] smoothedFloor = (float[,])floorOnly.Clone();
//         for (int pass = 0; pass < 4; pass++)
//         {
//             float[,] next = new float[gw, gh];
//             for (int vx = 0; vx < gw; vx++)
//             for (int vy = 0; vy < gh; vy++)
//             {
//                 if (isWall[vx, vy]) { next[vx, vy] = smoothedFloor[vx, vy]; continue; }

//                 float sum = smoothedFloor[vx, vy] * 2f; // weight center more
//                 float tot = 2f;
//                 for (int dx = -1; dx <= 1; dx++)
//                 for (int dy = -1; dy <= 1; dy++)
//                 {
//                     if (dx==0 && dy==0) continue;
//                     int nx = vx+dx, ny = vy+dy;
//                     if (nx<0||ny<0||nx>=gw||ny>=gh) continue;
//                     // Only average with other floor verts — ignore wall heights
//                     if (isWall[nx, ny]) continue;
//                     float w = (dx==0||dy==0) ? 1f : 0.5f;
//                     sum += smoothedFloor[nx, ny] * w;
//                     tot += w;
//                 }
//                 next[vx, vy] = tot > 0 ? sum / tot : smoothedFloor[vx, vy];
//             }
//             smoothedFloor = next;
//         }

//         // Step 4: apply smoothed floor heights back, clamped so they
//         // never go BELOW the minimum floor height of any adjacent cell
//         for (int vx = 0; vx < gw; vx++)
//         for (int vy = 0; vy < gh; vy++)
//         {
//             if (isWall[vx, vy]) continue;

//             // Find the minimum floor height among surrounding floor cells
//             float minNeighborFloor = float.MaxValue;
//             for (int dx = -1; dx <= 1; dx++)
//             for (int dy = -1; dy <= 1; dy++)
//             {
//                 int nx = vx+dx, ny = vy+dy;
//                 if (nx<0||ny<0||nx>=gw||ny>=gh) continue;
//                 if (!isWall[nx, ny])
//                     minNeighborFloor = Mathf.Min(minNeighborFloor, smoothedFloor[nx, ny]);
//             }

//             // Clamp: floor vertex can never go below its lowest floor neighbor
//             // This is what kills the downward spikes at wall bases
//             vertHeight[vx, vy] = Mathf.Max(smoothedFloor[vx, vy],
//                 minNeighborFloor > float.MaxValue * 0.5f ? smoothedFloor[vx, vy] : minNeighborFloor);
//         }
//     }

//     void BuildChunk(int chunkX, int chunkY)
//     {
//         int startX = chunkX * chunkSize;
//         int startY = chunkY * chunkSize;

//         var verts = new List<Vector3>();
//         var uvs   = new List<Vector2>();
//         var tris  = new[] { new List<int>(), new List<int>(),
//                             new List<int>(), new List<int>() };

//         for (int lx = 0; lx < chunkSize; lx++)
//         for (int ly = 0; ly < chunkSize; ly++)
//         {
//             int gx = startX + lx;
//             int gy = startY + ly;
//             if (gx >= MapData.Width || gy >= MapData.Height) continue;

//             int vx0=gx, vx1=gx+1, vy0=gy, vy1=gy+1;

//             float h00 = vertHeight[vx0, vy0];
//             float h10 = vertHeight[vx1, vy0];
//             float h01 = vertHeight[vx0, vy1];
//             float h11 = vertHeight[vx1, vy1];

//             int idx = verts.Count;
//             verts.Add(new Vector3(vx0*cellSize, h00, vy0*cellSize));
//             verts.Add(new Vector3(vx1*cellSize, h10, vy0*cellSize));
//             verts.Add(new Vector3(vx0*cellSize, h01, vy1*cellSize));
//             verts.Add(new Vector3(vx1*cellSize, h11, vy1*cellSize));

//             uvs.Add(new Vector2(vx0, vy0)); uvs.Add(new Vector2(vx1, vy0));
//             uvs.Add(new Vector2(vx0, vy1)); uvs.Add(new Vector2(vx1, vy1));

//             bool anyWall = vertSubmesh[vx0,vy0]==0 || vertSubmesh[vx1,vy0]==0 ||
//                            vertSubmesh[vx0,vy1]==0 || vertSubmesh[vx1,vy1]==0;

//             var cell = MapData.Cells[gx, gy];
//             int sub  = anyWall ? 0 : 1 + (int)cell.zone;
//             var tl   = tris[sub];

//             if (Mathf.Abs(h00-h11) <= Mathf.Abs(h10-h01))
//             {
//                 tl.Add(idx);   tl.Add(idx+2); tl.Add(idx+1);
//                 tl.Add(idx+1); tl.Add(idx+2); tl.Add(idx+3);
//             }
//             else
//             {
//                 tl.Add(idx);   tl.Add(idx+3); tl.Add(idx+1);
//                 tl.Add(idx);   tl.Add(idx+2); tl.Add(idx+3);
//             }
//         }

//         if (verts.Count == 0) return;

//         var mesh = new Mesh();
//         mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
//         mesh.SetVertices(verts);
//         mesh.SetUVs(0, uvs);
//         mesh.subMeshCount = 4;
//         for (int s = 0; s < 4; s++) mesh.SetTriangles(tris[s], s);
//         mesh.RecalculateNormals();
//         mesh.RecalculateBounds();

//         var go = new GameObject($"Chunk_{chunkX}_{chunkY}");
//         go.transform.SetParent(transform);
//         spawnedObjects.Add(go);

//         go.AddComponent<MeshFilter>().mesh = mesh;
//         var mr = go.AddComponent<MeshRenderer>();
//         mr.materials = new[] { wallMaterial, floorShallowMaterial,
//                                 floorMidMaterial, floorDeepMaterial };
//         go.AddComponent<MeshCollider>().sharedMesh = mesh;
//     }

//     float SampleNoise(float wx, float wz)
//     {
//         float val=0, amp=wallTopNoise, freq=1f/wallNoiseScale;
//         for (int o=0; o<wallNoiseOctaves; o++)
//         {
//             val  += Mathf.PerlinNoise(wx*freq+o*31.7f, wz*freq+o*17.3f) * amp;
//             amp  *= 0.5f; freq *= 2.1f;
//         }
//         return val;
//     }
// }
