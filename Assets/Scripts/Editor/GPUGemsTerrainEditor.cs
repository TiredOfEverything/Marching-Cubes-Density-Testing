using UnityEditor;
using UnityEngine;

[CustomEditor (typeof (GPUGemsTerrain))]
public class GPUGemsTerrainEditor : Editor {
    private GPUGemsTerrain terrain;
    private bool generateMesh;

    private void OnEnable () {
        terrain = (GPUGemsTerrain) target;
    }

    public override void OnInspectorGUI () {
        DrawDefaultInspector ();

        if (GUI.changed) {
            generateMesh = true;
        }

        if (GUILayout.Button ("Generate Mesh")) {
            generateMesh = true;
        }

        if (generateMesh) {
            terrain.GenerateMesh ();
            generateMesh = false;
        }
    }
}