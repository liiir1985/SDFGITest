//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;

namespace SDFGenerator
{
    public class Generator
    {
        Mesh mesh;
        MeshRenderer mr;
        public Generator(MeshRenderer mr, Mesh mesh)
        {
            this.mr = mr;
            this.mesh = mesh;
        }

        SDFComputeJob InitializeJob(int resolution, out NativeArray<SDFVoxel> voxels)
        {
            var bounds = mesh.bounds;
            bounds.size += Vector3.one * 0.125f;
            var size = bounds.size * resolution;
            var ceilSize = new Vector3Int(Mathf.CeilToInt(size.x), Mathf.CeilToInt(size.y), Mathf.CeilToInt(size.z));
            var dimension = ceilSize;
            var dimensionJob = new int3(dimension.x, dimension.y, dimension.z);
            // Get an array of triangles from the mesh.
            Vector3[] meshVertices = mesh.vertices;
            int[] meshTriangles = mesh.triangles;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] indexStarts = new int[mesh.subMeshCount];
            for (int i = 0; i < indexStarts.Length; i++)
            {
                var submesh = mesh.GetSubMesh(i);
                indexStarts[i] = submesh.indexStart;
            }
            NativeArray<float2> uvArray = new NativeArray<float2>(uvs.Length, Allocator.TempJob);
            for (int i = 0; i < uvs.Length; i++)
            {
                uvArray[i] = uvs[i];
            }
            NativeArray<TriangleData> triangleArray = new NativeArray<TriangleData>(meshTriangles.Length / 3, Allocator.TempJob);
            int submeshIdx = -1;
            int curStart = indexStarts[0];
            for (int t = 0; t < triangleArray.Length; t++)
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
                data.a = meshVertices[meshTriangles[index + 0]] - bounds.center;
                data.b = meshVertices[meshTriangles[index + 1]] - bounds.center;
                data.c = meshVertices[meshTriangles[index + 2]] - bounds.center;
                data.vertIdx = new int3(meshTriangles[index + 0], meshTriangles[index + 1], meshTriangles[index + 2]);
                data.normal = math.normalize((normals[meshTriangles[index + 0]] + normals[meshTriangles[index + 1]] + normals[meshTriangles[index + 2]]) / 3);
                //data.normal = normals[t];
                //data.uv = uvs[t];
                data.subMeshIdx = submeshIdx;

                triangleArray[t] = data;
            }

            voxels = new NativeArray<SDFVoxel>(dimension.x * dimension.y * dimension.z, Allocator.Persistent);

            var mats = mr.sharedMaterials;
            TextureInfo[] albedo = new TextureInfo[mesh.subMeshCount];
            TextureInfo[] surface = new TextureInfo[mesh.subMeshCount];
            TextureInfo[] emission = new TextureInfo[mesh.subMeshCount];
            NativeArray<SDFTextureInfo> albedoInfo = new NativeArray<SDFTextureInfo>(mesh.subMeshCount, Allocator.TempJob);
            NativeArray<SDFTextureInfo> surfaceInfo = new NativeArray<SDFTextureInfo>(mesh.subMeshCount, Allocator.TempJob);
            NativeArray<SDFTextureInfo> emissionInfo = new NativeArray<SDFTextureInfo>(mesh.subMeshCount, Allocator.TempJob);

