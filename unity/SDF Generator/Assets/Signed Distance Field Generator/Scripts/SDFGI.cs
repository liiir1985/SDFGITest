﻿//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.Mathematics;
using Unity.Collections;
using System.Runtime.InteropServices;

namespace SDFGenerator
{
    public class SDFGI : MonoBehaviour
    {
        BVH bvh;
        NativeArray<SDFVoxel> voxels;
        NativeArray<SDFVolumeInfo> volumeInfos;
        NativeArray<BVHNodeInfo> bvhTree;

        [SerializeField]
        Texture2D depthTexture;
        [SerializeField]
        Texture2D normalTexture;
        private void Start()
        {
            var depth = depthTexture.GetPixelData<half4>(0);
            var normal = normalTexture.GetPixelData<Color32>(0);
            Color32 b = normal[258];
            Color c = new Color(((int)b.r - 127) / 127f, ((int)b.g - 127) / 127f, ((int)b.b - 127) / 127f, ((int)b.a - 127) / 127f);
            Color a = new Color(0.4475098f, 0, 0);
            var volumes = Object.FindObjectsOfType<SDFVolume>();
            bvh = new BVH(volumes);
            bvh.Build();
            int voxelCnt = 0;
            foreach(var i in bvh.Volumes)
            {
                var dimension = i.Dimension;
                voxelCnt += dimension.x * dimension.y * dimension.z;
            }
            var nodes = bvh.Nodes;
            Dictionary<SDFVolume, int> indexMapping = new Dictionary<SDFVolume, int>();
            voxels = new NativeArray<SDFVoxel>(voxelCnt, Allocator.Persistent);
            volumeInfos = new NativeArray<SDFVolumeInfo>(bvh.Volumes.Count, Allocator.Persistent);
            bvhTree = new NativeArray<BVHNodeInfo>(nodes.Count, Allocator.Persistent);
            int curIdx = 0;
            for(int i = 0; i < bvh.Volumes.Count; i++)
            {
                var volume = bvh.Volumes[i];
                indexMapping[volume] = i;
                SDFVolumeInfo info = new SDFVolumeInfo();
                info.AABB = volume.Bounds.ToAABB();
                info.SDFBounds = volume.SDFBounds;
                info.Dimension = volume.Dimension;
                info.StartIndex = curIdx;
                volumeInfos[i] = info;
                volume.ReadData(voxels, curIdx);
                curIdx += info.Dimension.x * info.Dimension.y * info.Dimension.z;
            }
        }

        private void OnDestroy()
        {
            voxels.Dispose();
            volumeInfos.Dispose();
            bvhTree.Dispose();
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