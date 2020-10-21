using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Unity.Mathematics;
using UnityEditor;

namespace SDFGenerator
{  
    public class SDFVoxelObject : MonoBehaviour
    {
        public bool enableDebug;
        public MeshRenderer meshRenderer;
        bool calculated;
        SDFVoxel voxel;
        TriangleData triangel;
        public Vector3Int Coordinate;
        public int Resolution;
        Transform parentTransform;

        private void Start()
        {
            parentTransform = transform.parent;
        }
        void Update()
        {
            if (enableDebug)
            {
                if (!calculated)
                {
                    Mesh mesh = meshRenderer.GetComponent<MeshFilter>().sharedMesh;
                    //Generator gen = new Generator(meshRenderer, mesh);
                    //(voxel, triangel) = gen.CalculateVoxel(Resolution, transform.localPosition);
                    calculated = true;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if(enableDebug && calculated)
            {
                var l2w = parentTransform.localToWorldMatrix;
                Gizmos.color = Color.yellow;
                var a = LocalToWorld(triangel.a, l2w);
                var b = LocalToWorld(triangel.b, l2w);
                var c = LocalToWorld(triangel.c, l2w);
                var normal = (l2w * (Vector3)triangel.normal).normalized;
                Gizmos.DrawLine(b, a);
                Gizmos.DrawLine(c, b);
                Gizmos.DrawLine(a, c);
                Gizmos.DrawRay((a + b + c) / 3f, normal);
            }
        }

        Vector3 LocalToWorld(float3 pos, Matrix4x4 l2w)
        {
            float4 pos4 = new float4();
            pos4.xyz = pos;
            pos4.w = 1;

            Vector4 posv = pos4;

            return l2w * posv;
        }
    }

}