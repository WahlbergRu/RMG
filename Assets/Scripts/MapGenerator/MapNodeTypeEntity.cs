using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MapNodeTypeEntity
{
    public MapGraph.MapNodeType type;
    public Color color;
    public List<GameObject> modelList;

    [Header("First biome step")]
    [Header("optional, all value will be fill if once filled for check in")]
    [Header("Exclusive value")]
    public float precipitationMin;
    [Header("Inclusive value")]
    public float precipitationMax;
    [Header("Exclusive value")]
    public float heatMin;
    [Header("Inclusive value")]
    public float heatMax;

    [Header("Second biome step")]
    [Header("Exclusive value")]
    [Header("optional, all value will be fill if once filled for check in")]
    [Header("Exclusive value")]
    public float elevationMin;
    [Header("Inclusive value")]
    public float elevationMax;
}
