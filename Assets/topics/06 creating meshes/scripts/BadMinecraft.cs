using UnityEngine;
using System.Collections.Generic;
using System;

[RequireComponent(typeof (MeshFilter))]
[RequireComponent(typeof (MeshRenderer))]
public class BadMinecraft : MonoBehaviour {


    Mesh mesh;
    List<Vector3> vertices;
    List<Vector3> normals;
    List<Vector2> uvs;
    List<int> triangles;

    void Start() {
       
        MeshFilter meshFilter = GetComponent<MeshFilter>();

        vertices= new List<Vector3>();
        normals= new List<Vector3>();
        triangles = new List<int>();
        uvs = new List<Vector2>();

        mesh = CreateCube();
        meshFilter.mesh = mesh;
    }

    private Mesh CreateCube()
    {
        mesh = new Mesh();
        float hs = 0.5f;
        return mesh;
    }

    void CreateQuad(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector3 normal, Vector2Int uvTile) 
    {
        int startIndex = vertices.Count;
        vertices.Add(bl);
        vertices.Add(tl);
        vertices.Add(tr);
        vertices.Add(br);

        triangles.Add(startIndex+0);
        triangles.Add(startIndex+1);
        triangles.Add(startIndex+2);
        triangles.Add(startIndex+0);
        triangles.Add(startIndex+2);
        triangles.Add(startIndex+3);

        Vector3[] _normals = { normal, normal, normal, normal };
        normals.AddRange(_normals);

        Vector2 tilePos =new Vector2(uvTile.x, uvTile.y)*0.5f;
        uvs.Add(new Vector2(0.0f + tilePos.x, 0.0f + tilePos.y));
        uvs.Add(new Vector2(0.0f + tilePos.x, 0.5f + tilePos.y));
        uvs.Add(new Vector2(0.5f + tilePos.x, 0.5f + tilePos.y));
        uvs.Add(new Vector2(0.5f + tilePos.x, 0.0f + tilePos.y));

    }
}