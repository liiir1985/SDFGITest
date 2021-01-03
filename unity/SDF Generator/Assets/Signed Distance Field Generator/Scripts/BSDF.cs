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
using static SDFGenerator.Common;

namespace SDFGenerator
{
    public static class BSDF
    {
		public static float IorToFresnel(float transmittedIor, float incidentIor)
		{
			return pow2(transmittedIor - incidentIor) / pow2(transmittedIor + incidentIor);
		}

		public static float3 IorToFresnel(float3 transmittedIor, float3 incidentIor)
		{
			return pow2(transmittedIor - incidentIor) / pow2(transmittedIor + incidentIor);
		}
		public static float F_Schlick(float F0, float F90, float HoV)
        {
            return F0 + (F90 - F0) * pow5(1 - HoV);
        }

        public static float3 F_Schlick(float3 F0, float3 F90, float HoV)
        {
            float Fc = pow5(1 - HoV);
            return saturate(50 * F0.y) * Fc + (1 - Fc) * F0;
        }

        public static float3 F_Fresnel(float3 F0, float HoV)
        {
            float3 F0Sqrt = sqrt(clamp(float3(0, 0, 0), float3(0.99f, 0.99f, 0.99f), F0));
            float3 n = (1 + F0Sqrt) / (1 - F0Sqrt);
            float3 g = sqrt(n * n + HoV * HoV - 1);
            return 0.5f * Square((g - HoV) / (g + HoV)) * (1 + Square(((g + HoV) * HoV - 1) / ((g - HoV) * HoV + 1)));
        }

        public static float F_Hair(float CosTheta)
        {
            const float n = 1.55f;
            float F0 = Square((1 - n) / (1 + n));
            return F0 + (1 - F0) * pow5(1 - CosTheta);
        }

		/////////////////////////////////////////////////////////////////Diffuse
		public static float3 Diffuse_Lambert(float3 DiffuseColor)
		{
			return DiffuseColor * Inv_Pi;
		}

		public static float3 Diffuse_Fabric(float3 DiffuseColor, float3 Roughness)
		{
			return Diffuse_Lambert(DiffuseColor) * lerp(1, 0.5f, Roughness);
		}

		public static float Diffuse_Burley_NoPi(float LoH, float NoL, float NoV, float Roughness)
		{
			float F90 = 0.5f + 2 * pow2(LoH) * Roughness;
			float ViewScatter = F_Schlick(1, F90, NoL);
			float LightScatter = F_Schlick(1, F90, NoV);
			return ViewScatter * LightScatter;
		}

		public static float3 Diffuse_Burley(float LoH, float NoL, float NoV, float Roughness, float3 DiffuseColor)
		{
			return Diffuse_Burley_NoPi(LoH, NoL, NoV, Roughness) * (DiffuseColor * Inv_Pi);
		}

		public static float Diffuse_RenormalizeBurley_NoPi(float LoH, float NoL, float NoV, float Roughness)
		{
			float EnergyBias = lerp(0, 0.5f, Roughness);
			float EnergyFactor = lerp(1, 1 / 0.662f, Roughness);

			float F90 = EnergyBias + 2 * pow2(LoH) * Roughness;
			float LightScatter = F_Schlick(1, F90, NoL);
			float ViewScatter = F_Schlick(1, F90, NoV);
			return LightScatter * ViewScatter * EnergyFactor;
		}

		public static float3 Diffuse_RenormalizeBurley(float LoH, float NoL, float NoV, float Roughness, float3 DiffuseColor)
		{
			return Diffuse_RenormalizeBurley_NoPi(LoH, NoL, NoV, Roughness) * (DiffuseColor * Inv_Pi);
		}

		public static float Diffuse_OrenNayar_NoPi(float VoH, float NoL, float NoV, float Roughness)
		{
			float Roughness4 = pow4(Roughness);
			float VoL = 2 * VoH * VoH - 1;
			float Cosri = VoL - NoV * NoL;
			float C1 = 1 - 0.5f * Roughness4 / (Roughness4 + 0.33f);
			float C2 = 0.45f * Roughness4 / (Roughness4 + 0.09f) * Cosri * (Cosri >= 0 ? rcp(max(NoL, NoV)) : 1);
			return (C1 + C2) * (1 + Roughness * 0.5f);
		}

