using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static MapGraph;

public static class MapGenerator
{

    public static void GenerateMap(MapGraph graph, HeightMapSettings heightMapSettings, int seed, int meshSize)
    {
        SetNodesToReplace(graph);
        SetLowNodesToWater(graph, 0.2f);
        SetEdgesToWater(graph);

        FillOcean(graph, meshSize);
        FindRivers(graph, 12f);
        CreateLakes(graph);
        SpreadHumidity(graph);
        SetHeat(graph, heightMapSettings.overallTemperature);
        SetPrecipitation(graph, heightMapSettings.precipitation);
        SetBioms(graph, seed);

        AverageCenterPoints(graph);

        FindCities(graph, heightMapSettings.cityDistrictCount, seed);
    }

    private static void SetEdgesToWater(MapGraph graph)
    {
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            if (node.IsEdge()) node.nodeType = MapGraph.MapNodeType.SaltWater;
        }
    }

    private static void AverageCenterPoints(MapGraph graph)
    {
        // foreach (var node in graph.points)
        // {
        //     Debug.Log(node);

        // }
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            node.centerPoint = new Vector3(node.centerPoint.x, node.GetCorners().Average(x => x.position.y), node.centerPoint.z);
            // Debug.Log(node);
            // node.GetEdges().ToList().ForEach(edge =>
            // {

            //     // if (Vector2.Distance(
            //     //         new Vector2(edge.next.destination.position.x, edge.next.destination.position.y),
            //     //         new Vector2(edge.destination.position.x, edge.destination.position.y)
            //     //     ) > 100)
            //     // {
            //     //     edge.previous.destination.position = new Vector3(edge.previous.destination.position.x, edge.previous.destination.position.y + 100, edge.previous.destination.position.z);
            //     //     Debug.Log(edge.previous.destination.position);
            //     //     Debug.Log(edge.destination.position);
            //     // };
            //     // vec.

            // });
        }
    }

    private static void SpreadHumidity(MapGraph graph)
    {
        // TODO: edit into external recursive function
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            if (
                node.nodeType == MapGraph.MapNodeType.FreshWater
            )
            {
                // node.nodeType = MapGraph.MapNodeType.Mountain;
                recursiveLakes(4, 1f, node);
            }

            if (
                node.nodeType == MapGraph.MapNodeType.SaltWater
            )
            {
                node.GetNeighborNodes()
                    .ForEach(childNode =>
                    {
                        if (childNode.nodeType != MapGraph.MapNodeType.SaltWater)
                        {
                            childNode.humidity += 0.25f;
                            childNode.extraRainfall += 1f;
                            childNode.GetNeighborNodes()
                                .ForEach(childNode =>
                                {
                                    if (childNode.nodeType != MapGraph.MapNodeType.SaltWater && childNode.extraRainfall == 0)
                                    {
                                        childNode.extraRainfall += 0.5f;
                                    }
                                });
                        }
                    });
            }
        }

        graph.edges.ForEach(edge =>
        {
            if (edge.water != 0)
            {
                // node.nodeType = MapGraph.MapNodeType.Mountain;
                // Rivers humidity spread
                edge.node.humidity += 0.5f;
                edge.node.GetNeighborNodes().ForEach(nodeValue =>
                {
                    if (nodeValue.humidity < 0.5)
                    {
                        nodeValue.humidity += 0.25f;
                    }
                });

                if (edge.opposite != null)
                {
                    edge.opposite.node.humidity += 0.5f;
                    edge.opposite.node.GetNeighborNodes().ForEach(nodeValue =>
                    {
                        if (nodeValue.humidity < 0.5)
                        {
                            nodeValue.humidity += 0.25f;
                        }
                    });

                }
            }
        });
    }

    private static void SetBioms(MapGraph graph, int seed)
    {
        UnityEngine.Random.InitState(seed);

        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            if (node.nodeType == MapGraph.MapNodeType.Replace)
            {
                // Debug.Log(node);
                if (node.heat > 22)
                {
                    if (node.precipitation < 80)
                    {
                        node.nodeType = MapGraph.MapNodeType.SubtropicalDesert;
                    }
                    else if (node.precipitation < 250)
                    {
                        // MapGraph.MapNodeType.SeasonalForest
                        int RandomValue = UnityEngine.Random.Range(0, 2);
                        if (RandomValue == 0)
                        {
                            node.nodeType = MapGraph.MapNodeType.Savanna;
                        }
                        else
                        {
                            node.nodeType = MapGraph.MapNodeType.SeasonalForest;
                        }
                    }
                    else
                    {
                        node.nodeType = MapGraph.MapNodeType.TropicalRainforest;
                    }
                    // node.biomeType
                }
                else if (node.heat > 18 && node.heat <= 22)
                {
                    if (node.precipitation < 60)
                    {
                        int RandomValue = UnityEngine.Random.Range(0, 2);
                        if (RandomValue == 0)
                        {
                            node.nodeType = MapGraph.MapNodeType.Grassland;
                        }
                        else
                        {
                            node.nodeType = MapGraph.MapNodeType.Desert;
                        }
                    }
                    else if (node.precipitation < 100)
                    {
                        node.nodeType = MapGraph.MapNodeType.Woodland;
                    }
                    else if (node.precipitation < 200)
                    {
                        node.nodeType = MapGraph.MapNodeType.SeasonalForest;
                    }
                    else
                    {
                        node.nodeType = MapGraph.MapNodeType.TemperateRainforest;
                    }
                }
                else if (node.heat <= 18)
                {
                    if (node.precipitation < 50)
                    {
                        node.nodeType = MapGraph.MapNodeType.Tundra;
                    }
                    else if (node.precipitation < 75)
                    {
                        node.nodeType = MapGraph.MapNodeType.Desert;
                    }
                    else if (node.precipitation < 90)
                    {
                        node.nodeType = MapGraph.MapNodeType.Woodland;
                    }
                    else if (node.precipitation < 120)
                    {
                        node.nodeType = MapGraph.MapNodeType.BorealForest;
                    }
                    else
                    {
                        node.nodeType = MapGraph.MapNodeType.BorealForest;
                    }
                }

                if (node.GetElevation() > 18)
                {
                    node.nodeType = MapGraph.MapNodeType.Ice;
                }
                else if (node.GetElevation() > 13)
                {
                    node.nodeType = MapGraph.MapNodeType.Mountain;
                }
                // MapGraph.MapBiomeType
                // node.heat
            }
        }


        // foreach (var node in graph.nodesByCenterPosition.Values)
        // {

        //     if (node.nodeType == MapGraph.MapNodeType.Error)
        //     {
        //         Debug.Log(node);
        //     }
        // }
    }

    private static void SetHeat(MapGraph graph, float overallTemperature)
    {
        var max = graph.points.Values.Max(point => point.position.z);
        var min = 0f;
        Debug.Log(max);
        // var maxHeight = graph.points.Values.Max(point => point.position.y);
        // Debug.Log(maxHeight);
        // var minHeight = graph.points.Values.Min(point => point.position.y);
        // Debug.Log(minHeight);
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            float accumulation = 1f / (float)Math.Pow(Math.Cosh(node.centerPoint.z / (max - min) - 1 / 2), 2);
            // - node.GetElevation() / 2 - decrease heat from height
            node.heat = overallTemperature * accumulation - node.GetElevation() / 2;
        }

        // var minTemp = graph.nodesByCenterPosition.Values.Min(point => point.heat);
        // var maxTemp = graph.nodesByCenterPosition.Values.Max(point => point.heat);
        // Debug.Log(minTemp);
        // Debug.Log(maxTemp);
    }

    private static void SetPrecipitation(MapGraph graph, float precipitation)
    {

        // Debug.Log(MapBiomeType.BiomeTable[0, 0]);
        var max = graph.points.Values.Max(point => point.position.y);
        var min = 0f;
        // var maxHeight = graph.points.Values.Max(point => point.position.y);
        // Debug.Log(maxHeight);
        // var minHeight = graph.points.Values.Min(point => point.position.y);
        // Debug.Log(minHeight);
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            // Debug.Log(node);
            float accumulation = (float)Math.Pow(Math.Cosh(node.GetElevation() / (max - min) * 2 - 1), 2);
            // - node.GetElevation() / 2 - decrease heat from height
            node.precipitation = precipitation * (accumulation + node.humidity + node.extraRainfall);
        }

        // var minTemp = graph.nodesByCenterPosition.Values.Min(point => point.precipitation);
        // var maxTemp = graph.nodesByCenterPosition.Values.Max(point => point.precipitation);
        // Debug.Log(minTemp);
        // Debug.Log(maxTemp);
    }

    private static void recursiveLakes(int k, float initialHumidity, MapNode node)
    {
        node.humidity = initialHumidity;

        if (k == 0) return;

        node.GetNeighborNodes().ForEach(childNode =>
        {
            if (childNode.humidity <= initialHumidity / 2)
            {
                recursiveLakes(k - 1, initialHumidity / 2, childNode);
            }
        });
    }

    private static void AddMountains(MapGraph graph)
    {
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            if (node.GetElevation() > 15f || node.GetHeightDifference() > 7f)
            {
                node.nodeType = MapGraph.MapNodeType.Mountain;
            }
            if (node.GetElevation() > 17f)
            {
                node.nodeType = MapGraph.MapNodeType.Snow;
            }
        }
    }

    private static void CreateLakes(MapGraph graph)
    {
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            var edges = node.GetEdges();
            if (!edges.Any(x => x.water == 0))
            {
                CreateLake(node);
            }
        }
    }

    private static void FindRivers(MapGraph graph, float minElevation)
    {
        var riverCount = 0;
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            var elevation = node.GetElevation();
            if (elevation > minElevation)
            {
                var waterSource = node.GetLowestCorner();
                var lowestEdge = waterSource.GetDownSlopeEdge();
                if (lowestEdge == null) continue;
                CreateRiver(graph, lowestEdge);

                riverCount++;
            }
        }
        //Debug.Log(string.Format("{0} rivers drawn", riverCount));
    }

    private static void CreateRiver(MapGraph graph, MapGraph.MapNodeHalfEdge startEdge)
    {
        bool heightUpdated = false;

        // Once a river has been generated, it tries again to see if a quicker route has been created.
        // This sets how many times we should go over the same river.
        var maxIterations = 1;
        var iterationCount = 0;

        // Make sure that the river generation code doesn't get stuck in a loop.
        var maxChecks = 100;
        var checkCount = 0;

        var previousRiverEdges = new List<MapGraph.MapNodeHalfEdge>();
        do
        {
            heightUpdated = false;

            var riverEdges = new List<MapGraph.MapNodeHalfEdge>();
            var previousEdge = startEdge;
            var nextEdge = startEdge;

            while (nextEdge != null)
            {
                if (checkCount >= maxChecks)
                {
                    Debug.LogWarning("Unable to find route for river. Maximum number of checks reached");
                    return;
                }
                checkCount++;

                var currentEdge = nextEdge;

                // We've already seen this edge and it's flowing back up itself.
                if (riverEdges.Contains(currentEdge) || riverEdges.Contains(currentEdge.opposite)) break;
                riverEdges.Add(currentEdge);
                currentEdge.AddWater();

                // Check that we haven't reached the sea
                if (currentEdge.destination.GetNodes().Any(x => x.nodeType == MapGraph.MapNodeType.SaltWater)) break;

                nextEdge = GetDownSlopeEdge(currentEdge, riverEdges);

                if (nextEdge == null && previousEdge != null)
                {
                    // We need to start carving a path for the river.
                    nextEdge = GetNewCandidateEdge(graph.GetCenter(), currentEdge, riverEdges, previousRiverEdges);

                    // If we can't get a candidate edge, then backtrack and try again
                    var previousEdgeIndex = riverEdges.Count - 1;
                    while (nextEdge == null || previousEdgeIndex == 0)
                    {
                        previousEdge = riverEdges[previousEdgeIndex];
                        previousEdge.water--;
                        nextEdge = GetNewCandidateEdge(graph.GetCenter(), previousEdge, riverEdges, previousRiverEdges);
                        riverEdges.Remove(previousEdge);
                        previousEdgeIndex--;
                    }
                    if (nextEdge != null)
                    {
                        if (nextEdge.previous.destination.position.y != nextEdge.destination.position.y)
                        {
                            LevelEdge(nextEdge);
                            heightUpdated = true;
                        }
                    }
                    else
                    {
                        // We've tried tunneling, backtracking, and we're still lost.
                        Debug.LogError("Unable to find route for river");
                    }
                }
                previousEdge = currentEdge;
            }
            if (maxIterations <= iterationCount) break;
            iterationCount++;

            // If the height was updated, we need to recheck the river again.
            if (heightUpdated)
            {
                foreach (var edge in riverEdges)
                {
                    if (edge.water > 0) edge.water--;
                }
                previousRiverEdges = riverEdges;
            }
        } while (heightUpdated);
    }

    private static void CreateLake(MapGraph.MapNode node)
    {
        var lowestCorner = node.GetLowestCorner();
        node.nodeType = MapGraph.MapNodeType.FreshWater;

        // Set all of the heights equal to where the water came in.
        SetNodeHeightToCornerHeight(node, lowestCorner);
    }

    private static void LevelEdge(MapGraph.MapNodeHalfEdge currentEdge)
    {
        currentEdge.destination.position = new Vector3(currentEdge.destination.position.x, currentEdge.previous.destination.position.y, currentEdge.destination.position.z);
    }

    private static MapGraph.MapNodeHalfEdge GetDownSlopeEdge(MapGraph.MapNodeHalfEdge source, List<MapGraph.MapNodeHalfEdge> seenEdges)
    {
        var corner = source.destination;

        var candidates = corner.GetEdges().Where(x =>
            x.destination.position.y < corner.position.y &&
            !seenEdges.Contains(x) &&
            x.opposite != null && !seenEdges.Contains(x.opposite) &&
            x.node.nodeType != MapGraph.MapNodeType.FreshWater &&
            x.opposite.node.nodeType != MapGraph.MapNodeType.FreshWater);

        // Make sure the river prefers to follow existing rivers
        var existingRiverEdge = candidates.FirstOrDefault(x => x.water > 0);
        if (existingRiverEdge != null) return existingRiverEdge;

        return candidates.OrderByDescending(x => x.GetSlopeAngle()).FirstOrDefault();
    }

    private static MapGraph.MapNodeHalfEdge GetNewCandidateEdge(Vector3 center, MapGraph.MapNodeHalfEdge source, List<MapGraph.MapNodeHalfEdge> seenEdges, List<MapGraph.MapNodeHalfEdge> previousEdges)
    {
        var corner = source.destination;

        var edges = corner.GetEdges().Where(x =>
            !seenEdges.Contains(x) &&
            x.opposite != null &&
            !seenEdges.Contains(x.opposite)).ToList();

        // Make sure the river prefers to follow existing rivers
        var existingRiverEdge = edges.FirstOrDefault(x => x.water > 0);
        if (existingRiverEdge != null) return existingRiverEdge;

        // Make the river prefer to follow previous iterations
        existingRiverEdge = edges.FirstOrDefault(x => previousEdges.Contains(x));
        if (existingRiverEdge != null) return existingRiverEdge;

        var awayFromCenterEdges = edges.Where(x => Vector3.Dot(x.destination.position - x.previous.destination.position, x.destination.position - center) >= 0);
        if (awayFromCenterEdges.Any()) edges = awayFromCenterEdges.ToList();
        return edges.OrderBy(x => x.destination.position.y).FirstOrDefault();
    }


    private static void SetNodeHeightToCornerHeight(MapGraph.MapNode node, MapGraph.MapPoint targetCorner)
    {
        foreach (var corner in node.GetCorners())
        {
            corner.position = new Vector3(corner.position.x, targetCorner.position.y, corner.position.z);
        }
        node.centerPoint = new Vector3(node.centerPoint.x, targetCorner.position.y, node.centerPoint.z);
    }

    private static void FillOcean(MapGraph graph, int meshSize)
    {

        var startNodes = graph.nodesByCenterPosition
                        .Where(mapNode => mapNode.Value.oceanCell != true && mapNode.Value.nodeType == MapGraph.MapNodeType.SaltWater)
                        .ToList();

        while (startNodes.Count() > 0)
        {
            startNodes = graph.nodesByCenterPosition
                        .Where(mapNode => mapNode.Value.oceanCell != true && mapNode.Value.nodeType == MapGraph.MapNodeType.SaltWater)
                        .ToList();

            startNodes.ForEach(nodeElem =>
            {
                nodeElem.Value.oceanCell = true;
                foreach (var neighbor in nodeElem.Value.GetNeighborNodes())
                {
                    FloodFill(neighbor, MapGraph.MapNodeType.FreshWater, MapGraph.MapNodeType.SaltWater);
                }
            });
        }
    }

    private static void FloodFill(MapGraph.MapNode node, MapGraph.MapNodeType targetType, MapGraph.MapNodeType replacementType)
    {
        if (targetType == replacementType) return;
        if (node.nodeType != targetType) return;
        node.nodeType = replacementType;
    }

    private static void SetBeaches(MapGraph graph)
    {
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            if (node.nodeType == MapGraph.MapNodeType.Replace)
            {
                foreach (var neighbor in node.GetNeighborNodes())
                {
                    if (neighbor.nodeType == MapGraph.MapNodeType.SaltWater)
                    {
                        if (node.GetHeightDifference() < 0.8f)
                        {
                            node.nodeType = MapGraph.MapNodeType.Beach;
                        }
                        break;
                    }
                }
            }
        }
    }

    private static void FindCities(MapGraph graph, int maxCities, int seed)
    {
        var candidates = GetCityCandidate(graph);
        int cityCount = 0;
        UnityEngine.Random.InitState(seed);

        Debug.Log(candidates.Count());
        while (cityCount < maxCities)
        {
            var candidate = candidates.FirstOrDefault();
            if (candidate == null) break;
            // Debug.Log(candidate.humidity);

            candidate.isCity = true;
            candidate.population = UnityEngine.Random.Range(0, 10000);
            cityCount++;
        }
    }

    private static IOrderedEnumerable<MapGraph.MapNode> GetCityCandidate(MapGraph graph)
    {
        var candidates = graph.nodesByCenterPosition.Values
        .Where((node) =>
        {
            return node.humidity > 0 &&
                node.isCity == false &&
                node.nodeType != MapGraph.MapNodeType.FreshWater &&
                node.nodeType != MapGraph.MapNodeType.SaltWater &&
                node.GetHeightDifference() < 2f;
        })
        .OrderByDescending((node) =>
        {
            return node.humidity;
        });
        return candidates;
    }

    private static void SetNodesToReplace(MapGraph graph)
    {
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            if (node.nodeType != MapGraph.MapNodeType.Error) node.nodeType = MapGraph.MapNodeType.Replace;
        }
    }

    private static void SetLowNodesToWater(MapGraph graph, float cutoff)
    {
        foreach (var node in graph.nodesByCenterPosition.Values)
        {
            if (node.centerPoint.y <= cutoff)
            {
                var allZero = true;
                foreach (var edge in node.GetEdges())
                {
                    if (edge.destination.position.y > cutoff)
                    {
                        allZero = false;
                        break;
                    }
                }
                if (allZero && node.nodeType != MapGraph.MapNodeType.Error) node.nodeType = MapGraph.MapNodeType.FreshWater;
            }
        }
    }
}
