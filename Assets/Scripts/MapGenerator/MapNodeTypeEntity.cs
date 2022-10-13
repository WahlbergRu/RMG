using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MapNodeTypeEntity
{
    public MapGraph.MapNodeType type;
    public Color color;
    public List<GameObject> modelList;

    [Header("optional, all value will be fill if once filled for check in")]
    [Header("Exclusive value")]
    public float precipitationMin;
    [Header("Inclusive value")]
    public float precipitationMax;
    [Header("Exclusive value")]
    public float heatMin;
    [Header("Inclusive value")]
    public float heatMax;
}
