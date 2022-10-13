﻿using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System;
using static MapGraph;

/// <summary>
/// Generates a texture by writing to a render texture using GL
/// 
/// References:
/// https://docs.unity3d.com/ScriptReference/GL.html
/// https://forum.unity.com/threads/rendering-gl-to-a-texture2d-immediately-in-unity4.158918/
/// </summary>
public static class MapTextureGenerator
{
    private static Material drawingMaterial;

    private static void CreateDrawingMaterial()
    {
        if (!drawingMaterial)
        {
            // Unity has a built-in shader that is useful for drawing
            // simple colored things.
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            drawingMaterial = new Material(shader);
            drawingMaterial.hideFlags = HideFlags.HideAndDontSave;
            // Turn on alpha blending
            drawingMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            drawingMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // Turn backface culling off
            drawingMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            // Turn off depth writes
            drawingMaterial.SetInt("_ZWrite", 0);
        }
    }

    public static Texture2D GenerateTexture(MapGraph map, int seed, int meshSize, int textureSize, List<MapNodeTypeEntity> mapNodeTypeEntity, bool drawBoundries, bool drawTriangles, bool drawCenters, bool drawPrefabs)
    {
        CreateDrawingMaterial();
        var texture = RenderGLToTexture(map, seed, textureSize, meshSize, drawingMaterial, mapNodeTypeEntity, drawBoundries, drawTriangles, drawCenters, drawPrefabs);

        return texture;
    }

    private static Texture2D RenderGLToTexture(MapGraph map, int seed, int textureSize, int meshSize, Material material, List<MapNodeTypeEntity> mapNodeTypeEntity, bool drawBoundries, bool drawTriangles, bool drawCenters, bool drawPrefabs)
    {
        var renderTexture = CreateRenderTexture(textureSize, Color.white);

        // render GL immediately to the active render texture //
        DrawToRenderTexture(map, seed, material, textureSize, meshSize, mapNodeTypeEntity, drawBoundries, drawTriangles, drawCenters, drawPrefabs);

        return CreateTextureFromRenderTexture(textureSize, renderTexture);
    }

    private static Texture2D CreateTextureFromRenderTexture(int textureSize, RenderTexture renderTexture)
    {
        // read the active RenderTexture into a new Texture2D //
        Texture2D newTexture = new Texture2D(textureSize, textureSize);
        newTexture.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0);

        // apply pixels and compress //
        bool applyMipsmaps = false;
        newTexture.Apply(applyMipsmaps);
        bool highQuality = true;
        newTexture.Compress(highQuality);

        // clean up after the party //
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(renderTexture);

        // return the goods //
        return newTexture;
    }

    private static RenderTexture CreateRenderTexture(int textureSize, Color color)
    {
        // get a temporary RenderTexture //
        RenderTexture renderTexture = RenderTexture.GetTemporary(textureSize, textureSize);

        // set the RenderTexture as global target (that means GL too)
        RenderTexture.active = renderTexture;

        // clear GL //
        GL.Clear(false, true, color);
        GL.sRGBWrite = false;

        return renderTexture;
    }

    private static void DrawToRenderTexture(MapGraph map, int seed, Material material, int textureSize, int meshSize, List<MapNodeTypeEntity> colours, bool drawBoundries, bool drawTriangles, bool drawCenters, bool drawPrefabs)
    {
        material.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, meshSize, 0, meshSize);
        GL.Viewport(new Rect(0, 0, textureSize, textureSize));

        var coloursDictionary = new Dictionary<MapGraph.MapNodeType, Color>();
        foreach (var colour in colours)
        {
            if (!coloursDictionary.ContainsKey(colour.type)) coloursDictionary.Add(colour.type, colour.color);
        }

        DrawNodeTypes(map, coloursDictionary);


        if (drawCenters) DrawCenterPoints(map, Color.red);
        if (drawBoundries) DrawEdges(map, Color.black);
        DrawRivers(map, 2, coloursDictionary[MapGraph.MapNodeType.FreshWater]);
        if (drawTriangles) DrawDelauneyEdges(map, Color.red);

        GL.PopMatrix();
    }

    private static void DrawEdges(MapGraph map, Color color)
    {
        GL.Begin(GL.LINES);
        GL.Color(color);

        foreach (var edge in map.edges)
        {
            var start = edge.GetStartPosition();
            var end = edge.GetEndPosition();

            GL.Vertex3(start.x, start.z, 0);
            GL.Vertex3(end.x, end.z, 0);
        }

        GL.End();
    }

    private static void DrawDelauneyEdges(MapGraph map, Color color)
    {
        GL.Begin(GL.LINES);
        GL.Color(color);

        foreach (var edge in map.edges)
        {
            if (edge.opposite != null)
            {
                var start = edge.node.centerPoint;
                var end = edge.opposite.node.centerPoint;

                GL.Vertex3(start.x, start.z, 0);
                GL.Vertex3(end.x, end.z, 0);
            }
        }

        GL.End();
    }

    private static void DrawRivers(MapGraph map, int minRiverSize, Color color)
    {
        GL.Begin(GL.LINES);
        GL.Color(color);

        foreach (var edge in map.edges)
        {
            if (edge.water < minRiverSize) continue;

            var start = edge.GetStartPosition();
            var end = edge.GetEndPosition();

            GL.Vertex3(start.x, start.z, 0);
            GL.Vertex3(end.x, end.z, 0);
        }

        GL.End();
    }

    private static void DrawCenterPoints(MapGraph map, Color color)
    {
        GL.Begin(GL.QUADS);
        GL.Color(color);

        foreach (var point in map.nodesByCenterPosition.Values)
        {
            var x = point.centerPoint.x;
            var y = point.centerPoint.z;
            GL.Vertex3(x - .25f, y - .25f, 0);
            GL.Vertex3(x - .25f, y + .25f, 0);
            GL.Vertex3(x + .25f, y + .25f, 0);
            GL.Vertex3(x + .25f, y - .25f, 0);
        }

        GL.End();
    }
    private static void DrawNodeTypes(MapGraph map, Dictionary<MapGraph.MapNodeType, Color> colours)
    {
        GL.Begin(GL.TRIANGLES);
        foreach (var node in map.nodesByCenterPosition.Values)
        {
            var color = colours.ContainsKey(node.nodeType) ? colours[node.nodeType] : Color.red;
            Debug.Log($"{color} {node.nodeType}");
            GL.Color(color);

            foreach (var edge in node.GetEdges())
            {
                var start = edge.previous.destination.position;
                var end = edge.destination.position;
                GL.Vertex3(node.centerPoint.x, node.centerPoint.z, 0);
                GL.Vertex3(start.x, start.z, 0);
                GL.Vertex3(end.x, end.z, 0);
            }
        }
        GL.End();
    }
}
