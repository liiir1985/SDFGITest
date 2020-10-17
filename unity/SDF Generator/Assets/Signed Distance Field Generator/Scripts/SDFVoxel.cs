//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using System.Runtime.InteropServices;
public struct SDFVoxel
{
    public half4 NormalSDF;
    public half4 SurfaceAlbedoRough;
    public half4 EmissionMetallic;
}

[StructLayout(LayoutKind.Sequential)]
public struct TriangleData
{
    public float3 a;
    public float3 b;
    public float3 c;
    public float2 uv;
    public float3 normal;
    public int3 vertIdx;
    public int subMeshIdx;
}

public struct SDFTextureInfo
{
    public int2 Size;
    public int DataOffset;
}