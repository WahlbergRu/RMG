using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Rendering;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiGeometryBuildSystem))]
    public partial class MapRenderingSystem : SystemBase
    {
        private Material _cellMaterial;
        private Material _roadMaterial;
        private Material _borderMaterial;

        private bool _cellsSpawned;
        private bool _roadsSpawned;

        protected override void OnCreate()
        {
            _cellMaterial   = MaterialFactory.Create("Universal Render Pipeline/Lit",   "CellMaterial",   true);
            _roadMaterial   = MaterialFactory.Create("Universal Render Pipeline/Unlit", "RoadMaterial",   true, Color.yellow);
            _borderMaterial = MaterialFactory.Create("Universal Render Pipeline/Unlit", "BorderMaterial", true, Color.blue);

            if (_roadMaterial   != null) _roadMaterial.SetInt("_Cull",   (int)UnityEngine.Rendering.CullMode.Off);
            if (_borderMaterial != null) _borderMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

            RequireForUpdate<MapRenderingSettings>();
            RequireForUpdate<VoronoiCell>();
            RequireForUpdate<VoronoiEdge>();
            RequireForUpdate<CellPolygonVertex>();
            RequireForUpdate<CellTriIndex>();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.HasSingleton<MapGeneratedTag>()) return;

            var settings = SystemAPI.GetSingleton<MapRenderingSettings>();

            if (!_cellsSpawned)
            {
                CellMeshBuilder.Build(EntityManager, _cellMaterial);
                _cellsSpawned = true;
            }

            if (!_roadsSpawned)
            {
                RoadMeshBuilder.Build(EntityManager, _roadMaterial, settings);
                BorderMeshBuilder.Build(EntityManager, _borderMaterial, settings);
                _roadsSpawned = true;
            }
        }
    }
}
