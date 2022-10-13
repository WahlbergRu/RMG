using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

[Serializable]
public class MapNodeTown
{
    public int minPopulation;
    public int maxPopulation;
    public List<GameObject> modelList;
}