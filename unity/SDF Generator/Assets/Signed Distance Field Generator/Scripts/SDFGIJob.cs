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
        public float3 LightDir;
        public float3 LightColor;
        public int FrameIDMod8;

        const float RussianRoulette = 0.25f;
        public void Execute(int startIndex, int count)
        {
            int endIdx = startIndex + count;
            for (int idx = startIndex; idx < endIdx; idx++)
            {
                int3 randomSeed = int3(idx % Dimension.x, idx / Dimension.x, FrameIDMod8);
                int randomIndex = 0;
                uint2 Random = Rand3DPCG16(randomSeed).xy;
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
                    GIMap[idx] = (half4)float4(RayMarch(worldPos, reflect(rayDir, N.xyz), idx, GIMap.Length, randomSeed, ref randomIndex), 1f);
                }
            }
        }

        float3 RayMarch(float3 pos, float3 dir, int index, int numSamples,int3 randomSeed, ref int randomIdx)
        {
            SDFVoxel voxel = default;
            SDFVolumeInfo hitVolume = default;
            float minDistance = float.MaxValue;
            float3 hitPos = default;
            bool hit = false;
            for (int i = 0; i < BVHTree.Length; )
            {
                var node = BVHTree[i];
                if (node.SDFVolume >= 0)
                {
                    if (IntersectAABBRay(node.Bounds, pos, dir))
                    {
                        var volume = VolumeInfos[node.SDFVolume];
                        var localPos = mul(volume.WorldToLocal, float4(pos, 1)).xyz;
                        var localDir = normalize(mul(volume.WorldToLocal, float4(dir, 1)).xyz);
                        if(IntersectAABBRay(volume.SDFBounds, localPos, localDir, out float tmin, out float tmax))
                        {
                            localPos = localPos + localDir * (tmin + 0.01f);
                            SDFVoxel result = default;
                            if(RayCastSDF(ref volume, localPos, localDir, ref result, out var sdfPos))
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

            if (hit)
            {
                var normal = mul(float4(voxel.NormalSDF.xyz, 1), hitVolume.WorldToLocal).xyz;
                randomIdx++;
                randomSeed.z = randomIdx;
                uint2 Random = Rand3DPCG16(randomSeed).xy;
                var hash = Hammersley16((uint)(index), (uint)numSamples, Random);

                //Direct
                var H = UniformSampleCone(hash, PI / 8);
                var rayDir = TangentToWorld(H.xyz, LightDir);
                float3 L_dir = LightColor * ((float3)voxel.SurfaceAlbedoRough.xyz / PI) * saturate(dot(rayDir, normal)) / (H.w + float.Epsilon);

                if (hash.x < RussianRoulette)
                {
                    var worldPos = hitPos;
                    rayDir = -dir;
                    H = ImportanceSampleGGX(hash, 1 - voxel.SurfaceAlbedoRough.w);
                    float pdf = H.w;
                    var N = TangentToWorld(H.xyz, normalize(normal.xyz));
                }

                return L_dir;
            }
            else
                return default(float3);
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
                hit = curDis < 0.125f;
                sdfPos = sdfPos + dir * curDis;
            }
            while (!hit && sizeBound.Contains(hitPos));
            return hit;
        }

        SDFVoxel SampleSDF(int startIdx,int endIdx, float3 uv, int3 dimension)
        {
            var coord = uv * dimension;
            int index = (int)(uv.z * dimension.x * dimension.y + uv.y * dimension.x + uv.x);
            index = clamp(startIdx + index, startIdx, endIdx);
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