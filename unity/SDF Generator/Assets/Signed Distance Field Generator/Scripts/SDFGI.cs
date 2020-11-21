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
    }
}