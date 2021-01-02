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
using static Unity.Mathematics.math;

namespace SDFGenerator
{
    public static class Common
	{
        public const float Inv_Pi = 1 / PI;
        public static float Square(float x)
        {
            return x * x;
        }

        public static float2 Square(float2 x)
        {
            return x * x;
        }

        public static float3 Square(float3 x)
        {
            return x * x;
        }

        public static float4 Square(float4 x)
        {
            return x * x;
        }

        public static float pow2(float x)
        {
            return x * x;
        }

        public static float2 pow2(float2 x)
        {
            return x * x;
        }

        public static float3 pow2(float3 x)
        {
            return x * x;
        }

        public static float4 pow2(float4 x)
        {
            return x * x;
        }

        public static float pow3(float x)
        {
            return x * x * x;
        }

        public static float2 pow3(float2 x)
        {
            return x * x * x;
        }

        public static float3 pow3(float3 x)
        {
            return x * x * x;
        }

        public static float4 pow3(float4 x)
        {
            return x * x * x;
        }

        public static float pow4(float x)
        {
            float xx = x * x;
            return xx * xx;
        }

        public static float2 pow4(float2 x)
        {
            float2 xx = x * x;
            return xx * xx;
        }

        public static float3 pow4(float3 x)
        {
            float3 xx = x * x;
            return xx * xx;
        }

        public static float4 pow4(float4 x)
        {
            float4 xx = x * x;
            return xx * xx;
        }

        public static float pow5(float x)
        {
            float xx = x * x;
            return xx * xx * x;
        }

        public static float2 pow5(float2 x)
        {
            float2 xx = x * x;
            return xx * xx * x;
        }

        public static float3 pow5(float3 x)
        {
            float3 xx = x * x;
            return xx * xx * x;
        }

        public static float4 pow5(float4 x)
        {
            float4 xx = x * x;
            return xx * xx * x;
        }

        public static float pow6(float x)
        {
            float xx = x * x;
            return xx * xx * xx;
        }

        public static float2 pow6(float2 x)
        {
            float2 xx = x * x;
            return xx * xx * xx;
        }

        public static float3 pow6(float3 x)
        {
            float3 xx = x * x;
            return xx * xx * xx;
        }

        public static float4 pow6(float4 x)
        {
            float4 xx = x * x;
            return xx * xx * xx;
        }
        public static float min3(float a, float b, float c)
        {
            return min(min(a, b), c);
        }

        public static float max3(float a, float b, float c)
        {
            return max(a, max(b, c));
        }

        public static float4 min3(float4 a, float4 b, float4 c)
        {
            return float4(
                min3(a.x, b.x, c.x),
                min3(a.y, b.y, c.y),
                min3(a.z, b.z, c.z),
                min3(a.w, b.w, c.w));
        }

        public static float4 max3(float4 a, float4 b, float4 c)
        {
            return float4(
                max3(a.x, b.x, c.x),
                max3(a.y, b.y, c.y),
                max3(a.z, b.z, c.z),
                max3(a.w, b.w, c.w));
        }

        public static float CharlieL(float x, float r)
        {
            r = saturate(r);
            r = 1 - (1 - r) * (1 - r);

            float a = lerp(25.3245f, 21.5473f, r);
            float b = lerp(3.32435f, 3.82987f, r);
            float c = lerp(0.16801f, 0.19823f, r);
            float d = lerp(-1.27393f, -1.97760f, r);
            float e = lerp(-4.85967f, -4.32054f, r);

            return a / (1 + b * pow(x, c)) + d * x + e;
        }

        public unsafe static ref T GetByRef<T>(this NativeArray<T> arr, int index) where T : struct
        {
            return ref UnsafeUtility.ArrayElementAsRef<T>(arr.GetUnsafeReadOnlyPtr(), index);
        }

        public unsafe static ref T GetByRef<T>(this NativeSlice<T> arr, int index) where T : struct
        {
            return ref UnsafeUtility.ArrayElementAsRef<T>(arr.GetUnsafeReadOnlyPtr(), index);
        }
        public static bool IntersectAABBRay(in AABB aabb, in float3 origin, in float3 dir)
        {
            return IntersectAABBRay(in aabb, in origin, in dir, out _, out _);
        }
        
        public static bool IntersectAABBRay(in AABB aabb, in float3 origin, in float3 dir, out float tmin, out float tmax)
        {
            float3 dir_inv = 1.0f / dir;
            float t1 = (aabb.Min[0] - origin[0]) * dir_inv[0];
            float t2 = (aabb.Max[0] - origin[0]) * dir_inv[0];

            tmin = min(t1, t2);
            tmax = max(t1, t2);

            for (int i = 1; i < 3; ++i)
            {
                t1 = (aabb.Min[i] - origin[i]) * dir_inv[i];
                t2 = (aabb.Max[i] - origin[i]) * dir_inv[i];

                tmin = max(tmin, min(t1, t2));
                tmax = min(tmax, max(t1, t2));
            }

            return tmax > max(tmin, 0.0);
        }
	}
}