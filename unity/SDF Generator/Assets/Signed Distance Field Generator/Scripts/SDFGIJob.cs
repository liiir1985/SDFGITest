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
        public NativeArray<half4> NormalMap;
        [WriteOnly]
        public NativeArray<half4> GIMap;
        public float4x4 ViewProjectionMatrixInv;
        public int2 GBufferDimension;
        public int2 Dimension;
        public float3 EyePos;
        public void Execute(int startIndex, int count)
        {
            int endIdx = startIndex + count;
            for (int idx = startIndex; idx < endIdx; idx++)
            {
                var uv = ToUV(idx);
                var depth = tex2d(DepthMap, uv, GBufferDimension);
                if (depth > float.Epsilon)
                {
                    var normal = tex2d(NormalMap, uv, GBufferDimension);
                    var worldPos = DepthToWorldPos(uv, depth);
                    var rayDir = math.normalize(worldPos - EyePos);
                    
                }
            }
        }

        float3 DepthToWorldPos(float2 uv_depth, float depth)
        {
            float4 H = new float4(uv_depth.x * 2.0f - 1.0f, (uv_depth.y) * 2.0f - 1.0f, depth, 1.0f);
            float4 D = math.mul(ViewProjectionMatrixInv, H);
            return (D / D.w).xyz;
        }

        float4 tex2d(NativeArray<half4> tex, float2 uv, int2 size)
        {
            var maxSize = size - 1;
            uv = math.frac(uv);

            var uv_img = uv * size;
            var uv0 = math.clamp(math.floor(uv_img), int2.zero, maxSize);
            var uv1 = math.clamp(uv0 + 1, int2.zero, maxSize);


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
            var uv0 = math.clamp(math.floor(uv_img), int2.zero, maxSize);
            var uv1 = math.clamp(uv0 + 1, int2.zero, maxSize);


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