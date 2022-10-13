using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using static MapGraph;

public partial class MapGeneratorPreview : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] protected PreviewType previewType;

    [SerializeField] protected HeightMapSettings heightMapSettings;
    [SerializeField] protected int seed = 0;
    [SerializeField] public bool autoUpdate;

    [Header("Mesh Settings")]
    [SerializeField] protected int meshSize = 200;

    [Header("Texture Settings")]
    [SerializeField] protected int textureSize = 512;
    [SerializeField] protected bool drawNodeBoundries;
    [SerializeField] protected bool drawDelauneyTriangles;
    [SerializeField] protected bool drawNodeCenters;
    [SerializeField] protected bool drawPrefabs;

    [SerializeField] protected List<MapNodeTypeEntity> mapNodeSettings;
    [SerializeField] protected List<MapNodeTown> townModels;

    [Header("Voronoi Generation")]
    [SerializeField] protected PointGeneration pointGeneration;
    [SerializeField] protected int pointSpacing = 10;
    [SerializeField] protected int relaxationIterations = 1;
    [SerializeField] protected float snapDistance = 0;

    [Header("Outputs")]
    [SerializeField] protected MeshFilter meshFilter;
    [SerializeField] protected MeshRenderer meshRenderer;
    [SerializeField] protected MeshCollider meshCollider;
    protected Transform PrefabContainer;

    public void Start()
    {
        StartCoroutine(GenerateMapAsync());
    }

    public IEnumerator GenerateMapAsync()
    {
        yield return new WaitForSeconds(1f);
        GenerateMap();
    }

    public void GenerateMap()
    {

        var startTime = DateTime.Now;
        var points = GetPoints();

        var time = DateTime.Now;
        var voronoi = new Delaunay.Voronoi(points, null, new Rect(0, 0, meshSize, meshSize), relaxationIterations);
        Debug.Log(string.Format("Voronoi Generated: {0:n0}ms", DateTime.Now.Subtract(time).TotalMilliseconds));

        time = DateTime.Now;
        heightMapSettings.noiseSettings.seed = seed;
        var heightMap = HeightMapGenerator.GenerateHeightMap(meshSize, meshSize, heightMapSettings, Vector2.zero);
        Debug.Log(string.Format("Heightmap Generated: {0:n0}ms", DateTime.Now.Subtract(time).TotalMilliseconds));

        time = DateTime.Now;
        var mapGraph = new MapGraph(voronoi, heightMap, snapDistance);
        Debug.Log(string.Format("Finished Generating Map Graph: {0:n0}ms with {1} nodes", DateTime.Now.Subtract(startTime).TotalMilliseconds, mapGraph.nodesByCenterPosition.Count));

        time = DateTime.Now;
        MapGenerator.GenerateMap(mapGraph, heightMapSettings, seed, meshSize, mapNodeSettings);
        // Debug.Log(string.Format("Map Generated: {0:n0}ms", DateTime.Now.Subtract(time).TotalMilliseconds));


        if (previewType == PreviewType.HeightMap)
        {
            OnMeshDataReceived(MapMeshGenerator.GenerateMesh(mapGraph, heightMap, meshSize));
            UpdateTexture(TextureGenerator.TextureFromHeightMap(heightMap));
        }
        if (previewType == PreviewType.Map)
        {
            time = DateTime.Now;
            OnMeshDataReceived(MapMeshGenerator.GenerateMesh(mapGraph, heightMap, meshSize));
            Debug.Log(string.Format("Mesh Generated: {0:n0}ms", DateTime.Now.Subtract(time).TotalMilliseconds));

            time = DateTime.Now;
            var texture = MapTextureGenerator.GenerateTexture(mapGraph, seed, meshSize, textureSize, mapNodeSettings, drawNodeBoundries, drawDelauneyTriangles, drawNodeCenters, drawPrefabs);
            Debug.Log(string.Format("Texture Generated: {0:n0}ms", DateTime.Now.Subtract(time).TotalMilliseconds));

            UpdateTexture(texture);

            if (drawPrefabs)
            {


                if (GameObject.Find("PrefabContainer"))
                {
                    PrefabContainer = GameObject.Find("PrefabContainer").transform;
                }

                //create prefab containers
                if (PrefabContainer != null)
                {
                    DestroyImmediate(PrefabContainer.gameObject);
                }

                PrefabContainer = new GameObject(nameof(PrefabContainer)).transform;

                DrawPrefabs(mapGraph, seed, mapNodeSettings, townModels, PrefabContainer);
            }

        }

        Debug.Log(string.Format("Finished Generating World: {0:n0}ms with {1} nodes", DateTime.Now.Subtract(startTime).TotalMilliseconds, mapGraph.nodesByCenterPosition.Count));
    }

    private List<Vector2> GetPoints()
    {
        List<Vector2> points = null;
        if (pointGeneration == PointGeneration.Random)
        {
            points = VoronoiGenerator.GetVector2Points(seed, (meshSize / pointSpacing) * (meshSize / pointSpacing), meshSize);
        }
        else if (pointGeneration == PointGeneration.PoissonDisc)
        {
            var poisson = new PoissonDiscSampler(meshSize, meshSize, pointSpacing, seed);
            points = poisson.Samples().ToList();
        }
        else if (pointGeneration == PointGeneration.Grid)
        {
            points = new List<Vector2>();
            for (int x = pointSpacing; x < meshSize; x += pointSpacing)
            {
                for (int y = pointSpacing; y < meshSize; y += pointSpacing)
                {
                    points.Add(new Vector2(x, y));
                }
            }
        }
        else if (pointGeneration == PointGeneration.OffsetGrid)
        {
            points = new List<Vector2>();
            for (int x = pointSpacing; x < meshSize; x += pointSpacing)
            {
                bool even = false;
                for (int y = pointSpacing; y < meshSize; y += pointSpacing)
                {
                    var newX = even ? x : x - (pointSpacing / 2f);
                    points.Add(new Vector2(newX, y));
                    even = !even;
                }
            }
        }

        return points;
    }

    private void OnTextureDataReceived(object result)
    {
        var textureData = result as TextureData;
        UpdateTexture(textureData);
    }

    private void OnMeshDataReceived(object result)
    {
        var meshData = result as MeshData;
        UpdateMesh(meshData);
    }

    private void UpdateTexture(TextureData data)
    {
        var texture = new Texture2D(textureSize, textureSize);
        texture.SetPixels(data.colours);
        texture.Apply();
        UpdateTexture(texture);
    }

    // TODO: cut and paste into extension 
    /// <summary>
    /// Returns a random vector3 between min and max. (Inclusive)
    /// </summary>
    /// <returns>The <see cref="UnityEngine.Vector3"/>.</returns>
    /// <param name="min">Minimum.</param>
    /// <param name="max">Max.</param>
    /// https://gist.github.com/Ashwinning/269f79bef5b1d6ee1f83
    public static Vector3 GetRandomVector3Between(Vector3 min, Vector3 max)
    {
        return min + UnityEngine.Random.Range(0f, 1f) * (max - min);
    }

    /// <summary>
    /// Gets the random vector3 between the min and max points provided.
    /// Also uses minPadding and maxPadding (in metres).
    /// maxPadding is the padding amount to be added on the other Vector3's side.
    /// Setting minPadding and maxPadding to 0 will make it return inclusive values.
    /// </summary>
    /// <returns>The <see cref="UnityEngine.Vector3"/>.</returns>
    /// <param name="min">Minimum.</param>
    /// <param name="max">Max.</param>
    /// <param name="minPadding">Minimum padding.</param>
    /// <param name="maxPadding">Max padding.</param>
    /// https://gist.github.com/Ashwinning/269f79bef5b1d6ee1f83
    /// 
    public static Vector3 GetRandomVector3Between(Vector3 min, Vector3 max, float minPadding, float maxPadding)
    {
        //minpadding as a value between 0 and 1
        float distance = Vector3.Distance(min, max);
        Vector3 point1 = min + minPadding * (max - min);
        Vector3 point2 = max + maxPadding * (min - max);
        return GetRandomVector3Between(point1, -point2);
    }



    private static void DrawPrefabs(MapGraph map, int seed, List<MapNodeTypeEntity> mapNodeTypes, List<MapNodeTown> townModelsByPopulation, Transform container)
    {
        // mapNodeTypes
        // GL.Color();
        UnityEngine.Random.InitState(seed);

        foreach (MapNode point in map.nodesByCenterPosition.Values)
        {
            mapNodeTypes.ForEach((mapNodeAction) =>
            {
                if (mapNodeAction.type == point.nodeType)
                {
                    int modelListCount = mapNodeAction.modelList.Count();
                    if (modelListCount == 0) return;
                    var corners = point.GetCorners().ToArray();
                    var center = point.centerPoint;

                    for (int i = 0; i < corners.Count(); i++)
                    {
                        int RandomValue = UnityEngine.Random.Range(0, modelListCount);
                        var model = mapNodeAction.modelList[RandomValue];
                        var newPoint = GetRandomVector3Between(center, corners[i].position);
                        newPoint.y = newPoint.y + 0.5f;

                        var instance = Instantiate(model, newPoint, Quaternion.identity, container);
                    }
                }

                if (point.isCity)
                {
                    townModelsByPopulation.ForEach(townModelsByPopulationAction =>
                    {
                        if (
                            point.population >= townModelsByPopulationAction.minPopulation &&
                            point.population <= townModelsByPopulationAction.maxPopulation
                        )
                        {
                            int modelListCount = townModelsByPopulationAction.modelList.Count();
                            if (modelListCount == 0) return;
                            var corners = point.GetCorners().ToArray();
                            var center = point.centerPoint;

                            for (int i = 0; i < corners.Count(); i++)
                            {
                                int RandomValue = UnityEngine.Random.Range(0, modelListCount);
                                var model = mapNodeAction.modelList[RandomValue];
                                var newPoint = GetRandomVector3Between(center, corners[i].position);
                                newPoint.y = newPoint.y + 0.5f;

                                var instance = Instantiate(model, newPoint, Quaternion.identity, container);
                            }
                        }
                    });
                }
            });
        }
    }

    private void UpdateTexture(Texture2D texture)
    {
        meshRenderer.sharedMaterial.mainTexture = texture;
    }

    public void UpdateMesh(MeshData meshData)
    {
        var mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = meshData.vertices.ToArray();
        mesh.triangles = meshData.indices.ToArray();
        mesh.uv = meshData.uvs;
        mesh.RecalculateNormals();
        meshFilter.sharedMesh = mesh;
        meshCollider.sharedMesh = mesh;
    }

    void OnValidate()
    {
        if (heightMapSettings != null)
        {
            heightMapSettings.OnValuesUpdated -= OnValuesUpdated;
            heightMapSettings.OnValuesUpdated += OnValuesUpdated;
        }

    }

    private void OnValuesUpdated()
    {
        GenerateMap();
    }
}
