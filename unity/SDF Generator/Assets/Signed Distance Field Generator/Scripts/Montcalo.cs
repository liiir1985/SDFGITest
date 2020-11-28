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

namespace SDFGenerator
{
    public static class Montcalo
	{
		public static uint ReverseBits32(uint bits)
		{
			bits = (bits << 16) | (bits >> 16);
			bits = ((bits & 0x00ff00ff) << 8) | ((bits & 0xff00ff00) >> 8);
			bits = ((bits & 0x0f0f0f0f) << 4) | ((bits & 0xf0f0f0f0) >> 4);
			bits = ((bits & 0x33333333) << 2) | ((bits & 0xcccccccc) >> 2);
			bits = ((bits & 0x55555555) << 1) | ((bits & 0xaaaaaaaa) >> 1);
			return bits;
		}

		public static float RadicalInverse_VdC(uint bits)
		{
			bits = (bits << 16) | (bits >> 16);
			bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
			bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
			bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
			bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
			return (float)(bits) * 2.3283064365386963e-10f;
		}

		static float RadicalInverseSpecialized(uint _base, uint a)
		{
			float invBase = (float)1 / (float)_base;
			uint reversedDigits = 0;
			float invBaseN = 1;
			while (a > 0)
			{
				uint next = a / _base;
				uint digit = a - next * _base;
				reversedDigits = reversedDigits * _base + digit;
				invBaseN *= invBase;
				a = next;
			}
			return reversedDigits * invBaseN;
		}

		static float RadicalInverseSpecialized2(uint a)
		{
			return (float)reversebits(a) / (float)0xffffffffu;
		}
		public static float2 Hammersley(uint Index, uint NumSamples)
		{
			return float2((float)Index / (float)NumSamples, RadicalInverse_VdC(Index));
		}

		public static float2 Hammersley(uint Index, uint NumSamples, uint2 Random)
		{
			float E1 = frac((float)Index / NumSamples + (float)(Random.x & 0xffff) / (1 << 16));
			float E2 = (float)(ReverseBits32(Index) ^ Random.y) * 2.3283064365386963e-10f;
			return float2(E1, E2);
		}

		public static float2 Hammersley2(uint a)
		{
			return float2(RadicalInverseSpecialized2(a), RadicalInverseSpecialized(3, a));
		}

		public static float2 Hammersley16(uint Index, uint NumSamples, uint2 Random)
		{
			float E1 = frac((float)Index / NumSamples + (float)(Random.x) * (1.0f / 65536.0f));
			float E2 = (float)((ReverseBits32(Index) >> 16) ^ Random.y) * (1.0f / 65536.0f);
			return float2(E1, E2);
		}
		public static float3x3 GetTangentBasis(float3 TangentZ)
		{
			float3 UpVector = abs(TangentZ.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
			float3 TangentX = normalize(cross(UpVector, TangentZ));
			float3 TangentY = cross(TangentZ, TangentX);
			return float3x3(TangentX, TangentY, TangentZ);
		}

		public static float3 TangentToWorld(float3 Vec, float3 TangentZ)
		{
			return mul(Vec, GetTangentBasis(TangentZ));
		}

		public static float3 WorldToTangent(float3 Vec, float3 TangentZ)
		{
			return mul(GetTangentBasis(TangentZ), Vec);
		}

		public static float4 TangentToWorld(float3 Vec, float4 TangentZ)
		{
			float3 T2W = TangentToWorld(Vec, TangentZ.xyz);
			return float4(T2W, TangentZ.w);
		}
		public static float4 ImportanceSampleGGX(float2 E, float Roughness)
		{
			float m = Roughness * Roughness;
			float m2 = m * m;

			float Phi = 2 * PI * E.x;
			float CosTheta = sqrt((1 - E.y) / (1 + (m2 - 1) * E.y));
			float SinTheta = sqrt(1 - CosTheta * CosTheta);

			float3 H;
			H.x = SinTheta * cos(Phi);
			H.y = SinTheta * sin(Phi);
			H.z = CosTheta;

			float d = (CosTheta * m2 - CosTheta) * CosTheta + 1;
			float D = m2 / (PI * d * d);

			float PDF = D * CosTheta;
			return float4(H, PDF);
		}


		public static float4 ImportanceSampleVisibleGGX(float2 E, float Roughness, float3 V)
		{
			float a = Roughness * Roughness;
			float a2 = a * a;

			// stretch
			float3 Vh = normalize(float3(a * V.xy, V.z));

			// Orthonormal basis
			float3 Tangent0 = (Vh.z < 0.9999) ? normalize(cross(float3(0, 0, 1), Vh)) : float3(1, 0, 0);
			float3 Tangent1 = cross(Vh, Tangent0);

			float Radius = sqrt(E.x);
			float Phi = 2 * PI * E.y;

			float2 p = Radius * float2(cos(Phi), sin(Phi));
			float s = 0.5f + 0.5f * Vh.z;
			p.y = (1 - s) * sqrt(1 - p.x * p.x) + s * p.y;

			float3 H;
			H = p.x * Tangent0;
			H += p.y * Tangent1;
			H += sqrt(saturate(1 - dot(p, p))) * Vh;

			// unstretch
			H = normalize(float3(a * H.x, a * H.y, max(0.0f, H.z)));

			float NoV = V.z;
			float NoH = H.z;
			float VoH = dot(V, H);

			float d = (NoH * a2 - NoH) * NoH + 1;
			float D = a2 / (PI * d * d);

			float G_SmithV = 2 * NoV / (NoV + sqrt(NoV * (NoV - NoV * a2) + a2));

			float PDF = G_SmithV * VoH * D / NoV;

			return float4(H, PDF);
		}
	}
}