//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
public struct SDFVoxel
{
    public half4 NormalSDF;
    public half4 SurfaceAlbedoRough;
    public half4 EmissionMetallic;
}

public struct TriangleData
{
    public float3 a;
    public float3 b;
    public float3 c;
    public float2 uv;
    public float3 normal;
    public int subMeshIdx;
}