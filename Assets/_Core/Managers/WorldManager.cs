// Замените ваш WorldManager.cs на этот код
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public struct BiomeData
    {
        public float heightOffset;
        // В будущем добавим сюда и другие параметры
    }

public class WorldManager : MonoBehaviour
{
    public static WorldManager Instance;

    [Header("Настройки мира")]
    public Vector3Int chunkSize = new Vector3Int(48, 48, 48);
    public int viewDistance = 4;
    public float isoLevel = 0f;

    [Header("Настройки генерации")]
    public float surfaceLevel = 24f;
    public float noiseFrequency = 0.02f;
    public float noiseAmplitude = 15f;
    public float biomeMapScale = 0.01f;


    [Header("Настройки фрактального шума (fBm)")]
    [Tooltip("Множитель частоты для каждой следующей октавы (обычно > 1)")]
    public float lacunarity = 2.0f;
    [Tooltip("Множитель амплитуды для каждой следующей октавы (обычно < 1)")]
    public float persistence = 0.5f;

    [Header("Ссылки")]
    public GameObject chunkPrefab;
    public Transform player;
    public ComputeShader noiseGenerator;
    public Material worldMaterial;

    [Header("Биомы")]
    [Tooltip("Определения биомов, которые будут использоваться в мире.")]
    public BiomeDefinition[] biomeDefinitions;

    private readonly Dictionary<Vector3Int, Chunk> activeChunks = new Dictionary<Vector3Int, Chunk>();
    private readonly Queue<Vector3Int> generationQueue = new Queue<Vector3Int>();

    // Буфер с данными биомов на GPU
    private ComputeBuffer biomeDataBuffer;

    private void Start()
    {
        GenerateWorld();
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        SetupBiomeData();
    }

    private void Update()
    {
        if (generationQueue.Count > 0)
        {
            RequestChunkData(generationQueue.Dequeue());
        }
    }
    
    private void OnDestroy()
    {
        // ОБЯЗАТЕЛЬНО освобождаем память буфера при выходе
        if (biomeDataBuffer != null)
        {
            biomeDataBuffer.Release();
        }
    }

    private void SetupBiomeData()
    {
        if (biomeDefinitions == null || biomeDefinitions.Length == 0)
        {
            Debug.LogError("Массив Biome Definitions не назначен в инспекторе WorldManager!");
            return;
        }

        var biomeDataArray = new BiomeData[biomeDefinitions.Length];
        for (int i = 0; i < biomeDefinitions.Length; i++)
        {
            var heightModifier = biomeDefinitions[i].modifiers.OfType<SimpleHeightModifier>().FirstOrDefault();
            if (heightModifier != null)
            {
                biomeDataArray[i].heightOffset = heightModifier.heightOffset;
            }
            else
            {
                biomeDataArray[i].heightOffset = 0;
            }
        }
        
        biomeDataBuffer = new ComputeBuffer(biomeDataArray.Length, sizeof(float));
        biomeDataBuffer.SetData(biomeDataArray);
    }

