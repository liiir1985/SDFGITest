using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Runtime.InteropServices;

namespace SDFGenerator
{
    class Clustering
    {

        List<SDFVolume> objs = new List<SDFVolume>();
        public List<SDFVolume> Volumes => objs;

        public Cluster[] KMeanClustering(int k)
        {
            Cluster[] arr = new Cluster[k];
            Vector3 min, max;

            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

            for(int i=0; i < objs.Count; i++)
            {
                var pos = objs[i].Position;
                if (pos.x < min.x)
                    min.x = pos.x;
                if (pos.y < min.y)
                    min.y = pos.y;
                if (pos.z < min.z)
                    min.z = pos.z;
                if (pos.x > max.x)
                    max.x = pos.x;
                if (pos.y > max.y)
                    max.y = pos.y;
                if (pos.z > max.z)
                    max.z = pos.z;
            }

            for(int i = 0; i < k; i++)
            {
                arr[i] = new Cluster();
                arr[i].Mean = new Vector3(Random.Range(min.x, max.x), Random.Range(min.y, max.y), Random.Range(min.z, max.z));
            }

            Dictionary<SDFVolume, int> mapping = new Dictionary<SDFVolume, int>();
            bool changed = false;
            do
            {
                changed = false;
                for(int i = 0; i < k; i++)
                {
                    arr[i].Transforms.Clear();
                }
                foreach(var i in objs)
                {
                    float d = float.MaxValue;
                    int nearest = -1;
                    for(int j = 0; j < k; j++)
                    {
                        var newD = (arr[j].Mean - i.Position).magnitude;
                        if(newD < d)
                        {
                            d = newD;
                            nearest = j;
                        }
                    }

                    if (!changed)
                    {
                        if(mapping.TryGetValue(i, out var oldMapping))
                        {
                            if (oldMapping != nearest)
                                changed = true;
                        }
                        else
                        {
                            changed = true;
                        }
                    }

                    mapping[i] = nearest;
                    arr[nearest].Transforms.Add(i);
                }

                for(int i = 0; i < k; i++)
                {
                    arr[i].UpdateMean();
                }
            }
            while (changed);
            return arr;
        }
    }

    class Cluster
    {
        public Vector3 Mean { get; set; }

        public List<SDFVolume> Transforms { get; private set; } = new List<SDFVolume>();

        public float TotalDistance
        {
            get
            {
                float totalDistance = 0;
                for(int i = 0; i < Transforms.Count; i++)
                {
                    var t = Transforms[i];
                    totalDistance += (t.Position - Mean).magnitude;
                }
                return totalDistance;
            }
        }

        public float MaxDistance
        {
            get
            {
                float maxDistance = float.MinValue;
                for (int i = 0; i < Transforms.Count; i++)
                {
                    var d = (Transforms[i].Position - Mean).magnitude;
                    if (d > maxDistance)
                    {
                        maxDistance = d;
                    }
                }
                return maxDistance;
            }
        }

        public void UpdateMean()
        {
            Vector3 total = Vector3.zero;
            foreach(var i in Transforms)
            {
                total += i.Position;
            }

            Mean = total / Transforms.Count;
        }
    }
}