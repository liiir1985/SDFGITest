//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using Unity.Jobs;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using static SDFGenerator.Montcalo;
using static SDFGenerator.RandomFunc;
using static SDFGenerator.Common;
using static Unity.Mathematics.math;

namespace SDFGenerator
{
    [BurstCompile]
    public struct SDFGIJob : IJobParallelForBatch
    {
        [ReadOnly]
        public NativeArray<SDFVoxel> Voxels;
        [ReadOnly]
        public NativeArray<SDFVolumeInfo> VolumeInfos;
        [ReadOnly]
        public NativeArray<BVHNodeInfo> BVHTree;
        [ReadOnly]
        public NativeArray<float> DepthMap;
        [ReadOnly]
        public NativeArray<float4> AlbedoMap;
        [ReadOnly]
        public NativeArray<float4> NormalMap;
        [ReadOnly]
        public NativeArray<float4> MetallicMap;
        [WriteOnly]
        public NativeArray<half4> GIMap;
        public float4x4 ViewProjectionMatrixInv;
        public int2 GBufferDimension;
        public int2 Dimension;
        public float3 EyePos;
        public int FrameIDMod8;
        public void Execute(int startIndex, int count)
        {
            int endIdx = startIndex + count;
            
            for (int idx = startIndex; idx < endIdx; idx++)
            {
                uint2 Random = Rand3DPCG16(int3(idx % Dimension.x, idx / Dimension.x, FrameIDMod8)).xy;
                var uv = ToUV(idx);
                var depth = tex2d(DepthMap, uv, GBufferDimension);
                if (depth > float.Epsilon)
                {
                    var normal = tex2d(NormalMap, uv, GBufferDimension);
                    var worldPos = DepthToWorldPos(uv, depth);
                    var rayDir = normalize(worldPos - EyePos);

                    var hash = Hammersley16((uint)(idx), (uint)GIMap.Length, Random);
                    var H = ImportanceSampleGGX(hash, 1f - normal.w);
                    float pdf = H.w;
                    var N = TangentToWorld(H.xyz, normalize(normal.xyz));
                    RayMarch(worldPos, reflect(rayDir, N.xyz));
                }
            }
        }

        void RayMarch(float3 pos, float3 dir)
        {
            for (int i = 0; i < BVHTree.Length; )
            {
                var node = BVHTree[i];
                if (node.SDFVolume >= 0)
                {
                    if (IntersectAABBRay(node.Bounds, pos, dir))
                    {
                        var volume = VolumeInfos[node.SDFVolume];
                        i++;
                    }
                    else
                        i = node.FalseLink;
                }
                else
                {
                    if (IntersectAABBRay(node.Bounds, pos, dir))
                        i++;
                    else
                        i = node.FalseLink;
                }
            }
        }

        float3 DepthToWorldPos(float2 uv_depth, float depth)
        {
            float4 H = new float4(uv_depth.x * 2.0f - 1.0f, (uv_depth.y) * 2.0f - 1.0f, depth, 1.0f);
            float4 D = math.mul(ViewProjectionMatrixInv, H);
            return (D / D.w).xyz;
        }

        float4 tex2d(NativeArray<float4> tex, float2 uv, int2 size)
        {
            var maxSize = size - 1;
            uv = math.frac(uv);

            var uv_img = uv * size;
            var uv0 = math.clamp(math.floor(uv_img), Unity.Mathematics.int2.zero, maxSize);
            var uv1 = math.clamp(uv0 + 1, Unity.Mathematics.int2.zero, maxSize);


            var cuv0 = math.lerp(tex[(int)(uv0.y * size.x + uv0.x)], tex[(int)(uv0.y * size.x + uv1.x)], uv_img.x - uv0.x);
            var cuv1 = math.lerp(tex[(int)(uv1.y * size.x + uv0.x)], tex[(int)(uv1.y * size.x + uv1.x)], uv_img.x - uv0.x);
            var cFinal = math.lerp(cuv0, cuv1, uv_img.y - uv0.y);
            return cFinal;
        }

        float tex2d(NativeArray<float> tex, float2 uv, int2 size)
        {
            var maxSize = size - 1;
            uv = math.frac(uv);

            var uv_img = uv * size;
            var uv0 = math.clamp(math.floor(uv_img), Unity.Mathematics.int2.zero, maxSize);
            var uv1 = math.clamp(uv0 + 1, Unity.Mathematics.int2.zero, maxSize);


            var cuv0 = math.lerp(tex[(int)(uv0.y * size.x + uv0.x)], tex[(int)(uv0.y * size.x + uv1.x)], uv_img.x - uv0.x);
            var cuv1 = math.lerp(tex[(int)(uv1.y * size.x + uv0.x)], tex[(int)(uv1.y * size.x + uv1.x)], uv_img.x - uv0.x);
            var cFinal = math.lerp(cuv0, cuv1, uv_img.y - uv0.y);
            return cFinal;
        }

        float dot2(float3 v)
        {
            return math.dot(v, v);
        }

        bool IsZeroOne(float a)
        {
            return a >= 0 && a <= 1;
        }

        float2 ToUV(int id)
        {
            int y = id / Dimension.x;
            int x = id % Dimension.x;

            return new float2((float)x / Dimension.x, (float)y / Dimension.y);
        }

        public void DisposeNativeArrays()
        {
            
        }
    }
}