using System;
using System.Collections.Generic;
using Common.Unity.Drawing;
using MarchingCubesProject;
using ProceduralNoiseProject;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
public class GPUGemsTerrain : MonoBehaviour {
    public enum NoiseType {
        Perlin2D,
        Perlin2DUnity,
        Voronoi2D,
        Simplex2D,
        Value2D,
        Worley2D,
        Perlin3D,
        Voronoi3D,
        Simplex3D,
        Value3D,
        Worley3D
    }

    [Range (8, 64)] public int resolution = 32;
    public Vector3Int size = Vector3Int.one;
    public Material material;
    public MARCHING_MODE mode = MARCHING_MODE.CUBES;
    public float baseHeight, hardFloor = -13, hardFloor2 = 3, hardFloor3 = 40, lacunarity = 2.0f, persistence = 0.5f;
    public List<Octave> octaves;
    [System.Serializable]
    public class Octave {
        public bool enabled = true;
        public int seed = 0;
        public NoiseType noiseType;
        public float frequency = 1, amplitude = 1, terrace = 0.5f;
        public float redistribution = 1f;
        public FractalNoise fractal;
        public bool twoDee { get; set; }
    }
    public bool smoothNormals = false;
    public bool drawNormals = false;
    private List<GameObject> meshes = new List<GameObject> ();
    private NormalRenderer normalRenderer;
    public float3 offset;
    FractalNoise densityFractal;

