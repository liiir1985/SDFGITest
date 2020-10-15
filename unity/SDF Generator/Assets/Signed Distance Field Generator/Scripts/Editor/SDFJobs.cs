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
    public NativeArray<TriangleData> Vertices;
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
        float3 uv = (float3)(Dimension - pos) / 2f - 0.5f;
        float3 modelPos = uv * BoundSize;
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


    public void DisposeNativeArrays()
    {
        Vertices.Dispose();
        Voxels.Dispose();
        AlbedoMap.Dispose();
        SurfaceMap.Dispose();
    }
}