            int[] pixels = new int[3];
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (i < mats.Length)
                {
                    var mat = mats[i];
                    albedo[i].Texture = mat.GetTexture("_BaseMap") as Texture2D;
                    albedo[i].Tint = mat.GetColor("_BaseColor");
                    if (albedo[i].Texture)
                    {
                        pixels[0] += albedo[i].Texture.width * albedo[i].Texture.height;
                    }
                    else
                        pixels[0] += 1;
                    surface[i].Texture = mat.GetTexture("_MetallicGlossMap") as Texture2D;
                    float metallic = 1f;
                    if (!surface[i].Texture)
                        metallic = mat.GetFloat("_Metallic");
                    surface[i].Tint = new Color(mat.GetFloat("_Smoothness"), metallic, 0, 0);

                    if (surface[i].Texture)
                    {
                        pixels[1] += surface[i].Texture.width * surface[i].Texture.height;
                    }
                    else
                        pixels[1] += 1;
                    emission[i].Texture = mat.GetTexture("_EmissionMap") as Texture2D;
                    emission[i].Tint = mat.GetColor("_EmissionColor");
                    if (emission[i].Texture)
                    {
                        pixels[2] += emission[i].Texture.width * emission[i].Texture.height;
                    }
                    else
                        pixels[2] += 1;
                }
                else
                {
                    albedo[i].Texture = null;
                    albedo[i].Tint = Color.magenta;
                    surface[i].Texture = null;
                    surface[i].Tint = new Color(0.5f, 0.5f, 0, 0);
                    emission[i].Texture = null;
                    emission[i].Tint = Color.black;
                    pixels[0]++;
                    pixels[1]++;
                    pixels[2]++;
                }
            }
            var sdfJob = new SDFComputeJob()
            {
                Triangles = triangleArray,
                UVs = uvArray,
                Voxels = voxels,
                Dimension = dimensionJob,
                BoundSize = bounds.size,
                Resolution = resolution,
                AlbedoMap = ConvertTexture(albedo, pixels[0], albedoInfo),
                AlbedoInfo = albedoInfo,
                SurfaceMap = ConvertTexture(surface, pixels[1], surfaceInfo, (c) => new Vector4(c.y, c.w, 0, 0)),
                SurfaceInfo = surfaceInfo,
                EmissionMap = ConvertTexture(emission, pixels[2], emissionInfo),
                EmissionInfo = emissionInfo
            };

            return sdfJob;
        }

        public (SDFVoxel, TriangleData) CalculateVoxel(int resolution, float3 modelPos)
        {
            var sdfJob = InitializeJob(resolution, out var voxels);
            var uv = modelPos / sdfJob.BoundSize + 0.5f;
            var pos = uv * sdfJob.Dimension;
            int index = (int)(pos.z * sdfJob.Dimension.y * sdfJob.Dimension.x + pos.y * sdfJob.Dimension.x + pos.x);
            SDFVoxel voxel = new SDFVoxel();
            sdfJob.CalculateVoxel(ref voxel, index, out var t);
            voxels.Dispose();
            return (voxel, t);
        }

        public void Generate(int resolution, out NativeArray<SDFVoxel> voxels, out int3 sdfDimension, out float3 sdfBounds)
        {
            var sdfJob = InitializeJob(resolution, out voxels);
            sdfDimension = sdfJob.Dimension;
            sdfBounds = sdfJob.BoundSize;
            var sdfJobHandle = sdfJob.ScheduleBatch(voxels.Length, 128);

            sdfJobHandle.Complete();
            sdfJob.DisposeNativeArrays();
        }

        NativeArray<float4> ConvertTexture(TextureInfo[] info, int pixels, NativeArray<SDFTextureInfo> tInfos, System.Func<Vector4, Vector4> conv = null)
        {
            NativeArray<float4> result = new NativeArray<float4>(pixels, Allocator.TempJob);
            int curOffset = 0;
            for (int i = 0; i < info.Length; i++)
            {
                var tex = info[i].Texture;
                SDFTextureInfo tInfo = new SDFTextureInfo();
                tInfo.DataOffset = curOffset;
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
                        result[curOffset + j] = Vector4.Scale(conv != null ? conv(c) : (Vector4)c, info[i].Tint);
                    }
                    curOffset += colors.Length;
                }
                else
                {
                    tInfo.Size = new int2(1.1);
                    result[curOffset++] = (Vector4)info[i].Tint;
                }
                tInfos[i] = tInfo;
            }
            return result;
        }

    }
}