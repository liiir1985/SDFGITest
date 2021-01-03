//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.Jobs;
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
        NativeArray<float4> normalData;
        NativeArray<float4> albedoData;
        NativeArray<float4> metallicData;

        [SerializeField]
        Texture2D albedoTexture;
        [SerializeField]
        Texture2D metallicTexture;
        [SerializeField]
        Texture2D depthTexture;
        [SerializeField]
        Texture2D normalTexture;
        [SerializeField]
        Color equiatorColor;
        [SerializeField]
        Color skyColor;
        [SerializeField]
        Light sun;
        [SerializeField]
        UnityEngine.UI.RawImage rawImg;
        Texture2D giTexture;
        private void Start()
        {  
            var depth = depthTexture.GetPixelData<half4>(0);
            var albedo = albedoTexture.GetPixelData<Color32>(0);
            var normal = normalTexture.GetPixelData<Color32>(0);
            var metallic = metallicTexture.GetPixelData<Color32>(0);

            depthData = new NativeArray<float>(depth.Length, Allocator.Persistent);
            for(int i = 0; i < depth.Length; i++)
            {
                float x = Mathf.GammaToLinearSpace(depth[i].x);
                depthData[i] = x;
            }
            
            normalData = new NativeArray<float4>(normal.Length, Allocator.Persistent);
            for(int i = 0; i < normal.Length; i++)
            {
                Color32 b = normal[i];
                float4 c = new float4(((int)b.r - 127) / 127f, ((int)b.g - 127) / 127f, ((int)b.b - 127) / 127f, ((int)b.a / 255f));
                normalData[i] = (half4)c;
            }

            albedoData = new NativeArray<float4>(albedo.Length, Allocator.Persistent);
            for (int i = 0; i < albedo.Length; i++)
            {
                Color32 b = albedo[i];
                float4 c = new float4(b.r / 255f, b.g / 255f, b.b / 255f, b.a / 255f);
                albedoData[i] = c;
            }

            metallicData = new NativeArray<float4>(metallic.Length, Allocator.Persistent);
            for (int i = 0; i < metallic.Length; i++)
            {
                Color32 b = metallic[i];
                float4 c = new float4(b.r / 255f, b.g / 255f, b.b / 255f, b.a / 255f);
                metallicData[i] = c;
            }

            giTexture = new Texture2D(depthTexture.width / 2, depthTexture.height / 2, TextureFormat.RGBAHalf, -1, true);
            giTexture.name = "GI Texture";
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
                info.WorldToLocal = volume.WorldToLocal;
                info.WorldToLocalInv = math.inverse(volume.WorldToLocal);
                info.StartIndex = curIdx;
                info.EndIndex = curIdx + info.Dimension.x * info.Dimension.y * info.Dimension.z; 
                volumeInfos[i] = info;
                volume.ReadData(voxels, curIdx);
                curIdx = info.EndIndex;
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
        Matrix4x4 CalcInvVP(int RenderWidth, int RenderHeight, bool isOpenGL)
        {
            var camera = Camera.main;
            Matrix4x4 proj = camera.projectionMatrix;
            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(proj, false);

            // xy coordinates in range [-1; 1] go to pixel coordinates.
            Matrix4x4 toScreen = new Matrix4x4(
                new Vector4(0.5f * RenderWidth, 0.0f, 0.0f, 0.0f),
                new Vector4(0.0f, 0.5f * RenderHeight, 0.0f, 0.0f),
                new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                new Vector4(0.5f * RenderWidth, 0.5f * RenderHeight, 0.0f, 1.0f)
            );

            Matrix4x4 zScaleBias = Matrix4x4.identity;
            if (isOpenGL)
            {
                // We need to manunally adjust z in NDC space from [-1; 1] to [0; 1] (storage in depth texture).
                zScaleBias = new Matrix4x4(
                    new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, 1.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.5f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.5f, 1.0f)
                );
            }

            return Matrix4x4.Inverse(toScreen * zScaleBias * gpuProj * view);
        }
        public void PathTrace(int index)
        {
            var camera = Camera.main;
            Matrix4x4 viewMat = camera.worldToCameraMatrix;
            Matrix4x4 projMat = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            Matrix4x4 viewProjMat = (projMat * viewMat);
            SDFGIJob job = new SDFGIJob();
            job.Voxels = voxels;
            job.VolumeInfos = volumeInfos;
            job.BVHTree = bvhTree;
            job.AlbedoMap = albedoData;
            job.MetallicMap = metallicData;
            job.DepthMap = depthData;
            job.NormalMap = normalData;
            job.GIMap = giTexture.GetPixelData<half4>(0);
            job.GBufferDimension = new int2(normalTexture.width, normalTexture.height);
            job.ViewProjectionMatrixInv = viewProjMat.inverse;
            job.Dimension = new int2(giTexture.width, giTexture.height);
            job.EyePos = camera.transform.position;
            job.LightDir = -sun.transform.forward;
            job.LightColor = new float3(sun.color.r, sun.color.g, sun.color.b) * sun.intensity;
            job.EquiatorColor = new float3(equiatorColor.r, equiatorColor.g, equiatorColor.b);
            job.SkyColor = new float3(skyColor.r, skyColor.g, skyColor.b);

            job.PathTrace(index);
        }

        public void DoGI()
        {
            var camera = Camera.main;
            Matrix4x4 viewMat = camera.worldToCameraMatrix;
            Matrix4x4 projMat = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            Matrix4x4 viewProjMat = (projMat * viewMat);
            SDFGIJob job = new SDFGIJob();
            job.Voxels = voxels;
            job.VolumeInfos = volumeInfos;
            job.BVHTree = bvhTree;
            job.AlbedoMap = albedoData;
            job.MetallicMap = metallicData;
            job.DepthMap = depthData;
            job.NormalMap = normalData;
            job.GIMap = giTexture.GetPixelData<half4>(0);
            job.GBufferDimension = new int2(normalTexture.width, normalTexture.height);
            job.ViewProjectionMatrixInv = viewProjMat.inverse;
            job.Dimension = new int2(giTexture.width, giTexture.height);
            job.EyePos = camera.transform.position;
            job.LightDir = -sun.transform.forward;
            job.LightColor = new float3(sun.color.r, sun.color.g, sun.color.b) * sun.intensity * 1.5f;
            job.EquiatorColor = new float3(equiatorColor.r, equiatorColor.g, equiatorColor.b);
            job.SkyColor = new float3(skyColor.r, skyColor.g, skyColor.b);

            var handle = job.ScheduleBatch(job.GIMap.Length, 256);
            handle.Complete();
            giTexture.Apply(false, false);

            byte[] buffer = giTexture.EncodeToEXR();
            using(System.IO.FileStream fs = new System.IO.FileStream("Assets/GITexture.exr", System.IO.FileMode.Create))
            {
                fs.Write(buffer, 0, buffer.Length);
            }
            AssetDatabase.Refresh();

            rawImg.texture = giTexture;
        }

        private void OnDestroy()
        {
            voxels.Dispose();
            volumeInfos.Dispose();
            bvhTree.Dispose();
            depthData.Dispose();
            normalData.Dispose();
            albedoData.Dispose();
            metallicData.Dispose();
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