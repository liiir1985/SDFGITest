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
    public Vector3 a;
    public Vector3 b;
    public Vector3 c;
    public Vector2 uv;
    public Vector3 normal;
    public int subMeshIdx;
}