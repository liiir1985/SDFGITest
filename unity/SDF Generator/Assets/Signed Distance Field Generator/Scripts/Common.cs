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
		public static bool IntersectAABBRay(AABB aabb, float3 origin, float3 dir)
        {
            float3 dir_inv = 1.0f / dir;
            float t1 = (aabb.Min[0] - origin[0]) * dir_inv[0];
            float t2 = (aabb.Max[0] - origin[0]) * dir_inv[0];

            float tmin = min(t1, t2);
            float tmax = max(t1, t2);

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