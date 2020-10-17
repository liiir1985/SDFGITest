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
public struct SDFComputeJob : IJobParallelForBatch
{
    [ReadOnly]
    public NativeArray<TriangleData> Triangles;
    [ReadOnly]
    public NativeArray<float2> UVs;
    public NativeArray<SDFVoxel> Voxels;
    [ReadOnly]
    public NativeArray<float4> AlbedoMap;
    [ReadOnly]
    public NativeArray<SDFTextureInfo> AlbedoInfo;
    [ReadOnly]
    public NativeArray<float4> SurfaceMap;
    [ReadOnly]
    public NativeArray<SDFTextureInfo> SurfaceInfo;
    [ReadOnly]
    public NativeArray<float4> EmissionMap;
    [ReadOnly]
    public NativeArray<SDFTextureInfo> EmissionInfo;

    public int Resolution;
    public int3 Dimension;
    public float3 BoundSize;
    public void Execute(int startIndex, int count)
    {
        for (int index = startIndex; index < startIndex + count; index++)
        {
            int3 pos = To3D(index);
            float3 uv = ((float3)pos / Dimension) - 0.5f;
            float3 modelPos = uv * BoundSize;

            var d = FindNearesTriangle(modelPos, out var t);
            var barycentric = ProjectPointOnTriangle(modelPos, ref t);
            var uv2 = UVs[t.vertIdx.x] * barycentric.x + UVs[t.vertIdx.y] * barycentric.y + UVs[t.vertIdx.z] * barycentric.z;
            var color = tex2d(AlbedoMap, uv2, AlbedoInfo[t.subMeshIdx]);
            var surface = tex2d(SurfaceMap, uv2, SurfaceInfo[t.subMeshIdx]);
            var emission = tex2d(EmissionMap, uv2, EmissionInfo[t.subMeshIdx]);

            var albedoRough = new float4(color.x, color.y, color.z, surface.x);
            var emissionMetallic = new float4(emission.x, emission.y, emission.z, surface.y);

            float s = (IntersectionCount(modelPos, new float3(0, 1, 0)) % 2 == 0) ? 1 : -1;

            SDFVoxel voxel = new SDFVoxel();
            voxel.NormalSDF = new half4((half3)t.normal, (half)(s * d));
            voxel.SurfaceAlbedoRough = (half4)albedoRough;
            voxel.EmissionMetallic = (half4)emissionMetallic;
            Voxels[index] = voxel;
        }
    }

    float4 tex2d(NativeArray<float4> tex, float2 uv, SDFTextureInfo tinfo)
    {
        var size = tinfo.Size;
        var maxSize = size - 1;
        var offset = tinfo.DataOffset;
        uv = math.frac(uv);

        var uv_img = uv * size;
        var uv0 = math.clamp(math.floor(uv_img),int2.zero, maxSize);
        var uv1 = math.clamp(uv0 + 1, int2.zero, maxSize);
        

        var cuv0 = math.lerp(tex[offset + (int)(uv0.y * size.x + uv0.x)], tex[offset + (int)(uv0.y * size.x + uv1.x)], uv_img.x - uv0.x);
        var cuv1 = math.lerp(tex[offset + (int)(uv1.y * size.x + uv0.x)], tex[offset + (int)(uv1.y * size.x + uv1.x)], uv_img.x - uv0.x);
        var cFinal = math.lerp(cuv0, cuv1, uv_img.y - uv0.y);
        return cFinal;
    }

    float3 ProjectPointOnTriangle(float3 pos, ref TriangleData tri)
    {
        var u = tri.b - tri.a;
        var v = tri.c - tri.a;
        var n = math.cross(u, v);
        var w = pos - tri.a;
        float3 res = new float3();
        res.z = math.dot(math.cross(u, w), n) / dot2(n);
        res.y = math.dot(math.cross(w, v), n) / dot2(n);
        res.x = 1 - res.y - res.z;

        return res;
    }

    float FindNearesTriangle(float3 position, out TriangleData tri)
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
        tri = Triangles[id];
        return d;
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
        int y = xQ % Dimension.y;
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
        UVs.Dispose();
    }
}