		public static float3 Diffuse_OrenNayar(float VoH, float NoL, float NoV, float Roughness, float3 DiffuseColor)
		{
			return Diffuse_OrenNayar_NoPi(VoH, NoL, NoV, Roughness) * (DiffuseColor * Inv_Pi);
		}



		/////////////////////////////////////////////////////////////////Specular D
		public static float D_GGX_NoPi(float NoH, float Roughness)
		{
			float Roughness2 = pow2(Roughness);
			float D = (NoH * Roughness - NoH) * NoH + 1;
			return Roughness2 / pow2(D);
		}

		public static float D_GGX(float NoH, float Roughness)
		{
			return Inv_Pi * D_GGX_NoPi(NoH, Roughness);
		}

		public static float D_Beckmann_NoPi(float NoH, float Roughness)
		{
			float Roughness2 = pow2(Roughness);
			float NoH2 = pow2(NoH);
			return exp((NoH2 - 1) / (Roughness2 * NoH2)) / (Roughness2 * NoH2);
		}

		public static float D_Beckmann(float NoH, float Roughness)
		{
			return Inv_Pi * D_Beckmann_NoPi(NoH, Roughness);
		}

		public static float D_AnisotropyGGX_NoPi(float ToH, float BoH, float NoH, float RoughnessT, float RoughnessB)
		{
			float D = ToH * ToH / pow2(RoughnessT) + BoH * BoH / pow2(RoughnessB) + pow2(NoH);
			return rcp(RoughnessT * RoughnessB * pow2(D));
		}

		public static float D_AnisotropyGGX(float ToH, float BoH, float NoH, float RoughnessT, float RoughnessB)
		{
			return Inv_Pi * D_AnisotropyGGX_NoPi(ToH, BoH, NoH, RoughnessT, RoughnessB);
		}

		public static float D_InvBlinn_NoPi(float NoH, float Roughness)
		{
			float Roughness4 = pow4(Roughness);
			float Cos2h = NoH * NoH;
			float Sin2h = 1 - Cos2h;
			return rcp(5 * Roughness4) * (5 * exp(-Cos2h / Roughness4));
		}

		public static float D_InvBlinn(float NoH, float Roughness)
		{
			return Inv_Pi * D_InvBlinn_NoPi(NoH, Roughness);
		}

		public static float D_InvBeckmann_NoPi(float NoH, float Roughness)
		{
			float Roughness4 = pow4(Roughness);
			float Cos2h = NoH * NoH;
			float Sin2h = 1 - Cos2h;
			float Sin4h = Sin2h * Sin2h;
			return rcp((5 * Roughness4) * Sin4h) * (Sin4h + 4 * exp(-Cos2h / (Roughness4 * Sin2h)));
		}

		public static float D_InvBeckmann(float NoH, float Roughness)
		{
			return Inv_Pi * D_InvBeckmann_NoPi(NoH, Roughness);
		}

		public static float D_Ashikhmin_NoPi(float NoH, float Roughness)
		{
			float a2 = pow4(Roughness);
			float d = (NoH - a2 * NoH) * NoH + a2;
			return rcp(1 + 4 * a2) * (1 + 4 * a2 * a2 / (d * d));
		}

		public static float D_Ashikhmin(float NoH, float Roughness)
		{
			return Inv_Pi * D_Ashikhmin_NoPi(NoH, Roughness);
		}

		public static float D_Charlie_NoPi(float NoH, float Roughness)
		{
			float invR = rcp(Roughness);
			float cos2h = pow2(NoH);
			float sin2h = 1 - cos2h;
			return (2 + invR) * pow(sin2h, invR * 0.5f) / 2;
		}

		public static float D_Charlie(float NoH, float Roughness)
		{
			return Inv_Pi * D_Charlie_NoPi(NoH, Roughness);
		}



