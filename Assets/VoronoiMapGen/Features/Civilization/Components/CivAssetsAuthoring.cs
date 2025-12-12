using Unity.Entities;
using UnityEngine;

namespace VoronoiMapGen.Features.Civilization.Components
{
    // Компонент-хранилище ссылок на Entity-префабы
    public struct CivPrefabs : IComponentData
    {
        public Entity MetropolisPrefab;
        public Entity TownPrefab;
        public Entity OutpostPrefab;
    }

    public class CivAssetsAuthoring : MonoBehaviour
    {
        [Header("Models")]
        public GameObject MetropolisModel;
        public GameObject TownModel;
        public GameObject OutpostModel;

        public class Baker : Baker<CivAssetsAuthoring>
        {
            public override void Bake(CivAssetsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new CivPrefabs
                {
                    MetropolisPrefab = GetEntity(authoring.MetropolisModel, TransformUsageFlags.Dynamic),
                    TownPrefab = GetEntity(authoring.TownModel, TransformUsageFlags.Dynamic),
                    OutpostPrefab = GetEntity(authoring.OutpostModel, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}