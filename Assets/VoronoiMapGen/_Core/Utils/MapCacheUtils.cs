using System;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Utils
{
    public static class MapCacheUtils
    {
        // Увеличили версию, чтобы старый кэш сбросился
        private const int CurrentVersion = 5;

        private static string GetPath(int seed, int level)
        {
            return Path.Combine(Application.persistentDataPath, $"map_cache_v{CurrentVersion}_{seed}_{level}.bin");
        }

        public static bool LoadLevel(int seed, int level,
            out NativeArray<float2> sites,
            out NativeArray<VoronoiSite> meta,
            out NativeArray<TectonicPlateData> tectonic,
            out NativeArray<ClimateData> climate,
            out NativeArray<HydrologyData> hydro,
            out NativeArray<BiomeData> biomes,
            // --- НОВЫЕ ПАРАМЕТРЫ ГЕОМЕТРИИ ---
            out NativeArray<float2> geomVerts, // Все вершины полигонов подряд
            out NativeArray<int> geomCounts, // Сколько вершин у каждой ячейки
            out NativeArray<VoronoiEdge> geomEdges // Ребра
        )
        {
            sites = default;
            meta = default;
            tectonic = default;
            climate = default;
            hydro = default;
            biomes = default;
            geomVerts = default;
            geomCounts = default;
            geomEdges = default;

            string path = GetPath(seed, level);
            if (!File.Exists(path)) return false;

            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    int version = reader.ReadInt32();
                    if (version != CurrentVersion) return false;

                    int count = reader.ReadInt32();

                    // 1. Основные данные
                    sites = new NativeArray<float2>(count, Allocator.Persistent);
                    meta = new NativeArray<VoronoiSite>(count, Allocator.Persistent);
                    tectonic = new NativeArray<TectonicPlateData>(count, Allocator.Persistent);
                    climate = new NativeArray<ClimateData>(count, Allocator.Persistent);
                    hydro = new NativeArray<HydrologyData>(count, Allocator.Persistent);
                    biomes = new NativeArray<BiomeData>(count, Allocator.Persistent);

                    for (int i = 0; i < count; i++) sites[i] = new float2(reader.ReadSingle(), reader.ReadSingle());

                    for (int i = 0; i < count; i++)
                        meta[i] = new VoronoiSite
                        {
                            Index = reader.ReadInt32(),
                            Level = level,
                            ParentIndex = reader.ReadInt32(),
                            Value = reader.ReadSingle(),
                            Position = sites[i]
                        };

                    for (int i = 0; i < count; i++)
                        tectonic[i] = new TectonicPlateData
                            { IsOcean = reader.ReadBoolean(), BaseHeight = reader.ReadSingle() };
                    for (int i = 0; i < count; i++)
                        climate[i] = new ClimateData
                            { Temperature = reader.ReadSingle(), Moisture = reader.ReadSingle() };
                    for (int i = 0; i < count; i++)
                        hydro[i] = new HydrologyData
                        {
                            FlowTargetIndex = reader.ReadInt32(),
                            Flux = reader.ReadSingle(),
                            IsRiver = reader.ReadBoolean(),
                            IsLake = reader.ReadBoolean(),
                            IsOcean = reader.ReadBoolean()
                        };
                    for (int i = 0; i < count; i++) biomes[i] = new BiomeData { Type = (BiomeType)reader.ReadInt32() };

                    // 2. ГЕОМЕТРИЯ (НОВОЕ)
                    int vertCount = reader.ReadInt32();
                    geomVerts = new NativeArray<float2>(vertCount, Allocator.Persistent);
                    for (int i = 0; i < vertCount; i++)
                        geomVerts[i] = new float2(reader.ReadSingle(), reader.ReadSingle());

                    int countsCount = reader.ReadInt32();
                    geomCounts = new NativeArray<int>(countsCount, Allocator.Persistent);
                    for (int i = 0; i < countsCount; i++) geomCounts[i] = reader.ReadInt32();

                    int edgesCount = reader.ReadInt32();
                    geomEdges = new NativeArray<VoronoiEdge>(edgesCount, Allocator.Persistent);
                    for (int i = 0; i < edgesCount; i++)
                        // Сохраняем только данные (без Entity ссылок, они не валидны при перезагрузке)
                        geomEdges[i] = new VoronoiEdge
                        {
                            SiteA = reader.ReadInt32(),
                            SiteB = reader.ReadInt32(),
                            VertexA = new float2(reader.ReadSingle(), reader.ReadSingle()),
                            VertexB = new float2(reader.ReadSingle(), reader.ReadSingle()),
                            Level = level,
                            CellA = Entity.Null,
                            CellB = Entity.Null
                        };
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cache] Error loading Level {level}: {e.Message}");
                if (sites.IsCreated) sites.Dispose();
                if (meta.IsCreated) meta.Dispose();
                if (tectonic.IsCreated) tectonic.Dispose();
                if (climate.IsCreated) climate.Dispose();
                if (hydro.IsCreated) hydro.Dispose();
                if (biomes.IsCreated) biomes.Dispose();
                // Чистим новые массивы
                if (geomVerts.IsCreated) geomVerts.Dispose();
                if (geomCounts.IsCreated) geomCounts.Dispose();
                if (geomEdges.IsCreated) geomEdges.Dispose();

                return false;
            }
        }

        public static void SaveLevel(int seed, int level,
            NativeArray<float2> sites,
            NativeArray<VoronoiSite> meta,
            NativeArray<TectonicPlateData> tectonic,
            NativeArray<ClimateData> climate,
            NativeArray<HydrologyData> hydro,
            NativeArray<BiomeData> biomes,
            // --- НОВЫЕ ПАРАМЕТРЫ ---
            NativeList<float2> geomVerts,
            NativeList<int> geomCounts,
            NativeList<VoronoiEdge> geomEdges)
        {
            string path = GetPath(seed, level);
            using (FileStream stream = new FileStream(path, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(CurrentVersion);
                writer.Write(sites.Length);

                for (int i = 0; i < sites.Length; i++)
                {
                    writer.Write(sites[i].x);
                    writer.Write(sites[i].y);
                }

                for (int i = 0; i < meta.Length; i++)
                {
                    writer.Write(meta[i].Index);
                    writer.Write(meta[i].ParentIndex);
                    writer.Write(meta[i].Value);
                }

                for (int i = 0; i < tectonic.Length; i++)
                {
                    writer.Write(tectonic[i].IsOcean);
                    writer.Write(tectonic[i].BaseHeight);
                }

                for (int i = 0; i < climate.Length; i++)
                {
                    writer.Write(climate[i].Temperature);
                    writer.Write(climate[i].Moisture);
                }

                for (int i = 0; i < hydro.Length; i++)
                {
                    writer.Write(hydro[i].FlowTargetIndex);
                    writer.Write(hydro[i].Flux);
                    writer.Write(hydro[i].IsRiver);
                    writer.Write(hydro[i].IsLake);
                    writer.Write(hydro[i].IsOcean);
                }

                for (int i = 0; i < biomes.Length; i++) writer.Write((int)biomes[i].Type);

                // 2. ГЕОМЕТРИЯ
                writer.Write(geomVerts.Length);
                for (int i = 0; i < geomVerts.Length; i++)
                {
                    writer.Write(geomVerts[i].x);
                    writer.Write(geomVerts[i].y);
                }

                writer.Write(geomCounts.Length);
                for (int i = 0; i < geomCounts.Length; i++) writer.Write(geomCounts[i]);

                writer.Write(geomEdges.Length);
                for (int i = 0; i < geomEdges.Length; i++)
                {
                    VoronoiEdge e = geomEdges[i];
                    writer.Write(e.SiteA);
                    writer.Write(e.SiteB);
                    writer.Write(e.VertexA.x);
                    writer.Write(e.VertexA.y);
                    writer.Write(e.VertexB.x);
                    writer.Write(e.VertexB.y);
                }
            }
        }
    }
}