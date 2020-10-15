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

[BurstCompile]
public struct SDFComputeJob : IJobParallelFor
{
    [ReadOnly]
    public NativeArray<TriangleData> Triangles;
    public NativeArray<SDFVoxel> Voxels;
    [ReadOnly]
    public NativeArray<float4> AlbedoMap;
    [ReadOnly]
    public NativeArray<float4> SurfaceMap;
    [ReadOnly]
    public NativeArray<float4> EmissionMap;

    public int Resolution;
    public int3 Dimension;
    public float3 BoundSize;
    public void Execute(int index)
    {
        int3 pos = To3D(index);
        float3 uv = ((float3)(Dimension - pos) / Dimension) / 2f - 0.5f;
        float3 modelPos = uv * BoundSize;

        var (t, d) = FindNearesTriangle(modelPos);

        float s = (IntersectionCount(modelPos, new float3(0, 1, 0)) % 2 == 0) ? 1 : -1;

        SDFVoxel voxel = new SDFVoxel();
        voxel.NormalSDF = new half4((half3)t.normal, (half)(s * d));
        Voxels[index] = voxel;
    }

    (TriangleData, float) FindNearesTriangle(float3 position)
    {
        int id = 0;
        float d = float.MaxValue;
        for (int t = 0; t < Triangles.Length; t++)
        {
            float curD = DistanceToTriangle(position, t);
            if (curD < d)
            {
                id = t;
                d = curD;
            }
        }
        return (Triangles[id], d);
    }
    float dot2(float3 v)
    {
        return math.dot(v, v);
    }
    float DistanceToTriangle(float3 position, int triangleId)
    {
        TriangleData triangle = Triangles[triangleId];
        float3 ba = triangle.b - triangle.a;
        float3 cb = triangle.c - triangle.b;
        float3 ac = triangle.a - triangle.c;
        float3 pa = position - triangle.a;
        float3 pb = position - triangle.b;
        float3 pc = position - triangle.c;

        float3 nor = triangle.normal;

        if (math.sign(math.dot(math.cross(ba, nor), pa)) + math.sign(math.dot(math.cross(cb, nor), pb)) + math.sign(math.dot(math.cross(ac, nor), pc)) < 2.0)
        {
            float x = dot2(ba * math.clamp(math.dot(ba, pa) / dot2(ba), 0.0f, 1.0f) - pa);
            float y = dot2(cb * math.clamp(math.dot(cb, pb) / dot2(cb), 0.0f, 1.0f) - pb);
            float z = dot2(ac * math.clamp(math.dot(ac, pc) / dot2(ac), 0.0f, 1.0f) - pc);
            return math.sqrt(math.min(math.min(x, y), z));
        }
        else
        {
            return math.sqrt(math.dot(nor, pa) * math.dot(nor, pa) / dot2(nor));
        }
    }

    int3 To3D(int id)
    {
        int xQ = id / Dimension.x;
        int x = id % Dimension.x;
        int yQ = xQ / Dimension.y;
        int y = xQ / Dimension.y;
        int z = yQ % Dimension.z;
        return new int3(x, y, z);
    }

    /* Returns whether a ray intersects a triangle. Developed by Möller–Trumbore. */
    int RayIntersectsTriangle(float3 o, float3 d, int triangleId)
    {
        const float EPSILON = 0.0000001f;

        float3 v0 = Triangles[triangleId].a;
        float3 v1 = Triangles[triangleId].b;
        float3 v2 = Triangles[triangleId].c;

        float3 e1, e2, h, s, q;
        float a, f, u, v, t;

        e1 = v1 - v0;
        e2 = v2 - v0;

        h = math.cross(d, e2);
        a = math.dot(e1, h);

        if (math.abs(a) < EPSILON)
        {
            return 0;  // ray is parallel to triangle
        }

        f = 1.0f / a;
        s = o - v0;
        u = f * math.dot(s, h);

        if (u < 0.0 || u > 1.0)
        {
            return 0;
        }

        q = math.cross(s, e1);
        v = f * math.dot(d, q);

        if (v < 0.0 || u + v > 1.0)
        {
            return 0;
        }

        t = f * math.dot(e2, q);

        return (t >= 0.0) ? 1 : 0;
    }

    int IntersectionCount(float3 position, float3 direction)
    {
        int count = 0;

        for (int t = 0; t < Triangles.Length; t++)
        {
            count += RayIntersectsTriangle(position, direction, t);
        }

        return count;
    }

    public void DisposeNativeArrays()
    {
        Triangles.Dispose();
        Voxels.Dispose();
        AlbedoMap.Dispose();
        SurfaceMap.Dispose();
        EmissionMap.Dispose();
    }
}
