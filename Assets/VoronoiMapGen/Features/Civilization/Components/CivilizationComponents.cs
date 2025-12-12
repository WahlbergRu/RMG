using System;
using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Features.Civilization.Components
{
    // НОВАЯ ВЕРСИЯ ENUM
    public enum SettlementType : byte
    {
        Wilderness = 0, // Дикая местность
        Outpost = 1,    // Деревня/Аванпост
        Town = 2,       // Город
        Metropolis = 3  // Мегаполис
    }
    
    // --- BASIC DEMOGRAPHICS ---
    public struct DemographicsData : IComponentData
    {
        public float FoodYield;       
        public float WaterScore;      
        public float HazardRating;    
        public float HousingCapacity; 
        
        public int EstimatedPopulation; 
    }

    [Serializable]
    public struct SettlementData : IComponentData
    {
        public SettlementType Type;
        
        // НОВЫЕ ПОЛЯ (Компилятор ругался на их отсутствие)
        public float SuitabilityScore; 
        public int MetropolisIndex;    

        // Доп. поля
        public int Tier;            
        public float TradePower;    
        public bool IsRoadNode;     
        
        public bool IsUrban => Type != SettlementType.Wilderness;
    }

    public struct CalcDemographicsTag : IComponentData, IEnableableComponent { }
}