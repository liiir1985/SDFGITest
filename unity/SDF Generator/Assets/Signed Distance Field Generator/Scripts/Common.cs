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