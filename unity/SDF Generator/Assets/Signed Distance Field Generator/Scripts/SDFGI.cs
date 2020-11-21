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
    public class SDFGI : MonoBehaviour
    {
        BVH bvh;
        private void Start()
        {
            var volumes = Object.FindObjectsOfType<SDFVolume>();
            bvh = new BVH(volumes);
            bvh.Build();
        }
#if UNITY_EDITOR
        public int GizmoDepth { get; set; }
        int maxDepth = -1;
        public int MaxDepth => maxDepth;
        void CalcDepth(BVHNode node, ref int depth)
        {
            int leftDepth = depth + 1;
            int rightDepth = depth + 1;

            if (node.Left != null)
                CalcDepth(node.Left, ref leftDepth);
            else
                leftDepth = depth;

            if (node.Right != null)
                CalcDepth(node.Right, ref rightDepth);
            else
                rightDepth = depth;

            depth = Mathf.Max(leftDepth, rightDepth);
        }

        void DrawBVHNode(BVHNode node, int depth)
        {
            if(depth == GizmoDepth)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(node.AABB.center, node.AABB.size);
            }
            else if(depth < GizmoDepth)
            {
                if (node.Left != null)
                    DrawBVHNode(node.Left, depth + 1);
                if (node.Right != null)
                    DrawBVHNode(node.Right, depth + 1);
            }
        }
        private void OnDrawGizmosSelected()
        {
            if (bvh != null)
            {
                if (maxDepth < 0)
                {
                    int depth = 0;
                    CalcDepth(bvh.Root, ref depth);
                    maxDepth = depth;
                }

                DrawBVHNode(bvh.Root, 0);
            }
        }
#endif
    }
}