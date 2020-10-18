//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;

using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using SDFGenerator;

public class SDF : EditorWindow
{
    Mesh mesh;
    MeshRenderer mr;
    int subMeshIndex = 0;
    float padding = 0f;
    int resolution = 32;

    // For triangle buffer.
    private struct Triangle
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 c;
    }

    [MenuItem("Signed Distance Field/Generate")]
    static void Window()
    {
        SDF window = CreateInstance(typeof(SDF)) as SDF;
        window.ShowUtility();
    }

    private void OnGUI()
    {
        // Verify that compute shaders are supported.
        if (!SystemInfo.supportsComputeShaders)
        {
            EditorGUILayout.HelpBox("This tool requires a GPU that supports compute shaders.", MessageType.Error);

            if (GUILayout.Button("Close"))
            {
                Close();
            }

            return;
        }

        // Assign the mesh.
        mesh = EditorGUILayout.ObjectField("Mesh", mesh, typeof(Mesh), false) as Mesh;
        mr = EditorGUILayout.ObjectField("Mesh Renderer", mr, typeof(MeshRenderer), true) as MeshRenderer;
        // If the mesh is null, don't draw the rest of the GUI.
        if (mesh == null && mr == null)
        {
            if (GUILayout.Button("Close"))
            {
                Close();
            }

            return;
        }
        if (mr)
        {
            var mf = mr.GetComponent<MeshFilter>();
            mesh = mf.sharedMesh;
        }
        // Assign the sub-mesh index, if there are more than 1 in the mesh.
        if (mesh.subMeshCount > 1)
        {
            subMeshIndex = (int)Mathf.Max(EditorGUILayout.IntField("Submesh Index", subMeshIndex), 0f);
        }

        // Assign the padding around the mesh.
        padding = EditorGUILayout.Slider("Padding", padding, 0f, 1f);

        // Assign the SDF resolution.
        resolution = (int)Mathf.Max(EditorGUILayout.IntField("Resolution", resolution), 1f);

        if (GUILayout.Button("Create"))
        {
            if (mr)
                CreateSDFJob();
            else
                CreateSDF();
        }

        if (GUILayout.Button("Close"))
        {
            Close();
        }
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private unsafe void CreateSDFJob()
    {
        // Prompt the user to save the file.
        string path = EditorUtility.SaveFilePanelInProject("Save As", mesh.name + "_SDF", "bytes", "");

        // ... If they hit cancel.
        if (path == null || path.Equals(""))
        {
            return;
        }

        if (!IsPowerOfTwo(resolution))
        {
            Debug.LogError("Resolution is not power of 2");
            return;
        }

        Generator gen = new Generator(mr, mesh);
        gen.Generate(resolution, out var voxels, out var dimension, out var bounds);

        using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
        {
            using (System.IO.BinaryWriter bw = new System.IO.BinaryWriter(fs))
            {
                bw.Write(0xAFFEAFFE);
                bw.Write(resolution);
                bw.Write(dimension.x);
                bw.Write(dimension.y);
                bw.Write(dimension.z);
                bw.Write(bounds.x);
                bw.Write(bounds.y);
                bw.Write(bounds.z);

                var vSize = sizeof(SDFVoxel);
                byte[] buffer = new byte[vSize];
                fixed (byte* ptr = buffer)
                {
                    SDFVoxel* v = (SDFVoxel*)ptr;
                    for (int i = 0; i < voxels.Length; i++)
                    {
                        *v = voxels[i];
                        bw.Write(buffer);
                    }
                }
            }
        }
        voxels.Dispose();
    }

    bool IsPowerOfTwo(int x)
    {
        return (x & (x - 1)) == 0;
    }

    private void CreateSDF()
    {
        // Prompt the user to save the file.
        string path = EditorUtility.SaveFilePanelInProject("Save As", mesh.name + "_SDF", "asset", "");

        // ... If they hit cancel.
        if (path == null || path.Equals(""))
        {
            return;
        }

        // Get the Texture3D representation of the SDF.
        Texture3D voxels = ComputeSDF();

        // Save the Texture3D asset at path.
        AssetDatabase.CreateAsset(voxels, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Close the window.
        Close();

        // Select the SDF in the project view.
        Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(path);
    }

    private Texture3D ComputeSDF()
    {
        UnityEngine.Profiling.Profiler.BeginSample("SDF Compute");
        // Create the voxel texture.
        Texture3D voxels = new Texture3D(resolution, resolution, resolution, TextureFormat.RGBAHalf, false);
        voxels.anisoLevel = 1;
        voxels.filterMode = FilterMode.Bilinear;
        voxels.wrapMode = TextureWrapMode.Clamp;

        // Get an array of pixels from the voxel texture, create a buffer to
        // hold them, and upload the pixels to the buffer.
        Color[] pixelArray = voxels.GetPixels(0);
        ComputeBuffer pixelBuffer = new ComputeBuffer(pixelArray.Length, sizeof(float) * 4);
        pixelBuffer.SetData(pixelArray);

        // Get an array of triangles from the mesh.
        Vector3[] meshVertices = mesh.vertices;
        int[] meshTriangles = mesh.GetTriangles(subMeshIndex);
        Triangle[] triangleArray = new Triangle[meshTriangles.Length / 3];
        for (int t = 0; t < triangleArray.Length; t++)
        {
            triangleArray[t].a = meshVertices[meshTriangles[3 * t + 0]];  // - mesh.bounds.center;
            triangleArray[t].b = meshVertices[meshTriangles[3 * t + 1]];  // - mesh.bounds.center;
            triangleArray[t].c = meshVertices[meshTriangles[3 * t + 2]];  // - mesh.bounds.center;
        }

        // Create a buffer to hold the triangles, and upload them to the buffer.
        ComputeBuffer triangleBuffer = new ComputeBuffer(triangleArray.Length, sizeof(float) * 3 * 3);
        triangleBuffer.SetData(triangleArray);

        // Instantiate the compute shader from resources.
        ComputeShader compute = (ComputeShader)Instantiate(Resources.Load("SDF"));
        int kernel = compute.FindKernel("CSMain");

        // Upload the pixel buffer to the GPU.
        compute.SetBuffer(kernel, "pixelBuffer", pixelBuffer);
        compute.SetInt("pixelBufferSize", pixelArray.Length);

        // Upload the triangle buffer to the GPU.
        compute.SetBuffer(kernel, "triangleBuffer", triangleBuffer);
        compute.SetInt("triangleBufferSize", triangleArray.Length);

        // Calculate and upload the other necessary parameters.
        compute.SetInt("textureSize", resolution);
        Vector3 minExtents = Vector3.zero;
        Vector3 maxExtents = Vector3.zero;
        foreach (Vector3 v in mesh.vertices) {
            for (int i = 0; i < 3; i++) {
                minExtents[i] = Mathf.Min(minExtents[i], v[i]);
                maxExtents[i] = Mathf.Max(maxExtents[i], v[i]);
            }
        }
        compute.SetVector("minExtents", minExtents - Vector3.one*padding);
        compute.SetVector("maxExtents", maxExtents + Vector3.one*padding);

        // Compute the SDF.
        compute.Dispatch(kernel, pixelArray.Length / 256 + 1, 1, 1);

        // Destroy the compute shader and release the triangle buffer.
        DestroyImmediate(compute);
        triangleBuffer.Release();

        // Retrieve the pixel buffer and reapply it to the voxels texture.
        pixelBuffer.GetData(pixelArray);
        pixelBuffer.Release();
        voxels.SetPixels(pixelArray, 0);
        voxels.Apply();
        UnityEngine.Profiling.Profiler.EndSample();
        // Return the voxels texture.
        return voxels;
    }
}