    private void GenerateWorld()
    {
        Vector3Int playerChunkPos = Vector3Int.FloorToInt(player.position / chunkSize.x);

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int y = -1; y <= 0; y++)
            {
                for (int z = -viewDistance; z <= viewDistance; z++)
                {
                    Vector3Int chunkCoord = playerChunkPos + new Vector3Int(x, y, z);
                    if (!activeChunks.ContainsKey(chunkCoord))
                    {
                        generationQueue.Enqueue(chunkCoord);
                    }
                }
            }
        }
    }

    private void RequestChunkData(Vector3Int chunkCoord)
    {
        if (activeChunks.ContainsKey(chunkCoord) || biomeDataBuffer == null) return;

        int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
        ComputeBuffer densityBuffer = new ComputeBuffer(numVoxels, sizeof(float));
        int kernel = noiseGenerator.FindKernel("CSMain");
        
        noiseGenerator.SetBuffer(kernel, "densityBuffer", densityBuffer);
        noiseGenerator.SetBuffer(kernel, "_BiomeData", biomeDataBuffer);
        
        noiseGenerator.SetInts("chunkSize", chunkSize.x, chunkSize.y, chunkSize.z);
        noiseGenerator.SetInts("chunkPosition", chunkCoord.x, chunkCoord.y, chunkCoord.z);
        noiseGenerator.SetInt("numBiomes", biomeDefinitions.Length);
        
        // Передаем глобальные настройки
        noiseGenerator.SetFloat("_Amplitude", noiseAmplitude);
        noiseGenerator.SetFloat("_Frequency", noiseFrequency);
        noiseGenerator.SetFloat("_SurfaceLevel", surfaceLevel);
        noiseGenerator.SetFloat("_BiomeMapScale", biomeMapScale);
        
        // --- НОВЫЕ СТРОКИ ДЛЯ ПЕРЕДАЧИ ДАННЫХ ---
        noiseGenerator.SetFloat("_Lacunarity", lacunarity);
        noiseGenerator.SetFloat("_Persistence", persistence);
        // --- КОНЕЦ НОВЫХ СТРОК ---

        int dispatchSizeX = Mathf.CeilToInt((float)chunkSize.x / 8);
        int dispatchSizeY = Mathf.CeilToInt((float)chunkSize.y / 8);
        int dispatchSizeZ = Mathf.CeilToInt((float)chunkSize.z / 8);
        noiseGenerator.Dispatch(kernel, dispatchSizeX, dispatchSizeY, dispatchSizeZ);

        AsyncGPUReadback.Request(densityBuffer, request => OnChunkDataReceived(request, chunkCoord, densityBuffer));
    }

    private void OnChunkDataReceived(AsyncGPUReadbackRequest request, Vector3Int chunkCoord, ComputeBuffer buffer)
    {
        if (request.hasError || !this.enabled)
        {
            if (buffer != null) buffer.Release();
            return;
        }

        NativeArray<float> persistentDensities = new NativeArray<float>(request.GetData<float>(), Allocator.TempJob);

        NativeList<float3> vertices = new NativeList<float3>(Allocator.TempJob);
        NativeList<int> triangles = new NativeList<int>(Allocator.TempJob);

        MarchingCubesJob job = new MarchingCubesJob
        {
            densities = persistentDensities,
            chunkSize = new int3(chunkSize.x, chunkSize.y, chunkSize.z),
            isoLevel = this.isoLevel,
            vertices = vertices,
            triangles = triangles
        };

        JobHandle handle = job.Schedule();
        StartCoroutine(ProcessMeshData(handle, chunkCoord, persistentDensities, vertices, triangles));

        buffer.Release();
    }

    private IEnumerator ProcessMeshData(JobHandle jobHandle, Vector3Int chunkCoord, NativeArray<float> persistentDensities, NativeList<float3> vertices, NativeList<int> triangles)
    {
        yield return new WaitUntil(() => jobHandle.IsCompleted);

        jobHandle.Complete();

        if (vertices.Length > 3)
        {
            CreateChunkObject(chunkCoord, vertices.AsArray(), triangles.AsArray());
        }

        persistentDensities.Dispose();
        vertices.Dispose();
        triangles.Dispose();
    }

    private void CreateChunkObject(Vector3Int chunkCoord, NativeArray<float3> vertices, NativeArray<int> triangles)
    {
        Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };

        // Новый, более производительный способ копирования вершин
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles.ToArray(), 0);

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        Vector3 position = new Vector3(chunkCoord.x * chunkSize.x, chunkCoord.y * chunkSize.y, chunkCoord.z * chunkSize.z);

        GameObject chunkObject = Instantiate(chunkPrefab, position, Quaternion.identity, this.transform);
        chunkObject.name = $"Chunk {chunkCoord}";

        chunkObject.GetComponent<MeshFilter>().sharedMesh = mesh;
        chunkObject.GetComponent<MeshRenderer>().material = worldMaterial;

        // Добавляем в словарь только после полного создания
        if (!activeChunks.ContainsKey(chunkCoord))
        {
            activeChunks.Add(chunkCoord, chunkObject.GetComponent<Chunk>());
        }
    }
}