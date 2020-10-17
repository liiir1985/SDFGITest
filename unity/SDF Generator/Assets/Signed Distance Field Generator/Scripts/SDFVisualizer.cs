using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Unity.Mathematics;

namespace SDFGenerator
{  
    public class SDFVisualizer : MonoBehaviour
    {
        public TextAsset sdfAsset;
        public GameObject Cube;
        public int resolution = 4;
        // Start is called before the first frame update
        unsafe void Start()
        {
            System.IO.MemoryStream ms = new System.IO.MemoryStream(sdfAsset.bytes);
            System.IO.BinaryReader br = new System.IO.BinaryReader(ms);
            br.ReadInt32();
            int resolusion = br.ReadInt32();
            int3 dimension = new int3(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            Vector3 size = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            SDFVoxel[] voxels = new SDFVoxel[dimension.x * dimension.y * dimension.z];
            var vSize = sizeof(SDFVoxel);
            byte[] buffer = new byte[vSize];
            fixed (byte* ptr = buffer)
            {
                SDFVoxel* v = (SDFVoxel*)ptr;
                for (int i = 0; i < voxels.Length; i++)
                {
                    ms.Read(buffer, 0, vSize);
                    voxels[i] = *v;
                }
            }
            for (int z = 0; z <= dimension.z; z += resolution)
            {
                for (int y = 0; y <= dimension.y; y += resolution)
                {
                    for (int x = 0; x <= dimension.x; x += resolution)
                    {
                        x = Mathf.Min(x, dimension.x - 1);
                        y = Mathf.Min(y, dimension.y - 1);
                        z = Mathf.Min(z, dimension.z - 1);

                        int index = z * dimension.x * dimension.y + y * dimension.x + x;
                        var data = voxels[index];

                        Vector3 uv = new Vector3((float)x / dimension.x, (float)y / dimension.y, (float)z / dimension.z);
                        uv -= Vector3.one * 0.5f;

                        var pos = Vector3.Scale(uv, size);

                        GameObject go = Instantiate(Cube);
                        go.name = $"{x}_{y}_{z}";
                        var t = go.transform;
                        t.SetParent(transform);
                        t.localPosition = pos;
                        t.localScale = Vector3.one * (1f / resolusion);
                        var mr = go.GetComponent<MeshRenderer>();
                        Material mat = Instantiate(mr.sharedMaterial);

                        float dRatio = 2f / resolusion;
                        float3 normal = math.saturate(((float4)data.NormalSDF).xyz + 1f / 2);
                        float3 albedoRough = math.saturate(((float4)data.SurfaceAlbedoRough).xyz + 1f / 2);
                        mat.SetColor("_BaseColor", new Color(albedoRough.x, albedoRough.y, albedoRough.z, Mathf.Clamp01(1 - data.NormalSDF.w / dRatio)));
                        mr.material = mat;

                        int3 pos2 = To3D(index, dimension);
                        float3 uv2 = ((float3)pos2 / dimension) - 0.5f;
                        float3 modelPos = uv2 * size;

                        if(((Vector3)modelPos - pos).magnitude > 0.001f)
                        {
                            Debug.Log("?????");
                        }
                    }
                }
            }
        }
        int3 To3D(int id, int3 Dimension)
        {
            int xQ = id / Dimension.x;
            int x = id % Dimension.x;
            int yQ = xQ / Dimension.y;
            int y = xQ % Dimension.y;
            int z = yQ % Dimension.z;
            return new int3(x, y, z);
        }
        // Update is called once per frame
        void Update()
        {

        }
    }

}