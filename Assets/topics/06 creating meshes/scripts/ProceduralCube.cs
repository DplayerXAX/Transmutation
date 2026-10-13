using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof (MeshFilter))]
[RequireComponent(typeof (MeshRenderer))]
public class ProceduralCube : MonoBehaviour {
    Mesh mesh;
    void Start() {
        MakeCube();
    }

    void MakeCube() {
        Vector3[] vertices = { 
            new Vector3(0,0,0),
            new Vector3(1,0,0),
            new Vector3(1,1,0),
            new Vector3(0,1,0),
            new Vector3(0,1,1),
            new Vector3(1,1,1),
            new Vector3(1,0,1),
            new Vector3(0,0,1)
        };

        int[] triangles = { 
            0,2,1,
            0,3,2,
            3,5,2,
            3,4,5,
            1,5,6,
            1,2,5,
            0,7,4,
            0,4,3,
            7,5,4,
            7,6,5,
            0,1,6,
            0,6,7
        };
        mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        GetComponent<MeshFilter>().mesh = mesh;
    }

    private void OnDestroy()
    {
        Destroy(mesh);
    }

}