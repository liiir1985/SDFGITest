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
        public NativeArray<half4> GIMap;
        public float4x4 ViewProjectionMatrixInv;
        public int2 GBufferDimension;
        public int2 Dimension;
        public float3 EyePos;
        public float3 LightDir;
        public float3 LightColor;
        public int FrameIDMod8;

        const float RussianRoulette = 0.25f;
        const int SPP = 32;

        public float3 PathTrace(int index)
        {
            float4 color = default;
            for (int i = 0; i < SPP; i++)
            {
                int3 randomSeed = int3(index % Dimension.x, index / Dimension.x, FrameIDMod8++);
                int randomIndex = 0;
                uint2 Random = Rand3DPCG16(randomSeed).xy;
                var uv = ToUV(index);
                var depth = tex2d(DepthMap, uv, GBufferDimension);
                if (depth > float.Epsilon)
                {
                    var normal = tex2d(NormalMap, uv, GBufferDimension);
                    var worldPos = DepthToWorldPos(uv, depth);
                    var rayDir = normalize(worldPos - EyePos);


                    var hash = Hammersley16((uint)(index), (uint)GIMap.Length, Random);
                    var H = ImportanceSampleGGX(hash, 1f - normal.w);
                    float pdf = H.w;
                    var N = TangentToWorld(H.xyz, normalize(normal.xyz));
                    var albedo = tex2d(AlbedoMap, uv, GBufferDimension);
                    rayDir = reflect(rayDir, N.xyz);
                    var L = RayMarch(worldPos, rayDir, index, GIMap.Length, randomSeed, ref randomIndex);
                    color += saturate(float4(L * (albedo.xyz / PI) * saturate(dot(rayDir, N.xyz)) / (H.w + float.Epsilon), 1f));
                    //color += saturate(float4(L, 1f));
                }
            }
            return (color.xyz / SPP); 
        }
        public void Execute(int startIndex, int count)
        {
            int endIdx = startIndex + count;
            int FrameIDMod8 = 0;
            Unity.Mathematics.Random rand = new Unity.Mathematics.Random((uint)startIndex + 1);
            for (int idx = startIndex; idx < endIdx; idx++)
            {
                float4 color = default;
                for (int i = 0; i < SPP; i++)
                {
                    int3 randomSeed = int3(idx % Dimension.x, idx / Dimension.x, FrameIDMod8++);
                    int randomIndex = 0;
                    uint2 Random = Rand3DPCG16(randomSeed).xy;
                    var uv = ToUV(idx);
                    var depth = tex2d(DepthMap, uv, GBufferDimension);
                    if (depth > float.Epsilon)
                    {
                        var normal = tex2d(NormalMap, uv, GBufferDimension);
                        var worldPos = DepthToWorldPos(uv, depth);
                        var rayDir = normalize(worldPos - EyePos);


                        var hash = rand.NextFloat2();// Hammersley16((uint)(idx), (uint)GIMap.Length, Random);
                        var H = ImportanceSampleGGX(hash, 1f - normal.w);
                        float pdf = H.w;
                        var N = TangentToWorld(H.xyz, normalize(normal.xyz));
                        var albedo = tex2d(AlbedoMap, uv, GBufferDimension);
                        rayDir = reflect(rayDir, N.xyz);
                        var L = RayMarch(worldPos, rayDir, idx, GIMap.Length, randomSeed, ref randomIndex);
                        //color += saturate(float4(L * (albedo.xyz / PI) * saturate(dot(rayDir, N.xyz)) / (H.w + float.Epsilon), 1f));
                        color += saturate(float4(L, 1f));
                    }
                }

                GIMap[idx] = (half4)(color / SPP);
            }
        }

        float3 RayMarch(float3 pos, float3 dir, int index, int numSamples, int3 randomSeed, ref int randomIdx)
        {
            SDFVoxel voxel;
            SDFVolumeInfo hitVolume;
            float3 hitPos;
            bool hit = RayCast(pos, dir, out voxel, out hitVolume, out hitPos);

            if (hit)
            {
                var normal = mul(float4(voxel.NormalSDF.xyz, 1), hitVolume.WorldToLocal).xyz;
                randomIdx++;
                randomSeed.z += randomIdx;
                uint2 Random = Rand3DPCG16(randomSeed).xy;
                var hash = Hammersley16((uint)(index), (uint)numSamples, Random);

                //Direct
                float3 L_dir = default;
                if (!RayCast(hitPos, LightDir, out _, out _, out _))
                {
                    var LoN = dot(LightDir, normal); 
                    L_dir = LightColor * ((float3)voxel.SurfaceAlbedoRough.xyz) * saturate(LoN);
                }
                L_dir = voxel.SurfaceAlbedoRough.xyz;
                if (hash.x < RussianRoulette)
                {
                    var worldPos = hitPos;
                    var rayDir = -dir;
                    var H = ImportanceSampleGGX(hash, 1 - voxel.SurfaceAlbedoRough.w);
                    float pdf = H.w;
                    var N = TangentToWorld(H.xyz, normalize(normal.xyz));
                }

                return L_dir;
            }
            else
                return default(float3);
        }

        bool RayCast(float3 pos, float3 dir, out SDFVoxel voxel, out SDFVolumeInfo hitVolume, out float3 hitPos)
        {
            voxel = default;
            hitVolume = default;
            hitPos = default;
            float minDistance = float.MaxValue;
            bool hit = false;
            for (int i = 0; i < BVHTree.Length;)
            {
                var node = BVHTree[i];
                if (node.SDFVolume >= 0)
                {
                    if (IntersectAABBRay(node.Bounds, pos, dir))
                    {
                        var volume = VolumeInfos[node.SDFVolume];
                        var localPos = mul(volume.WorldToLocal, float4(pos, 1)).xyz;
                        var localDir = normalize(mul(volume.WorldToLocal, float4(dir, 1)).xyz);
                        if (IntersectAABBRay(volume.SDFBounds, localPos, localDir, out float tmin, out float tmax))
                        {
                            localPos = localPos + localDir * (tmin + 0.01f);
                            SDFVoxel result = default;
                            if (RayCastSDF(ref volume, localPos, localDir, ref result, out var sdfPos))
                            {
                                sdfPos = mul(float4(sdfPos, 1), volume.WorldToLocal).xyz;
                                var dis = distancesq(sdfPos, pos);
                                if (dis < minDistance)
                                {
                                    hit = true;
                                    hitPos = sdfPos;
                                    hitVolume = volume;
                                    minDistance = dis;
                                    voxel = result;
                                }
                            }
                        }
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

            return hit;
        }
    
        //float3 BRDF(SDFVoxel voxel, float3 normal, float3 wi)
        //{

        //}

        bool RayCastSDF(ref SDFVolumeInfo sdf, float3 pos, float3 dir, ref SDFVoxel voxel, out float3 hitPos)
        {
            var sdfPos = pos - sdf.SDFBounds.Center; ;
            var sizeBound = new AABB();
            sizeBound.Extents = sdf.SDFBounds.Extents;
            float curDis;
            bool hit = false;
            do
            {
                hitPos = sdfPos;
                var sdfuv = sdfPos / sdf.SDFBounds.Size + 0.5f;
                voxel = SampleSDF(sdf.StartIndex, sdf.EndIndex, sdfuv, sdf.Dimension);
                curDis = voxel.NormalSDF.w;
                var NoL = dot(voxel.NormalSDF.xyz, dir);
                hit = curDis < 0.125f && NoL < 0;
                sdfPos = sdfPos + dir * abs(curDis);
            }
            while (!hit && sizeBound.Contains(hitPos));
            return hit;
        }

        SDFVoxel SampleSDF(int startIdx,int endIdx, float3 uv, int3 dimension)
        {
            var coord = uv * dimension;
            int index = (int)(coord.z * dimension.x * dimension.y + coord.y * dimension.x + coord.x);
            index = clamp(startIdx + index, startIdx, endIdx - 1);
            return Voxels[index];
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