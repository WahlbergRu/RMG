using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using Random = Unity.Mathematics.Random;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct MultiLevelSiteGenerationJob : IJob
    {
        [ReadOnly] public NativeArray<LevelSettings> LevelSettings;
        [ReadOnly] public float2 MapSize;
        [ReadOnly] public int BaseSeed;
        [ReadOnly] public int ParentLevel;
        [ReadOnly] public NativeArray<float2> ParentSites;
        [ReadOnly] public NativeArray<VoronoiCell> ParentCells;
        [ReadOnly] public NativeArray<VoronoiSite> ParentSiteMetadata;

        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> SiteMetadata;

        public void Execute()
        {
            int lvl = ParentLevel + 1;
            if (ParentLevel == -1) GenerateGlobal(lvl, LevelSettings[lvl]);
            else GenerateChildren(lvl, LevelSettings[lvl]);
        }

        private void GenerateGlobal(int lvl, LevelSettings s)
        {
            int idx = 0;
            // Рамка (Grid)
            float step = 80f, margin = 200f;
            for (float x = -margin; x <= MapSize.x + margin; x += step) {
                for (float y = -margin; y <= MapSize.y + margin; y += step) {
                    if (x > 10 && x < MapSize.x - 10 && y > 10 && y < MapSize.y - 10) continue;
                    if (idx < Sites.Length) AddSite(idx++, new float2(x, y), lvl, -1, -2.0f);
                }
            }
            // Сайты с запасом
            int target = s.GlobalSiteCount;
            var rng = new Random((uint)BaseSeed);
            for (int i = 0; i < target; i++) {
                if (idx >= Sites.Length) break;
                float2 pos = rng.NextFloat2(new float2(-100), MapSize + new float2(100)); // Over-provision
                AddSite(idx++, pos, lvl, -1, math.saturate(rng.NextFloat()));
            }
        }

        private void GenerateChildren(int lvl, LevelSettings s)
        {
            int idx = 0;
            // Копия рамки
            for (int i = 0; i < ParentSiteMetadata.Length; i++) {
                if (ParentSiteMetadata[i].Value < -0.5f && idx < Sites.Length) {
                    Sites[idx] = ParentSites[i];
                    SiteMetadata[idx] = new VoronoiSite { Position = ParentSites[i], Index = idx, Level = lvl, Value = -2.0f };
                    idx++;
                }
            }
            // Дети
            var rng = new Random((uint)BaseSeed + (uint)lvl * 77);
            float rBase = 50f * s.ScaleFactor; // Примерный радиус

            for (int p = 0; p < ParentCells.Length; p++) {
                if (ParentSiteMetadata[p].Value < -0.5f || ParentCells[p].Level != ParentLevel) continue;
                if (idx >= Sites.Length) break;

                int count = (int)math.lerp(s.MinSiteCount, s.MaxSiteCount, ParentSiteMetadata[p].Value);
                float2 center = ParentCells[p].Centroid;
                
                for (int c = 0; c < count; c++) {
                    if (idx >= Sites.Length) break;
                    float2 pos = center + rng.NextFloat2Direction() * rng.NextFloat(0, rBase);
                    AddSite(idx++, pos, lvl, p, 0.5f);
                }
            }
        }

        private void AddSite(int i, float2 p, int l, int parent, float v) {
            Sites[i] = p;
            SiteMetadata[i] = new VoronoiSite { Position = p, Index = i, Level = l, ParentIndex = parent, Value = v };
        }
    }
}