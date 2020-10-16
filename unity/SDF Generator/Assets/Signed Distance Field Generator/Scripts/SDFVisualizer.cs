using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SDFGenerator
{  
    public class SDFVisualizer : MonoBehaviour
    {
        public TextAsset sdfAsset;
        public GameObject Cube;
        // Start is called before the first frame update
        unsafe void Start()
        {
            System.IO.MemoryStream ms = new System.IO.MemoryStream(sdfAsset.bytes);
            System.IO.BinaryReader br = new System.IO.BinaryReader(ms);
            br.ReadInt32();
            int resolusion = br.ReadInt32();
            Vector3Int dimension = new Vector3Int(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
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
            for (int z = 0; z <= dimension.z; z += 8)
            {
                for (int y = 0; y <= dimension.y; y += 8)
                {
                    for (int x = 0; x <= dimension.x; x += 8)
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
                        mat.SetColor("_BaseColor", new Color(0, 0, 0, Mathf.Clamp01(1 - data.NormalSDF.w / dRatio)));
                        mr.material = mat;
                    }
                }
            }
        }

        // Update is called once per frame
        void Update()
        {

        }
    }

}