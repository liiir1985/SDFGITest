//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace SDFGenerator
{
    public struct SDFVolumeInfo
    {
        public AABB AABB;
        public AABB SDFBounds;
        public int3 Dimension;
        public float4x4 WorldToLocal;
        public int StartIndex;
        public int EndIndex;
    }
    public class SDFVolume : MonoBehaviour
    {
        [SerializeField]
        TextAsset sdfData;

        Vector3 size;
        Vector3 center;
        int3 dimension;
        AABB sdfBounds;
        Bounds aabb;
        int resolusion;
        System.IO.MemoryStream ms;

        public Vector3 Position => aabb.center;

        public Vector3 Size => aabb.size;

        public Bounds Bounds => aabb;

        public AABB SDFBounds => sdfBounds;
        public int3 Dimension => dimension;

        public int Resolusion => resolusion;

        public float4x4 WorldToLocal => transform.worldToLocalMatrix;

        private void Awake()
        {
            ReadMeta();

        }
        void ReadMeta()
        {
            ms = new System.IO.MemoryStream(sdfData.bytes);
            System.IO.BinaryReader br = new System.IO.BinaryReader(ms);
            br.ReadInt32();
            resolusion = br.ReadInt32();
            dimension = new int3(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            center = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            size = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

            Bounds bounds = new Bounds(center, size);
            sdfBounds = bounds.ToAABB();
            var aabb = AABB.Transform(transform.localToWorldMatrix, sdfBounds);
            this.aabb = new Bounds(aabb.Center, aabb.Size);
        }

        public unsafe void ReadData(NativeArray<SDFVoxel> arr, int index)
        {
            ms.Position = 44;
            int cnt = dimension.x * dimension.y * dimension.z;

            var vSize = sizeof(SDFVoxel);
            byte[] buffer = new byte[vSize];
            fixed (byte* ptr = buffer)
            {
                SDFVoxel* v = (SDFVoxel*)ptr;

                for (int i = 0; i < cnt; i++)
                {
                    ms.Read(buffer, 0, vSize);
                    arr[index + i] = *v;
                }
            }
        }
#if UNITY_EDITOR
        TextAsset cachedSdfData;

        private void OnDrawGizmosSelected()
        {
            if (cachedSdfData != sdfData && sdfData)
            {
                ReadMeta();
                cachedSdfData = sdfData;
            }
            Gizmos.matrix = transform.localToWorldMatrix;
            var c = Color.cyan;
            c.a = 0.5f;
            Gizmos.color = c;
            Gizmos.DrawWireCube(center, size);

            Bounds bounds = new Bounds(center, size);
            var aabb = AABB.Transform(transform.localToWorldMatrix, bounds.ToAABB());
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(aabb.Center, aabb.Size);

        }
#endif
    }
}