		/////////////////////////////////////////////////////////////////Specular V
		public static float Vis_Neumann(float NoL, float NoV)
		{
			return rcp(4 * max(NoL, NoV));
		}

		public static float Vis_Kelemen(float VoH)
		{
			return rcp(4 * VoH * VoH + 1e-5f);
		}

		public static float Vis_Schlick(float NoL, float NoV, float Roughness)
		{
			float k = pow2(Roughness) * 0.5f;
			float Vis_SchlickV = NoV / (NoV * (1 - k) + k);
			float Vis_SchlickL = NoL / (NoL * (1 - k) + k);
			return Vis_SchlickV * Vis_SchlickL;
		}

		public static float Vis_Smith(float NoL, float NoV, float Roughness)
		{
			float a2 = pow4(Roughness);
			float Vis_SmithV = NoV + sqrt(NoV * (NoV - NoV * a2) + a2);
			float Vis_SmithL = NoL + sqrt(NoL * (NoL - NoL * a2) + a2);
			return rcp(Vis_SmithV * Vis_SmithL);
		}

		public static float Vis_SmithJointApprox_NoPI(float NoL, float NoV, float Roughness)
		{
			float a = pow2(Roughness);
			float LambdaL = NoV * (NoL * (1 - a) + a);
			float LambdaV = NoL * (NoV * (1 - a) + a);
			return 0.5f / rcp(LambdaV + LambdaL);
		}

		public static float Vis_SmithJointApprox(float NoL, float NoV, float Roughness)
		{
			return Inv_Pi * Vis_SmithJointApprox_NoPI(NoL, NoV, Roughness);
		}

		public static float Vis_SmithJoint_NoPI(float NoL, float NoV, float Roughness)
		{
			float a2 = pow4(Roughness);
			float LambdaV = NoL * sqrt(NoV * (NoV - NoV * a2) + a2);
			float LambdaL = NoV * sqrt(NoL * (NoL - NoL * a2) + a2);
			return 0.5f / rcp(LambdaL + LambdaV);
		}

		public static float Vis_SmithJoint(float NoL, float NoV, float Roughness)
		{
			return Inv_Pi * Vis_SmithJoint_NoPI(NoL, NoV, Roughness);
		}

		public static float Vis_AnisotropyGGX_NoPi(float ToV, float BoV, float NoV, float ToL, float BoL, float NoL, float RoughnessT, float RoughnessB)
		{
			RoughnessT = pow2(RoughnessT);
			RoughnessB = pow2(RoughnessB);

			float LambdaV = NoL * sqrt(RoughnessT * pow2(ToV) + RoughnessB * pow2(BoV) + pow2(NoV));
			float LambdaL = NoV * sqrt(RoughnessT * pow2(ToL) + RoughnessB * pow2(BoL) + pow2(NoL));

			return 0.5f / rcp(LambdaV + LambdaL);
		}

		public static float Vis_AnisotropyGGX(float ToV, float BoV, float NoV, float ToL, float BoL, float NoL, float RoughnessT, float RoughnessB)
		{
			return Inv_Pi * Vis_AnisotropyGGX_NoPi(ToV, BoV, NoV, ToL, BoL, NoL, RoughnessT, RoughnessB);
		}

		public static float Vis_Ashikhmin(float NoL, float NoV)
		{
			return rcp(4 * (NoL + NoV - NoL * NoV));
		}

		public static float Vis_Charlie(float NoL, float NoV, float Roughness)
		{
			float lambdaV = NoV < 0.5 ? exp(CharlieL(NoV, Roughness)) : exp(2 * CharlieL(0.5f, Roughness) - CharlieL(1 - NoV, Roughness));
			float lambdaL = NoL < 0.5 ? exp(CharlieL(NoL, Roughness)) : exp(2 * CharlieL(0.5f, Roughness) - CharlieL(1 - NoL, Roughness));

			return rcp((1 + lambdaV + lambdaL) * (4 * NoV * NoL));
		}

		public static float Vis_Hair(float B, float Theta)
		{
			return exp(-0.5f * Square(Theta) / (B * B)) / (sqrt(2 * PI) * B);
		}
	}
}