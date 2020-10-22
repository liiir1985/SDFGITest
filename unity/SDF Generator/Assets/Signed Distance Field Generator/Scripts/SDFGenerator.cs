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
        SDFGenerationInfo info;

        public Bounds Bounds => info.Bounds;
        public Generator(GameObject go)
        {
            info = new SDFGenerationInfo(go);
        }

        public int3 GetDimension(int resolution)
        {
            var bounds = info.Bounds;
            bounds.size += Vector3.one * 0.125f;
            var size = bounds.size * resolution;
            var ceilSize = new Vector3Int(Mathf.CeilToInt(size.x), Mathf.CeilToInt(size.y), Mathf.CeilToInt(size.z));
            var dimension = ceilSize;
            var dimensionJob = new int3(dimension.x, dimension.y, dimension.z);
            return dimensionJob;

        }

        SDFComputeJob InitializeJob(int resolution, out NativeArray<SDFVoxel> voxels)
        {
            var bounds = info.Bounds;
            bounds.size += Vector3.one * 0.125f;
            var size = bounds.size * resolution;
            var ceilSize = new Vector3Int(Mathf.CeilToInt(size.x), Mathf.CeilToInt(size.y), Mathf.CeilToInt(size.z));
            var dimension = ceilSize;
            var dimensionJob = new int3(dimension.x, dimension.y, dimension.z);
            
            voxels = new NativeArray<SDFVoxel>(dimension.x * dimension.y * dimension.z, Allocator.Persistent);

            var sdfJob = new SDFComputeJob()
            {
                Voxels = voxels,
                Dimension = dimensionJob,
                BoundSize = bounds.size,
                Resolution = resolution,                
            };
            info.InitializeJob(ref sdfJob);

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