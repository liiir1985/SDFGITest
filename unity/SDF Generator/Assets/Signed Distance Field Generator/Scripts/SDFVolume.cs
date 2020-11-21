//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace SDFGenerator
{
    public class SDFVolume : MonoBehaviour
    {
        [SerializeField]
        TextAsset sdfData;

        Vector3 size;
        Vector3 center;

        Bounds aabb;

        public Vector3 Position => aabb.center;

        public Vector3 Size => aabb.size;

        public Bounds Bounds => aabb;

        private void Start()
        {
            ReadData();

        }
        void ReadData()
        {
            System.IO.MemoryStream ms = new System.IO.MemoryStream(sdfData.bytes);
            System.IO.BinaryReader br = new System.IO.BinaryReader(ms);
            br.ReadInt32();
            int resolusion = br.ReadInt32();
            int3 dimension = new int3(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            center = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            size = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

            Bounds bounds = new Bounds(center, size);
            var aabb = AABB.Transform(transform.localToWorldMatrix, bounds.ToAABB());
            this.aabb = new Bounds(aabb.Center, aabb.Size);
        }
#if UNITY_EDITOR
        TextAsset cachedSdfData;

        private void OnDrawGizmosSelected()
        {
            if (cachedSdfData != sdfData && sdfData)
            {
                ReadData();
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