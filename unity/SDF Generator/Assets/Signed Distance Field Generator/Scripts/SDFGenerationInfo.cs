//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace SDFGenerator
{
    struct TextureInfo
    {
        public Texture2D Texture;
        public Color Tint;
    }


    struct SDFMesh
    {
        public Vector3[] Vertices;
        public int[] Triangles;
        public int TriangleCount;
        public Vector3[] Normals;
        public Vector2[] UVs;
        public int[] IndexStarts;
        public int SubMeshCount;
        public TextureInfo[] Albedo;
        public TextureInfo[] Surface;
        public TextureInfo[] Emission;
        public int AlbedoPixels;
        public int SurfacePixels;
        public int EmissionPixels;
        public int SubmeshOffset;
        public Matrix4x4 Local2World;

        public SDFMesh(Mesh mesh, Material[] mats)
        {
            Vertices = mesh.vertices;
            Triangles = mesh.triangles;
            TriangleCount = Triangles.Length / 3;
            Normals = mesh.normals;
            UVs = mesh.uv;
            SubMeshCount = mesh.subMeshCount;
            IndexStarts = new int[mesh.subMeshCount];
            for (int i = 0; i < IndexStarts.Length; i++)
            {
                var submesh = mesh.GetSubMesh(i);
                IndexStarts[i] = submesh.indexStart;
            }
            Albedo = new TextureInfo[SubMeshCount];
            Surface = new TextureInfo[SubMeshCount];
            Emission = new TextureInfo[SubMeshCount];
            AlbedoPixels = 0;
            SurfacePixels = 0;
            EmissionPixels = 0;
            SubmeshOffset = 0;
            Local2World = Matrix4x4.identity;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (i < mats.Length)
                {
                    var mat = mats[i];
                    Albedo[i].Texture = mat.GetTexture("_BaseMap") as Texture2D;
                    Albedo[i].Tint = mat.GetColor("_BaseColor");
                    if (Albedo[i].Texture)
                    {
                        AlbedoPixels += Albedo[i].Texture.width * Albedo[i].Texture.height;
                    }
                    else
                        AlbedoPixels += 1;
                    Surface[i].Texture = mat.GetTexture("_MetallicGlossMap") as Texture2D;
                    float metallic = 1f;
                    if (!Surface[i].Texture)
                        metallic = mat.GetFloat("_Metallic");
                    Surface[i].Tint = new Color(mat.GetFloat("_Smoothness"), metallic, 0, 0);

                    if (Surface[i].Texture)
                    {
                        SurfacePixels += Surface[i].Texture.width * Surface[i].Texture.height;
                    }
                    else
                        SurfacePixels += 1;
                    Emission[i].Texture = mat.GetTexture("_EmissionMap") as Texture2D;
                    Emission[i].Tint = mat.GetColor("_EmissionColor");
                    if (Emission[i].Texture)
                    {
                        EmissionPixels += Emission[i].Texture.width * Emission[i].Texture.height;
                    }
                    else
                        EmissionPixels += 1;
                }
                else
                {
                    Albedo[i].Texture = null;
                    Albedo[i].Tint = Color.magenta;
                    Surface[i].Texture = null;
                    Surface[i].Tint = new Color(0.5f, 0.5f, 0, 0);
                    Emission[i].Texture = null;
                    Emission[i].Tint = Color.black;
                    AlbedoPixels++;
                    SurfacePixels++;
                    EmissionPixels++;
                }
            }
        }
    }
    public class SDFGenerationInfo
    {
        List<SDFMesh> meshes;
        int totalSubmeshes;
        int totalTriangles;
        int totalVertices;
        int totalAlbedoPixels;
        int totalSurfacePixels;
        int totalEmissionPixels;
        Bounds bounds;
        Matrix4x4 baseWorld2Local;

        public Bounds Bounds => bounds;

        void EncapsulateBounds(ref Bounds bounds,ref Matrix4x4 local2world, Bounds meshBounds)
        {
            bounds.Encapsulate(AABB.Transform(local2world, meshBounds.ToAABB()).ToBounds());
        }

        public SDFGenerationInfo(GameObject go)
        {
            baseWorld2Local = go.transform.worldToLocalMatrix;
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            var lods = go.GetComponentsInChildren<LODGroup>();
            HashSet<MeshRenderer> invalidRenderers = new HashSet<MeshRenderer>();
            foreach(var i in lods)
            {
                var ls = i.GetLODs();
                if(ls.Length > 1)
                {
                    for (int j = 1; j < ls.Length; j++)
                    {
                        foreach (var r in ls[j].renderers)
                        {
                            if (r is MeshRenderer mr)
                                invalidRenderers.Add(mr);
                        }
                    }
                }
            }
            meshes = new List<SDFMesh>();
            bounds = new Bounds();
            int submeshOffset = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (invalidRenderers.Contains(renderers[i]))
                    continue;
                var mats = renderers[i].sharedMaterials;
                var filter = renderers[i].GetComponent<MeshFilter>();
                if (filter)
                {
                    var mesh = filter.sharedMesh;
                    var local2world = baseWorld2Local * filter.transform.localToWorldMatrix;
                    EncapsulateBounds(ref bounds, ref local2world, mesh.bounds);
                    var m = new SDFMesh(mesh, mats);
                    m.Local2World = local2world;
                    m.SubmeshOffset = submeshOffset;
                    totalSubmeshes += m.SubMeshCount;
                    totalTriangles += (m.TriangleCount);
                    totalVertices += m.Vertices.Length;
                    totalAlbedoPixels += m.AlbedoPixels;
                    totalSurfacePixels += m.SurfacePixels;
                    totalEmissionPixels += m.EmissionPixels;
                    meshes.Add(m);
                    submeshOffset += m.SubMeshCount;
                }
            }
        }

        Vector3 TransformPoint(Vector3 p, ref Matrix4x4 local2world)
        {
            Vector4 v = new Vector4(p.x, p.y, p.z, 1);
            return local2world * v;
        }

        Vector3 TransformNormal(Vector3 p, ref Matrix4x4 local2world)
        {
            Vector4 v = new Vector4(p.x, p.y, p.z, 0);
            return local2world * v;
        }

        public void InitializeJob(ref SDFComputeJob job)
        {
            NativeArray<TriangleData> triangleArray = new NativeArray<TriangleData>(totalTriangles, Allocator.TempJob);
            NativeArray<float2> uvArray = new NativeArray<float2>(totalVertices, Allocator.TempJob);
            NativeArray<float4> albedoMap = new NativeArray<float4>(totalAlbedoPixels, Allocator.TempJob);
            NativeArray<SDFTextureInfo> albedoInfo = new NativeArray<SDFTextureInfo>(totalSubmeshes, Allocator.TempJob);
            NativeArray<float4> surfaceMap = new NativeArray<float4>(totalSurfacePixels, Allocator.TempJob);
            NativeArray<SDFTextureInfo> surfaceInfo = new NativeArray<SDFTextureInfo>(totalSubmeshes, Allocator.TempJob);
            NativeArray<float4> emissionMap = new NativeArray<float4>(totalEmissionPixels, Allocator.TempJob);
            NativeArray<SDFTextureInfo> emissionInfo = new NativeArray<SDFTextureInfo>(totalSubmeshes, Allocator.TempJob);
            int triangleOffset = 0;
            int verticesOffset = 0;
            int albedoOffset = 0;
            int surfaceOffset = 0;
            int emissionOffset = 0;

            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh = meshes[i];
                var uvs = mesh.UVs;
                for (int j = 0; j < uvs.Length; j++)
                {
                    uvArray[verticesOffset + j] = uvs[j];
                }

                int submeshIdx = -1;
                var indexStarts = mesh.IndexStarts;
                int curStart = indexStarts[0];
                var meshTriangles = mesh.Triangles;
                var meshVertices = mesh.Vertices;
                var normals = mesh.Normals;
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    TriangleData data = new TriangleData();
                    var index = 3 * t;
                    if (index >= curStart)
                    {
                        submeshIdx++;
                        if (indexStarts.Length > submeshIdx + 1)
                        {
                            curStart = indexStarts[submeshIdx + 1];
                        }
                        else
                            curStart = meshTriangles.Length;
                    }
                    data.a = TransformPoint(meshVertices[meshTriangles[index + 0]],ref mesh.Local2World) - bounds.center;
                    data.b = TransformPoint(meshVertices[meshTriangles[index + 1]],ref mesh.Local2World) - bounds.center;
                    data.c = TransformPoint(meshVertices[meshTriangles[index + 2]],ref mesh.Local2World) - bounds.center;
                    data.vertIdx = new int3(meshTriangles[index + 0], meshTriangles[index + 1], meshTriangles[index + 2]);
                    data.vertIdx += verticesOffset;
                    data.normal = TransformNormal(math.normalize((normals[meshTriangles[index + 0]] + normals[meshTriangles[index + 1]] + normals[meshTriangles[index + 2]]) / 3), ref mesh.Local2World);
                    //data.normal = normals[t];
                    //data.uv = uvs[t];
                    data.subMeshIdx = submeshIdx + mesh.SubmeshOffset;
                    triangleArray[triangleOffset + t] = data;
                }
                ConvertTexture(mesh.Albedo, albedoMap, albedoInfo, ref albedoOffset, mesh.SubmeshOffset);
                ConvertTexture(mesh.Surface, surfaceMap, surfaceInfo, ref surfaceOffset, mesh.SubmeshOffset, (c) => new Vector4(c.y, c.w, 0, 0));
                ConvertTexture(mesh.Emission, emissionMap, emissionInfo, ref emissionOffset, mesh.SubmeshOffset);

                triangleOffset += mesh.TriangleCount;
                verticesOffset += mesh.Vertices.Length;
            }


            job.Triangles = triangleArray;
            job.UVs = uvArray;
            job.AlbedoMap = albedoMap;
            job.AlbedoInfo = albedoInfo;
            job.SurfaceMap = surfaceMap;
            job.SurfaceInfo = surfaceInfo;
            job.EmissionMap = emissionMap;
            job.EmissionInfo = emissionInfo;
        }

        void ConvertTexture(TextureInfo[] info, NativeArray<float4> result, NativeArray<SDFTextureInfo> tInfos, ref int pixelOffset, int submeshOffset, System.Func<Vector4, Vector4> conv = null)
        {
            for (int i = 0; i < info.Length; i++)
            {
                var tex = info[i].Texture;
                SDFTextureInfo tInfo = new SDFTextureInfo();
                tInfo.DataOffset = pixelOffset;
                if (tex)
                {
                    tInfo.Size = new int2(tex.width, tex.height);
                    var path = AssetDatabase.GetAssetPath(tex);
                    TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (!ti.isReadable)
                    {
                        ti.isReadable = true;
                        ti.SaveAndReimport();
                    }
                    var colors = tex.GetPixels();
                    for (int j = 0; j < colors.Length; j++)
                    {
                        var c = colors[j];
                        result[pixelOffset + j] = Vector4.Scale(conv != null ? conv(c) : (Vector4)c, info[i].Tint);
                    }
                    pixelOffset += colors.Length;
                }
                else
                {
                    tInfo.Size = new int2(1.1);
                    result[pixelOffset++] = (Vector4)info[i].Tint;
                }
                tInfos[i + submeshOffset] = tInfo;
            }
        }
    }
}