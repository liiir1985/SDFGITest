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

        public BVHNode Root => root;
        public BVH(SDFVolume[] volumes)
        {
            this.volumes.AddRange(volumes);
        }

        public void Build()
        {
            root = new BVHNode();
            root.Volumes.AddRange(volumes);
            root.Build();
        }
    }

    public class BVHNode
    {
        Bounds bounds;
        List<SDFVolume> volumes = new List<SDFVolume>();
        public List<SDFVolume> Volumes => volumes;
        public Bounds AABB => bounds;

        public void Build()
        {
            foreach(var i in volumes)
            {
                bounds.Encapsulate(i.Bounds);
            }
        }
    }
}