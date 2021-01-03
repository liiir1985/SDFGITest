//#define NO_BILINEAR

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
using static SDFGenerator.BSDF;
using static SDFGenerator.ShadingModel;
using Unity.Entities;

namespace SDFGenerator
{
    [BurstCompile(FloatMode = FloatMode.Fast)]
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
        public float3 EquiatorColor;
        public float3 SkyColor;
        public int FrameIDMod8;

        const float RussianRoulette = 0.5f;
        const int SPP = 512;

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
                    var albedo = tex2d(AlbedoMap, uv, GBufferDimension);
                    var metallic = tex2d(MetallicMap, uv, GBufferDimension);
                    var c = TraceColor(worldPos, normal.xyz, ref rayDir, Random, hash, randomSeed, index, ref randomIndex, albedo.xyz, 1f - normal.w, metallic.w, 1);
                    color += c;
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


                        var hash = Hammersley16((uint)(idx), (uint)GIMap.Length, Random);

                        var albedo = tex2d(AlbedoMap, uv, GBufferDimension);
                        var metallic = tex2d(MetallicMap, uv, GBufferDimension);
                        var c = TraceColor(worldPos, normal.xyz, ref rayDir, Random, hash, randomSeed, idx, ref randomIndex, albedo.xyz, 1f - normal.w, metallic.w, 1);
                        color += c;
                        //color += saturate(float4(L, 1f));
                    }
                }

                GIMap[idx] = (half4)(color / SPP);
            }
        }

        float4 TraceColor(in float3 worldPos, in float3 normal, ref float3 rayDir, in uint2 Random, in float2 hash, int3 randomSeed, int idx, ref int randomIndex, in float3 albedo, float roughness, float metallic, float russianRollete)
        {
            var NN = normalize(normal);
            var viewDir = -rayDir;
            var NoV = saturate(dot(NN, viewDir));

            float geometrySchlick = F_Schlick(IorToFresnel(1, 0.004f), 1f, NoV);
            float kD = 1 - geometrySchlick;
            bool reflected = RandFast(Random) < geometrySchlick;
            var H_GGX = ImportanceSampleGGX(hash, roughness);
            var H_Lambert = UniformSampleCone(hash, 0f);
            var H = reflected ? H_GGX : H_Lambert;
            float pdf = H_GGX.w * geometrySchlick + H_Lambert.w * kD;
            var N = TangentToWorld(H.xyz, NN);
            rayDir = reflect(rayDir, N.xyz);
            //rayDir = reflect(rayDir, normalize(normal.xyz));
            float3 L = default;
            if (dot(rayDir, NN) > 0)
                L = RayMarch(in worldPos, in rayDir, idx, GIMap.Length, randomSeed, ref randomIndex);
            var brdf = Default_Lit(albedo, N, rayDir, viewDir, roughness, metallic);
            return float4(clamp(L * brdf * saturate(dot(rayDir, N.xyz)) / (pdf * russianRollete + float.Epsilon), default, L), 1f);
        }

        float LambertPDF(in float3 dir, in float3 normal)
        {
            return saturate(dot(dir, normal)) * PI;
        }

        float3 RayMarch(in float3 pos, in float3 dir, int index, int numSamples, int3 randomSeed, ref int randomIdx)
        {
            SDFVoxel voxel;
            SDFVolumeInfo hitVolume;
            float3 hitPos;
            bool hit = RayCast(in pos, in dir, out voxel, out hitVolume, out hitPos);

            if (hit)
            {
                var normal = mul(hitVolume.WorldToLocalInv, float4(voxel.NormalSDF.xyz, 1)).xyz;
                randomIdx++;
                randomSeed.z += randomIdx;
                uint2 Random = Rand3DPCG16(randomSeed).xy;
                var hash = Hammersley16((uint)(index), (uint)numSamples, Random);

                //Direct
                float3 L_dir = default;
                if (!RayCast(in hitPos, in LightDir, out _, out _, out _))
                {
                    var LoN = dot(LightDir, normal);
                    L_dir = LightColor * ((float3)voxel.SurfaceAlbedoRough.xyz) * saturate(LoN);
                }
                L_dir += voxel.EmissionMetallic.xyz;
                float4 L_Indir = default;
                if (hash.x < RussianRoulette)
                {
                    var worldPos = hitPos;
                    var rayDir = -dir;

                    var albedo = (float3)voxel.SurfaceAlbedoRough.xyz;
                    var c = TraceColor(worldPos, normal, ref rayDir, Random, hash, randomSeed, index, ref randomIdx, albedo.xyz, voxel.SurfaceAlbedoRough.w, voxel.EmissionMetallic.w, RussianRoulette);

                    L_Indir += c;
                }

                return L_dir + L_Indir.xyz;
            }
            else
            {
                return math.lerp(EquiatorColor, SkyColor, saturate(dot(dir, up())));
            }
        }

        bool RayCast(in float3 pos, in float3 dir, out SDFVoxel voxel, out SDFVolumeInfo hitVolume, out float3 hitPos)
        {
            voxel = default;
            hitVolume = default;
            hitPos = default;
            float minDistance = float.MaxValue;
            bool hit = false;
            for (int i = 0; i < BVHTree.Length;)
            {
                ref var node = ref BVHTree.GetByRef(i);
                if (node.SDFVolume >= 0)
                {
                    if (IntersectAABBRay(in node.Bounds, in pos, in dir))
                    {
                        ref var volume = ref VolumeInfos.GetByRef(node.SDFVolume);
                        var localPos = mul(volume.WorldToLocal, float4(pos, 1)).xyz;
                        var localDir = normalize(mul(volume.WorldToLocal, float4(pos + dir, 1)).xyz - localPos);
                        if (IntersectAABBRay(in volume.SDFBounds, in localPos, in localDir, out float tmin, out float tmax))
                        {
                            localPos = localPos + localDir * max((tmin + 0.01f), 0);
                            SDFVoxel result = default;
                            if (RayCastSDF(in volume, in localPos, in localDir, out result, out var sdfPos))
                            {

                                sdfPos = mul(volume.WorldToLocalInv, float4(sdfPos, 1)).xyz;
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

        bool RayCastSDF(in SDFVolumeInfo sdf, in float3 pos, in float3 dir, out SDFVoxel voxel, out float3 hitPos)
        {
            var sdfPos = pos - sdf.SDFBounds.Center;
            var sizeBound = new AABB();
            sizeBound.Extents = sdf.SDFBounds.Extents;
            float curDis;
            bool hit = false;
            int cnt = 0;
            float3 sdfuv;
            NativeSlice<SDFVoxel> slice = Voxels.Slice(sdf.StartIndex, sdf.EndIndex - sdf.StartIndex);
            do
            {
                hitPos = sdfPos;
                sdfuv = sdfPos / sdf.SDFBounds.Size + 0.5f;
                var sdfVal = SampleSDFValue(slice,in sdfuv,in sdf.Dimension);
                curDis = sdfVal.w;
                var NoL = dot(sdfVal.xyz, dir);
                hit = curDis < 0.01f && NoL < 0; 
                sdfPos = sdfPos + dir * max(abs(curDis), 0.01f);
                cnt++;
            }
            while (!hit && sizeBound.Contains(hitPos));
            voxel = hit ? SampleSDF(sdf.StartIndex, sdf.EndIndex, in sdfuv, in sdf.Dimension) : default;
            hitPos += sdf.SDFBounds.Center;
            return hit;
        }
        float4 SampleSDFValue(NativeSlice<SDFVoxel> tex, in float3 uv, in int3 size)
        {
#if NO_BILINEAR
            var maxSize = size - 1;
            var uv_img = clamp(round(uv * size), Unity.Mathematics.int3.zero, maxSize);
            return tex[(int)(uv_img.z * size.y * size.x + uv_img.y * size.x + uv_img.x)].NormalSDF;
#else
            var maxSize = size - 1;
            //uv = math.frac(uv);

            var uv_img = uv * size;
            var uv0 = math.clamp(math.floor(uv_img), Unity.Mathematics.int3.zero, maxSize);
            var uv1 = math.clamp(uv0 + 1, Unity.Mathematics.int3.zero, maxSize);

            float4 cuv0;
            float4 cuv1;
            lerpSDF(ref tex.GetByRef((int)(uv0.z * size.y * size.x + uv0.y * size.x + uv0.x)), ref tex.GetByRef((int)(uv0.z * size.y * size.x + uv0.y * size.x + uv1.x)), uv_img.x - uv0.x, out cuv0);
            lerpSDF(ref tex.GetByRef((int)(uv0.z * size.y * size.x + uv1.y * size.x + uv0.x)), ref tex.GetByRef((int)(uv0.z * size.y * size.x + uv1.y * size.x + uv1.x)), uv_img.x - uv0.x, out cuv1);
            var cFinal = math.lerp(cuv0, cuv1, uv_img.y - uv0.y);
            lerpSDF(ref tex.GetByRef((int)(uv1.z * size.y * size.x + uv0.y * size.x + uv0.x)), ref tex.GetByRef((int)(uv1.z * size.y * size.x + uv0.y * size.x + uv1.x)), uv_img.x - uv0.x, out cuv0);
            lerpSDF(ref tex.GetByRef((int)(uv1.z * size.y * size.x + uv1.y * size.x + uv0.x)), ref tex.GetByRef((int)(uv1.z * size.y * size.x + uv1.y * size.x + uv1.x)), uv_img.x - uv0.x, out cuv1);
            var cFinal2 = math.lerp(cuv0, cuv1, uv_img.y - uv0.y);
            var cFinal3 = math.lerp(cFinal, cFinal2, uv_img.z - uv0.z);
            return cFinal3;
#endif
        }

        void lerpSDF(ref SDFVoxel a, ref SDFVoxel b, float t, out float4 result)
        {
            result = math.lerp(a.NormalSDF, b.NormalSDF, t);
        }

        unsafe SDFVoxel SampleSDF(int startIdx, int endIdx, in float3 uv, in int3 dimension)
        {
            SDFVoxel* ptr = (SDFVoxel*)Voxels.GetUnsafeReadOnlyPtr();
            return tex3d(ptr + startIdx, uv, dimension);
            /*var coord = round(uv * dimension);
            int index = (int)(coord.z * dimension.x * dimension.y + coord.y * dimension.x + coord.x);
            index = clamp(startIdx + index, startIdx, endIdx - 1);
            return Voxels[index];*/
        }

        float3 DepthToWorldPos(float2 uv_depth, float depth)
        {
            float4 H = new float4(uv_depth.x * 2.0f - 1.0f, (1 - uv_depth.y) * 2.0f - 1.0f, depth, 1.0f);
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

        unsafe SDFVoxel lerp(SDFVoxel* a, SDFVoxel* b, float t)
        {
            SDFVoxel res = default;
            res.NormalSDF = (half4)math.lerp(a->NormalSDF, b->NormalSDF, t);
            res.SurfaceAlbedoRough = (half4)math.lerp(a->SurfaceAlbedoRough, b->SurfaceAlbedoRough, t);
            res.EmissionMetallic = (half4)math.lerp(a->EmissionMetallic, b->EmissionMetallic, t);
            return res;
        }

        unsafe SDFVoxel tex3d(SDFVoxel* tex, float3 uv, int3 size)
        {
            var maxSize = size - 1;
            uv = math.frac(uv);

            var uv_img = uv * size;
            var uv0 = math.clamp(math.floor(uv_img), Unity.Mathematics.int3.zero, maxSize);
            var uv1 = math.clamp(uv0 + 1, Unity.Mathematics.int3.zero, maxSize);
            
            var cuv0 = lerp(&tex[(int)(uv0.z * size.y * size.x + uv0.y * size.x + uv0.x)], &tex[(int)(uv0.z * size.y * size.x + uv0.y * size.x + uv1.x)], uv_img.x - uv0.x);
            var cuv1 = lerp(&tex[(int)(uv0.z * size.y * size.x + uv1.y * size.x + uv0.x)], &tex[(int)(uv0.z * size.y * size.x + uv1.y * size.x + uv1.x)], uv_img.x - uv0.x);
            var cFinal = lerp(&cuv0, &cuv1, uv_img.y - uv0.y);
            cuv0 = lerp(&tex[(int)(uv1.z * size.y * size.x + uv0.y * size.x + uv0.x)], &tex[(int)(uv1.z * size.y * size.x + uv0.y * size.x + uv1.x)], uv_img.x - uv0.x);
            cuv1 = lerp(&tex[(int)(uv1.z * size.y * size.x + uv1.y * size.x + uv0.x)], &tex[(int)(uv1.z * size.y * size.x + uv1.y * size.x + uv1.x)], uv_img.x - uv0.x);
            var cFinal2 = lerp(&cuv0, &cuv1, uv_img.y - uv0.y);
            var cFinal3 = lerp(&cFinal, &cFinal2, uv_img.z - uv0.z);
            return cFinal3;
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