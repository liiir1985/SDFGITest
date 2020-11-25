//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

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
        public NativeArray<half4> DepthMap;
        [ReadOnly]
        public NativeArray<half4> NormalMap;
        public NativeArray<half4> GIMap;
        public int2 GBufferDimension;
        public int2 Dimension;
        public void Execute(int startIndex, int count)
        {
            int endIdx = startIndex + count;
            for (int idx = startIndex; idx < endIdx; idx++)
            {
                var uv = ToUV(idx);
            }
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