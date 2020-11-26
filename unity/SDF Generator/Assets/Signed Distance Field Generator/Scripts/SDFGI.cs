//
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
        NativeArray<float> depthData;
        NativeArray<half4> normalData;

        [SerializeField]
        Texture2D depthTexture;
        [SerializeField]
        Texture2D normalTexture;
        Texture2D giTexture;
        private void Start()
        {
            var depth = depthTexture.GetPixelData<half4>(0);
            var normal = normalTexture.GetPixelData<Color32>(0);
            
            depthData = new NativeArray<float>(depth.Length, Allocator.Persistent);
            for(int i = 0; i < depth.Length; i++)
            {
                float x = Mathf.GammaToLinearSpace(depth[i].x);
                depthData[i] = x;
            }
            
            normalData = new NativeArray<half4>(normal.Length, Allocator.Persistent);
            for(int i = 0; i < normal.Length; i++)
            {
                Color32 b = normal[i];
                float4 c = new float4(((int)b.r - 127) / 127f, ((int)b.g - 127) / 127f, ((int)b.b - 127) / 127f, ((int)b.a - 127) / 127f);
                normalData[i] = (half4)c;
            }

            giTexture = new Texture2D(depthTexture.width / 2, depthTexture.height / 2, 0, true);
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

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                BVHNodeInfo info = new BVHNodeInfo();
                info.Bounds = node.AABB.ToAABB();
                if (node.Left == null && node.Right == null)
                    info.SDFVolume = indexMapping[node.Volumes[0]];
                else
                    info.SDFVolume = -1;
                info.FalseLink = node.FalseLink;
                bvhTree[i] = info;
            }
        }

        public void DoGI()
        {
            SDFGIJob job = new SDFGIJob();
            job.Voxels = voxels;
            job.VolumeInfos = volumeInfos;
            job.BVHTree = bvhTree;
            job.DepthMap = depthData;
        }

        private void OnDestroy()
        {
            voxels.Dispose();
            volumeInfos.Dispose();
            bvhTree.Dispose();

            Destroy(giTexture);
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