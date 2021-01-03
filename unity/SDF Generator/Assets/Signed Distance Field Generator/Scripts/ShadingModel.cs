//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using Unity.Jobs;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using static Unity.Mathematics.math;
using static SDFGenerator.BSDF;
using Unity.Entities;

namespace SDFGenerator
{
    public static class ShadingModel
    {
        public static float3 Default_Lit(in float3 Albedo, in float3 normal, in float3 lightDir, in float3 viewDir, float roughness, float metallic)
        {
            float3 diffuse = Diffuse_Lambert(Albedo);
            var halfVec = (lightDir + viewDir) / 2f;
            float NoH = saturate(dot(normal, halfVec));
            float NoV = saturate(dot(normal, viewDir));
            float NoL = saturate(dot(normal, lightDir));
            float d_ggx = D_GGX(NoH, roughness);
            float3 F0 = float3(0.04f);
            F0 = lerp(F0, Albedo, metallic);
            float3 f_schlick = F_Schlick(F0, float3(1), NoV);
            float g = Vis_Smith(NoL, NoV, roughness);
            float3 kD = (float3(1) - f_schlick) * (1 - metallic);
            return kD * diffuse + (d_ggx * f_schlick * g) / (4 * NoL * NoV);
        }
    }
}