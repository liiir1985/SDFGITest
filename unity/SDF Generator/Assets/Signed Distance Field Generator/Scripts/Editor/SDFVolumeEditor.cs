//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;

using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace SDFGenerator
{
    [CustomEditor(typeof(SDFVolume))]
    public class SDFVolumeEditor : Editor
    {
        SerializedProperty sdfData;
        TextAsset lastSDF;
        Vector3Int dimension;
        Vector3 size;

        int resolution = 8;
        private void OnEnable()
        {
            sdfData = serializedObject.FindProperty("sdfData");
        }

        public unsafe override void OnInspectorGUI()
        {
            SDFVolume baker = target as SDFVolume;
            var oldAsset = sdfData.objectReferenceValue as TextAsset;
            EditorGUILayout.PropertyField(sdfData, new GUIContent("SDF Asset"));
            if (oldAsset)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (lastSDF != oldAsset)
                    {
                        System.IO.MemoryStream ms = new System.IO.MemoryStream(oldAsset.bytes);
                        System.IO.BinaryReader br = new System.IO.BinaryReader(ms);
                        br.ReadInt32();
                        int resolusion = br.ReadInt32();
                        dimension = new Vector3Int(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
                        var center = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        size = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        lastSDF = oldAsset;
                    }
                    EditorGUILayout.Vector3IntField("Dimension", dimension);
                    EditorGUILayout.Vector3Field("Size", size);
                    var vSize = sizeof(SDFVoxel);
                    var pixels = dimension.x * dimension.y * dimension.z;
                    EditorGUILayout.LabelField($"File Size:{(pixels * vSize) / 1024f / 1024f: 0.###}MB");
                }
            }
            EditorGUILayout.Separator();
            if (baker)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("Bake");
                    resolution = EditorGUILayout.IntField("Resolution", resolution);
                    if (GUILayout.Button("Bake"))
                    {
                        string path = oldAsset ? AssetDatabase.GetAssetPath(oldAsset) : null;
                        path = Bake(baker.gameObject, path);
                        if (!string.IsNullOrEmpty(path))
                        {
                            var data = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                            sdfData.objectReferenceValue = data;
                        }
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        unsafe string Bake(GameObject go, string path)
        {
            Generator gen = new Generator(go);
            var dimension = gen.GetDimension(resolution);
            var vSize = sizeof(SDFVoxel);
            var pixels = dimension.x * dimension.y * dimension.z;
            if (EditorUtility.DisplayDialog("Confirm", $"The generated SDF Data will be at ({dimension.x}x{dimension.y}x{dimension.z}),\n the file size will be {(pixels * vSize) / 1024f / 1024f: 0.##}MB, continue?", "OK", "Cancel"))
            {
                if (string.IsNullOrEmpty(path))
                {
                    string folder = System.IO.Path.GetDirectoryName(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
                    path = EditorUtility.SaveFilePanelInProject("Save As", go.name + "_SDF", "bytes", "", folder);

                    // ... If they hit cancel.
                    if (path == null || path.Equals(""))
                    {
                        return null;
                    }
                }
                var center = gen.Bounds.center;
                gen.Generate(resolution, out var voxels, out dimension, out var bounds);

                using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
                {
                    using (System.IO.BinaryWriter bw = new System.IO.BinaryWriter(fs))
                    {
                        bw.Write(0xAFFEAFFE);
                        bw.Write(resolution);
                        bw.Write(dimension.x);
                        bw.Write(dimension.y);
                        bw.Write(dimension.z);
                        bw.Write(center.x);
                        bw.Write(center.y);
                        bw.Write(center.z);
                        bw.Write(bounds.x);
                        bw.Write(bounds.y);
                        bw.Write(bounds.z);

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

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return path;
            }
            else
                return null;
        }
        bool IsPowerOfTwo(int x)
        {
            return (x & (x - 1)) == 0;
        }

    }
}
