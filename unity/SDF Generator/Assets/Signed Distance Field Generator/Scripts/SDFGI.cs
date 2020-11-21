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
        SDFVolume[] volumes;
        private void Start()
        {
            volumes = Object.FindObjectsOfType<SDFVolume>();

            Clustering c = new Clustering();
            c.Volumes.AddRange(volumes);

            var clusters = c.KMeanClustering(2);
        }
    }
}