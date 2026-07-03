using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class ModelManager : MonoBehaviour
{
    [Header("First Setup")]
    public string fbxfilepath;//models/fbxmodel
    public Material material;
    public Transform renderCamera;

    [Header("Target Rendering Surface")]
    public MeshRenderer targetQuad;

    [Header("Object rotations")]
    [UnityEngine.Range(0.0f, 360.0f)]
    public float Pitch = 0.0f;
    [UnityEngine.Range(0.0f, 360.0f)]
    public float Yaw = 0.0f;
    [UnityEngine.Range(0.0f, 360.0f)]
    public float Roll = 0.0f;
    [Header("Object Position")]
    [UnityEngine.Range(-100.0f, 100.0f)]
    [Tooltip("right-left")]
    public float x = 0.0f;
    [UnityEngine.Range(-100.0f, 100.0f)]
    [Tooltip("up-down")]
    public float y = 0.0f;
    [UnityEngine.Range(-100.0f, 100.0f)]
    [Tooltip("inward-outward")]
    public float z = 0.0f;

    private Material runtimeMaterial;
    private ComputeBuffer triangleBuffer;

    //bvh setup
    private ComputeBuffer bvhtrianlgebuffer;
    private ComputeBuffer bvhindicesbuffer;
    private ComputeBuffer bvhnodesbuffer;

    //bvh boxes 
    BVHNode[] bvhNodes;
    Tri[] bvhTriangles;
    int[] triIndices;

    uint nodesUsed;
    uint rootNodeIdx;

    //sah optimization
    const int BINS = 16;
    Bin[] bins = new Bin[BINS];

    

  

    struct aabb
    {
        public float3 bmin;
        public float3 bmax;
        public aabb(float3 positive, float3 negative)
        {
            bmin = positive;
            bmax = negative;
        }
        public void grow(float3 p)
        {
            bmin = math.min(bmin, p);
            bmax = math.max(bmax, p);
        }
        public float area()
        {
            float3 e = bmax - bmin;
            if (e.x < 0 || e.y < 0 || e.z < 0) return 0; // if uninitilized bounds 
            return e.x * e.y + e.y * e.z + e.z * e.x;
        }
    }
    struct Bin
    {
        public aabb bounds;
        public int triCount;
    }

    struct Tri
    {
        public Vector3 v0, v1, v2;
        public float3 centroid;
    }
    struct BVHNode
    {
        public float3 min;
        public float3 max;

        public uint leftfirst;
        public uint tricount;

        public bool isleaf()
        {
            return tricount > 0;
        }
    }


    void Start()
    {
        if (targetQuad == null)
        {
            Debug.LogError($"No Quad assigned to {gameObject.name}! Drag the Quad into the Target Quad slot.");
            return;
        }
        runtimeMaterial = new Material(material);
        targetQuad.material = runtimeMaterial;

        GameObject fbxPrefab = Resources.Load<GameObject>(fbxfilepath);
        if (fbxPrefab != null)
        {
            MeshFilter mf = fbxPrefab.GetComponentInChildren<MeshFilter>();
            Renderer r = fbxPrefab.GetComponentInChildren<Renderer>();
            if (mf != null && r != null)
            {
                UploadMesh(mf.sharedMesh);
                Texture tex = r.sharedMaterial.mainTexture;

                runtimeMaterial.SetTexture("_MainTex", tex);
            }
            else Debug.LogError("No MeshFilter found in FBX prefab");
        }
        else Debug.LogError("FBX prefab not found in Resources");

       
    }

    void BuildBVH(uint trianglecount)
    {
        bvhNodes = new BVHNode[trianglecount * 2 - 1];

        nodesUsed = 1;
        rootNodeIdx = 0;

        bvhNodes[(int)rootNodeIdx].leftfirst = 0;
        bvhNodes[(int)rootNodeIdx].tricount = trianglecount;

        UpdateNodeBounds(rootNodeIdx);
        Subdivide(rootNodeIdx);
    }

    void UpdateNodeBounds(uint nodeidx)
    {
        BVHNode node = bvhNodes[nodeidx];

        node.min = new float3(float.PositiveInfinity);
        node.max = new float3(float.NegativeInfinity);

        for (int i = 0; i < node.tricount; i++)
        {
            int triIndex = triIndices[node.leftfirst + i];
            Tri t = bvhTriangles[triIndex];

            node.min = math.min(node.min, t.v0);
            node.min = math.min(node.min, t.v1);
            node.min = math.min(node.min, t.v2);

            node.max = math.max(node.max, t.v0);
            node.max = math.max(node.max, t.v1);
            node.max = math.max(node.max, t.v2);
        }

        bvhNodes[nodeidx] = node;
    }

    void Subdivide(uint nodeIdx)
    {
        BVHNode node = bvhNodes[nodeIdx];

        if (node.tricount <= 2)
            return;

        int bestAxis = -1;
        float bestPos = 0.0f;
        float SplitCost = FindBestSplitPlane(ref node, ref bestAxis, ref bestPos);

        float currentCost = node.tricount * CalculateNodeCost(ref node);
        if (SplitCost >= currentCost || bestAxis < 0)
            return;

        int axis = bestAxis;
        float splitPos = bestPos;

        int i = (int)node.leftfirst;
        int j = i + (int)node.tricount - 1;

        while (i <= j)
        {
            int triIndex = triIndices[i];
            Vector3 c = bvhTriangles[triIndex].centroid;

            if (c[axis] < splitPos)
            {
                i++;
            }
            else
            {
                int tmp = triIndices[i];
                triIndices[i] = triIndices[j];
                triIndices[j] = tmp;
                j--;
            }
        }

        int leftCount = i - (int)node.leftfirst;

        if (leftCount == 0 || leftCount == node.tricount)
            return;

        int leftChild = (int)nodesUsed++;
        int rightChild = (int)nodesUsed++;

        bvhNodes[leftChild].leftfirst = node.leftfirst;
        bvhNodes[leftChild].tricount = (uint)leftCount;

        bvhNodes[rightChild].leftfirst = (uint)i;
        bvhNodes[rightChild].tricount = node.tricount - (uint)leftCount;

        node.leftfirst = (uint)leftChild;
        node.tricount = 0;

        bvhNodes[nodeIdx] = node;

        UpdateNodeBounds((uint)leftChild);
        UpdateNodeBounds((uint)rightChild);

        Subdivide((uint)leftChild);
        Subdivide((uint)rightChild);
    }

    float FindBestSplitPlane(ref BVHNode node, ref int axis, ref float splitPos)
    {
        float BestCost = float.PositiveInfinity;

        Span<float> leftArea = stackalloc float[BINS];
        Span<float> leftCount = stackalloc float[BINS];
        Span<float> rightArea = stackalloc float[BINS];
        Span<float> rightCount = stackalloc float[BINS];

        for (int a = 0; a < 3; a++)
        {
            float boundsMin = float.PositiveInfinity;
            float boundsMax = float.NegativeInfinity;

            for (int i = 0; i < node.tricount; i++)
            {
                Tri triangle = bvhTriangles[triIndices[node.leftfirst + i]];
                boundsMin = math.min(boundsMin, triangle.centroid[a]);
                boundsMax = math.max(boundsMax, triangle.centroid[a]);
            }
            if (boundsMin == boundsMax) continue;

            for (int i = 0; i < BINS; i++)
            {
                bins[i].triCount = 0;
                bins[i].bounds = new aabb(
                    new float3(float.PositiveInfinity),
                    new float3(float.NegativeInfinity)
                );
            }

            float scale = BINS / (boundsMax - boundsMin);
            for (uint i = 0; i < node.tricount; i++)
            {
                Tri triangle = bvhTriangles[triIndices[node.leftfirst + i]];
                int binIdx = math.min(BINS - 1, (int)((triangle.centroid[a] - boundsMin) * scale));
                bins[binIdx].triCount++;
                bins[binIdx].bounds.grow(triangle.v0);
                bins[binIdx].bounds.grow(triangle.v1);
                bins[binIdx].bounds.grow(triangle.v2);
            }

            aabb leftBox = new aabb(new float3(float.PositiveInfinity), new float3(float.NegativeInfinity));
            aabb rightBox = new aabb(new float3(float.PositiveInfinity), new float3(float.NegativeInfinity));
            int leftSum = 0;
            int rightSum = 0;
            for (int i = 0; i < BINS - 1; i++)
            {
                leftSum += bins[i].triCount;
                leftCount[i] = leftSum;
                if (bins[i].triCount > 0)
                {
                    leftBox.grow(bins[i].bounds.bmin);
                    leftBox.grow(bins[i].bounds.bmax);
                }
                leftArea[i] = leftBox.area();

                rightSum += bins[BINS - 1 - i].triCount;
                rightCount[BINS - 2 - i] = rightSum;
                if (bins[i].triCount > 0)
                {
                    rightBox.grow(bins[BINS - 1 - i].bounds.bmin);
                    rightBox.grow(bins[BINS - 1 - i].bounds.bmax);
                }
                rightArea[BINS - 2 - i] = rightBox.area();
            }

            scale = (boundsMax - boundsMin) / BINS;
            for (int i = 0; i < BINS - 1; i++)
            {
                if (leftCount[i] == 0 || rightCount[i] == 0) continue;

                float planeCost = leftCount[i] * leftArea[i] + rightCount[i] * rightArea[i];
                if (planeCost < BestCost)
                {
                    axis = a;
                    splitPos = boundsMin + scale * (i + 1);
                    BestCost = planeCost;
                }
            }
        }
        return BestCost;
    }

    float CalculateNodeCost(ref BVHNode node)
    {
        float3 e = node.max - node.min;
        float surfaceArea = e.x * e.y + e.y * e.z + e.z * e.x;
        return node.tricount * surfaceArea;
    }

    struct Triangle
    {
        public Vector3 a, b, c;      // 48 bytes  
        public Vector2 uva, uvb, uvc; // 48 bytes -> 96 bytes total
    }


    private struct bvhBufferData
    {
        public BVHNode[] bvhNodes;
        public Tri[] bvhTriangles;
        public int[] triIndices;
        public Triangle[] triArray;
        public Vector3[] vertices;
    }

    static private Dictionary<Mesh, bvhBufferData> bvhCache = new Dictionary<Mesh, bvhBufferData>();
    void UploadMesh(Mesh mesh)
    {
        var verts = mesh.vertices;
        var norms = mesh.normals;
        var uvs = mesh.uv;
        var tris = mesh.triangles;

        Vector3 rotationAngles = new Vector3(Pitch, Yaw, Roll);
        Vector3 PositionOffset = new Vector3(x, y, z);

        Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(rotationAngles), Vector3.one);


        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = matrix.MultiplyPoint3x4(verts[i]);
        }

        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] += PositionOffset;
        }



        Triangle[] triArray;
        if (bvhCache.TryGetValue(mesh, out bvhBufferData cachedBvh))
        {
            bvhTriangles = cachedBvh.bvhTriangles;
            triIndices = cachedBvh.triIndices;
            triArray = cachedBvh.triArray;
            bvhNodes = cachedBvh.bvhNodes;
            verts = cachedBvh.vertices;
        }
        else
        {
            bvhTriangles = new Tri[tris.Length / 3];
            triIndices = new int[tris.Length / 3];
            for (int i = 0; i < tris.Length / 3; i++)
            {
                int id = i * 3;

                bvhTriangles[i].v0 = verts[tris[id]];
                bvhTriangles[i].v1 = verts[tris[id + 1]];
                bvhTriangles[i].v2 = verts[tris[id + 2]];

                bvhTriangles[i].centroid =
                    (bvhTriangles[i].v0 +
                     bvhTriangles[i].v1 +
                     bvhTriangles[i].v2) / 3f;

                triIndices[i] = i;
            }
            BuildBVH((uint)tris.Length / 3);

            triArray = new Triangle[tris.Length / 3];

            for (int t = 0; t < triArray.Length; t++)
            {
                int i = t * 3;
                triArray[t] = new Triangle()
                {
                    a = new Vector3(verts[tris[i]].x, verts[tris[i]].y, verts[tris[i]].z),
                    b = new Vector3(verts[tris[i + 1]].x, verts[tris[i + 1]].y, verts[tris[i + 1]].z),
                    c = new Vector3(verts[tris[i + 2]].x, verts[tris[i + 2]].y, verts[tris[i + 2]].z),

                    uva = new Vector2(uvs[tris[i]].x, uvs[tris[i]].y),
                    uvb = new Vector2(uvs[tris[i + 1]].x, uvs[tris[i + 1]].y),
                    uvc = new Vector2(uvs[tris[i + 2]].x, uvs[tris[i + 2]].y)
                };
            }
            bvhCache[mesh] = new bvhBufferData()
            {
                bvhNodes = bvhNodes,
                bvhTriangles = bvhTriangles,
                triIndices = triIndices,
                triArray = triArray,
                vertices = verts
            };

        }
        if (triangleBuffer != null)
            triangleBuffer.Release();



        triangleBuffer = new ComputeBuffer(triArray.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Triangle)));
        triangleBuffer.SetData(triArray);

        bvhtrianlgebuffer = new ComputeBuffer(bvhTriangles.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Tri)));
        bvhtrianlgebuffer.SetData(bvhTriangles);

        bvhindicesbuffer = new ComputeBuffer(triIndices.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(int)));
        bvhindicesbuffer.SetData(triIndices);

        bvhnodesbuffer = new ComputeBuffer(bvhNodes.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(BVHNode)));
        bvhnodesbuffer.SetData(bvhNodes);

        runtimeMaterial.SetBuffer("_BvhTriangles", bvhtrianlgebuffer);
        runtimeMaterial.SetBuffer("_BvhIndices", bvhindicesbuffer);
        runtimeMaterial.SetBuffer("_BvhNodes", bvhnodesbuffer);

        runtimeMaterial.SetBuffer("_Triangles", triangleBuffer);
        runtimeMaterial.SetInt("_TriangleCount", triArray.Length);
    }

    void Update()
    {
        if (material != null && renderCamera != null)
        {
            runtimeMaterial.SetVector("_CameraPos", renderCamera.transform.position);


            Matrix4x4 mat = Matrix4x4.TRS(targetQuad.transform.position, targetQuad.transform.rotation, Vector3.one);
            runtimeMaterial.SetMatrix("_BehindTheQuadSpace", mat.inverse);
        }


    }

    private void OnDestroy()
    {
        triangleBuffer?.Release();
        bvhtrianlgebuffer?.Release();
        bvhindicesbuffer?.Release();
        bvhnodesbuffer?.Release();
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }
}
