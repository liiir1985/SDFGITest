//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace SDFGenerator
{
    public class BVH
    {
        BVHNode root;
        List<SDFVolume> volumes = new List<SDFVolume>();
        List<BVHNode> nodes = new List<BVHNode>();
        public BVHNode Root => root;
        public List<BVHNode> Nodes => nodes;

        public List<SDFVolume> Volumes => volumes;
        public BVH(SDFVolume[] volumes)
        {
            this.volumes.AddRange(volumes);
        }

        public void Build()
        {
            root = new BVHNode();
            root.Volumes.AddRange(volumes);
            root.Build();

            root.Traverse(nodes);
        }
    }

    public struct BVHNodeInfo
    {
        public AABB Bounds;
        public int SDFVolume;
        public int FailLink;
    }
    public class BVHNode
    {
        Bounds bounds;
        List<SDFVolume> volumes = new List<SDFVolume>();
        BVHNode left, right;
        public List<SDFVolume> Volumes => volumes;
        public Bounds AABB => bounds;

        public BVHNode Left => left;
        public BVHNode Right => right;

        public int Index { get; set; }
        public int FalseLink { get; set; } = int.MaxValue;

        public void Build()
        {
            foreach(var i in volumes)
            {
                bounds.Encapsulate(i.Bounds);
            }
            if (volumes.Count <= 1)
                return;
            Clustering c = new Clustering();
            c.Volumes.AddRange(volumes);

            var clusters = c.KMeanClustering(2);

            if(clusters[0].Transforms.Count > 0)
            {
                left = new BVHNode();
                left.volumes.AddRange(clusters[0].Transforms);
                left.Build();
            }
            if (clusters[1].Transforms.Count > 0)
            {
                right = new BVHNode();
                right.volumes.AddRange(clusters[1].Transforms);
                right.Build();
            }
        }

        public void Traverse(List<BVHNode> nodes)
        {
            Index = nodes.Count;
            nodes.Add(this);
            if (left != null)
                left.Traverse(nodes);
            if (right != null)
                right.Traverse(nodes);

            if(left != null)
            {
                if (right != null)
                    left.FalseLink = right.Index;
                else
                    left.FalseLink = nodes.Count;
            }
            if(right != null)
            {
                right.FalseLink = nodes.Count;
            }
        }
    }
}