    void Start () {
        //GenerateMesh ();
    }
    public void GenerateMesh () {
        foreach (var o in octaves) {
            INoise selectedInoise = GenerateNewNoise (o);
            o.fractal = new FractalNoise (selectedInoise, 1, 1.0f);
        }
        //Set the mode used to create the mesh.
        //Cubes is faster and creates less verts, tetrahedrons is slower and creates more verts but better represents the mesh surface.
        Marching marching = null;
        if (mode == MARCHING_MODE.TETRAHEDRON)
            marching = new MarchingTertrahedron ();
        else
            marching = new MarchingCubes ();
        //Surface is the value that represents the surface of mesh
        //For example the perlin noise has a range of -1 to 1 so the mid point is where we want the surface to cut through.
        //The target value does not have to be the mid point it can be any value with in the range.
        marching.Surface = 0.0f;
        //The size of voxel array.
        int width = resolution * size.x;
        int height = resolution * size.y;
        int depth = resolution * size.z;
        var voxels = new VoxelArray (width, height, depth);
        //Fill voxels with values. Im using perlin noise but any method to create voxels will work.
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                for (int z = 0; z < depth; z++) {
                    float density = Density (x, y, z, width, height, depth);
                    voxels[x, y, z] = density;
                }
            }
        }
        List<Vector3> verts = new List<Vector3> ();
        List<Vector3> normals = new List<Vector3> ();
        List<int> indices = new List<int> ();
        //The mesh produced is not optimal. There is one vert for each index.
        //Would need to weld vertices for better quality mesh.
        marching.Generate (voxels.Voxels, verts, indices);
        float densityFactor = 32.0f / (resolution);
        for (int i = 0; i < verts.Count; i++)
            verts[i] *= densityFactor;
        //Create the normals from the voxel.
        if (smoothNormals) {
            for (int i = 0; i < verts.Count; i++) {
                //Presumes the vertex is in local space where
                //the min value is 0 and max is width/height/depth.
                Vector3 p = verts[i];
                float u = p.x / (width - 1.0f);
                float v = p.y / (height - 1.0f);
                float w = p.z / (depth - 1.0f);
                Vector3 n = voxels.GetNormal (u, v, w);
                normals.Add (n);
            }
            normalRenderer = new NormalRenderer ();
            normalRenderer.DefaultColor = Color.red;
            normalRenderer.Length = 0.25f;
            normalRenderer.Load (verts, normals);
        }
        var position = new Vector3 (-width / 2f, -height / 2f, -depth / 2f);
        CreateMesh32 (verts, normals, indices, position);
    }
    public float Density (int x, int y, int z, int width, int height, int depth) {
        float u = x / (width - 1.0f);
        float v = y / (height - 1.0f);
        float w = z / (depth - 1.0f);
        float3 ws = new float3 (u * size.x, (v + baseHeight) * size.y, w * size.z);
        ws += new float3 (offset);
        float density = -ws.y;
        for (int i = 0; i < octaves.Count; i++) {
            if (!octaves[i].enabled) {
                continue;
            }
            densityFractal = octaves[i].fractal;
            float frequency = octaves[i].frequency * Mathf.Pow (lacunarity, i);
            float amplitude = octaves[i].amplitude * Mathf.Pow (persistence, i);
            float noiseSample = octaves[i].noiseType == NoiseType.Perlin2DUnity ? Mathf.PerlinNoise (ws.x * frequency, ws.z * frequency) : (octaves[i].twoDee? densityFractal.Sample2D (ws.x * frequency, ws.z * frequency) : densityFractal.Sample3D (ws * frequency));
            density += Mathf.Round (math.pow ((noiseSample * amplitude), octaves[i].redistribution) * octaves[i].terrace) / octaves[i].terrace;
        }
        float hard_floor_y = hardFloor;
        density += math.saturate ((hard_floor_y - ws.y) * hardFloor2) * hardFloor3;
        return density;
    }
    public INoise GenerateNewNoise (Octave octave) {
        octave.twoDee = false;
        INoise selectedInoise = null;
        switch (octave.noiseType) {
            case NoiseType.Perlin2D:
                INoise perlin2D = new PerlinNoise (octave.seed, 1.0f);
                selectedInoise = perlin2D;
                octave.twoDee = true;
                break;
            case NoiseType.Perlin3D:
                INoise perlin3D = new PerlinNoise (octave.seed, 1.0f);
                selectedInoise = perlin3D;
                break;
            case NoiseType.Voronoi2D:
                INoise voronoi2D = new VoronoiNoise (octave.seed, 1.0f);
                selectedInoise = voronoi2D;
                octave.twoDee = true;
                break;
            case NoiseType.Voronoi3D:
                INoise voronoi3D = new VoronoiNoise (octave.seed, 1.0f);
                selectedInoise = voronoi3D;
                break;
            case NoiseType.Simplex2D:
                INoise simplex2D = new SimplexNoise (octave.seed, 1.0f);
                selectedInoise = simplex2D;
                octave.twoDee = true;
                break;
            case NoiseType.Simplex3D:
                INoise simplex3D = new SimplexNoise (octave.seed, 1.0f);
                selectedInoise = simplex3D;
                break;
            case NoiseType.Value2D:
                INoise value2D = new ValueNoise (octave.seed, 1.0f);
                selectedInoise = value2D;
                octave.twoDee = true;
                break;
            case NoiseType.Value3D:
                INoise value3D = new ValueNoise (octave.seed, 1.0f);
                selectedInoise = value3D;
                break;
            case NoiseType.Worley2D:
                INoise worley2D = new WorleyNoise (octave.seed, 1.0f, 0, 1);
                selectedInoise = worley2D;
                octave.twoDee = true;
                break;
            case NoiseType.Worley3D:
                INoise worley3D = new WorleyNoise (octave.seed, 1.0f, 0, 1);
                selectedInoise = worley3D;
                break;
        }
        return selectedInoise;
    }
    private void CreateMesh32 (List<Vector3> verts, List<Vector3> normals, List<int> indices, Vector3 position) {
        Mesh mesh = new Mesh ();
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices (verts);
        mesh.SetTriangles (indices, 0);
        if (normals.Count > 0)
            mesh.SetNormals (normals);
        else
            mesh.RecalculateNormals ();
        mesh.RecalculateBounds ();
        DestroyChildren ();
        GameObject go = new GameObject ("Mesh");
        go.transform.parent = transform;
        go.AddComponent<MeshFilter> ();
        go.AddComponent<MeshRenderer> ();
        go.GetComponent<Renderer> ().material = material;
        go.GetComponent<MeshFilter> ().mesh = mesh;
        go.transform.localPosition = position;
        meshes.Add (go);
    }
    /// <summary>
    /// UPDATE - Unity now supports 32 bit indices so the method is optional.
    /// 
    /// A mesh in unity can only be made up of 65000 verts.
    /// Need to split the verts between multiple meshes.
    /// </summary>
    /// <param name="verts"></param>
    /// <param name="normals"></param>
    /// <param name="indices"></param>
    /// <param name="position"></param>
    private void CreateMesh16 (List<Vector3> verts, List<Vector3> normals, List<int> indices, Vector3 position) {
        int maxVertsPerMesh = 30000; //must be divisible by 3, ie 3 verts == 1 triangle
        int numMeshes = verts.Count / maxVertsPerMesh + 1;
        for (int i = 0; i < numMeshes; i++) {
            List<Vector3> splitVerts = new List<Vector3> ();
            List<Vector3> splitNormals = new List<Vector3> ();
            List<int> splitIndices = new List<int> ();
            for (int j = 0; j < maxVertsPerMesh; j++) {
                int idx = i * maxVertsPerMesh + j;
                if (idx < verts.Count) {
                    splitVerts.Add (verts[idx]);
                    splitIndices.Add (j);
                    if (normals.Count != 0)
                        splitNormals.Add (normals[idx]);
                }
            }
            if (splitVerts.Count == 0) continue;
            Mesh mesh = new Mesh ();
            mesh.indexFormat = IndexFormat.UInt16;
            mesh.SetVertices (splitVerts);
            mesh.SetTriangles (splitIndices, 0);
            if (splitNormals.Count > 0)
                mesh.SetNormals (splitNormals);
            else
                mesh.RecalculateNormals ();
            mesh.RecalculateBounds ();
            DestroyChildren ();
            GameObject go = new GameObject ("Mesh");
            go.transform.parent = transform;
            go.AddComponent<MeshFilter> ();
            go.AddComponent<MeshRenderer> ();
            go.GetComponent<Renderer> ().material = material;
            go.GetComponent<MeshFilter> ().mesh = mesh;
            go.transform.localPosition = position;
            meshes.Add (go);
        }
    }
    public void DestroyChildren () {
        foreach (Transform child in gameObject.GetComponentsInChildren<Transform> ()) {
            if (child != transform) {
                if (Application.isPlaying) {
                    Destroy (child.gameObject);
                } else {
                    DestroyImmediate (child.gameObject);
                }
            }
        }
    }
    private void Update () {
        //transform.Rotate(Vector3.up, 10.0f * Time.deltaTime);
    }
    private void OnRenderObject () {
        if (normalRenderer != null && meshes.Count > 0 && drawNormals) {
            var m = meshes[0].transform.localToWorldMatrix;
            normalRenderer.LocalToWorld = m;
            normalRenderer.Draw ();
        }
    }
}