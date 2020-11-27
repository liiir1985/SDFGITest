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
    public static class RandomFunc
    {
        public static int2 ihash(int2 n)
        {
            n = (n << 13) ^ n;
            return (n * (n * n * 15731 + 789221) + 1376312589) & 2147483647;
        }

        public static int3 ihash(int3 n)
        {
            n = (n << 13) ^ n;
            return (n * (n * n * 15731 + 789221) + 1376312589) & 2147483647;
        }

        public static float2 frand(int2 n)
        {
            return (float2)ihash(n) / 2147483647f;
        }

        public static float3 frand(int3 n)
        {
            return (float3)ihash(n) / 2147483647f;
        }
        public static float2 cellNoise(float2 p, float4 RandomNumber)
        {
            int seed = (int)dot(p, float2(641338.4168541f, 963955.16871685f));
            return sin(float2(frand(int2(seed, seed - 53))) * RandomNumber.xy + RandomNumber.zw);
        }

        public static float3 cellNoise(float3 p, float4 RandomNumber)
        {
            int seed = (int)dot(p, float3(641738.4168541f, 9646285.16871685f, 3186964.168734f));
            return sin(float3(frand(int3(seed, seed - 12, seed - 57))) * RandomNumber.xyz + RandomNumber.w);
        }

        public static float PseudoRandom(float2 xy)
        {
            float2 pos = frac(xy / 128.0f) * 128.0f + float2(-64.340622f, -72.465622f);
            return frac(dot(pos.xyx * pos.xyy, float3(20.390625f, 60.703125f, 2.4281209f)));
        }

        public static float InterleavedGradientNoise(float2 uv, float FrameId)
        {
            uv += FrameId * (float2(47, 17) * 0.695f);
            float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
            return frac(magic.z * frac(dot(uv, magic.xy)));
        }

        public static float RandFast(uint2 PixelPos, float Magic = 3571.0f)
        {
            float2 Random2 = (1.0f / 4320.0f) * (float2)PixelPos + float2(0.25f, 0.0f);
            float Random = frac(dot(Random2 * Random2, Magic));
            Random = frac(Random * Random * (2 * Magic));
            return Random;
        }

        const int BBS_PRIME24 = 4093;

        // Blum-Blum-Shub-inspired pseudo random number generator
        // http://www.umbc.edu/~olano/papers/mNoise.pdf
        // real BBS uses ((s*s) mod M) with bignums and M as the product of two huge Blum primes
        // instead, we use a single prime M just small enough not to overflow
        // note that the above paper used 61, which fits in a half, but is unusably bad
        // @param Integer valued floating point seed
        // @return random number in range [0,1)
        // ~8 ALU operations (5 *, 3 frac)
        public static float RandBBSfloat(float seed)
        {
            float s = frac(seed / BBS_PRIME24);
            s = frac(s * s * BBS_PRIME24);
            s = frac(s * s * BBS_PRIME24);
            return s;
        }

        // 3D random number generator inspired by PCGs (permuted congruential generator)
        // Using a **simple** Feistel cipher in place of the usual xor shift permutation step
        // @param v = 3D integer coordinate
        // @return three elements w/ 16 random bits each (0-0xffff).
        // ~8 ALU operations for result.x    (7 mad, 1 >>)
        // ~10 ALU operations for result.xy  (8 mad, 2 >>)
        // ~12 ALU operations for result.xyz (9 mad, 3 >>)
        public static uint3 Rand3DPCG16(int3 p)
        {
            // taking a signed int then reinterpreting as unsigned gives good behavior for negatives
            uint3 v = uint3(p);

            // Linear congruential step. These LCG constants are from Numerical Recipies
            // For additional #'s, PCG would do multiple LCG steps and scramble each on output
            // So v here is the RNG state
            v = v * 1664525u + 1013904223u;

            // PCG uses xorshift for the final shuffle, but it is expensive (and cheap
            // versions of xorshift have visible artifacts). Instead, use simple MAD Feistel steps
            //
            // Feistel ciphers divide the state into separate parts (usually by bits)
            // then apply a series of permutation steps one part at a time. The permutations
            // use a reversible operation (usually ^) to part being updated with the result of
            // a permutation function on the other parts and the key.
            //
            // In this case, I'm using v.x, v.y and v.z as the parts, using + instead of ^ for
            // the combination function, and just multiplying the other two parts (no key) for 
            // the permutation function.
            //
            // That gives a simple mad per round.
            v.x += v.y * v.z;
            v.y += v.z * v.x;
            v.z += v.x * v.y;
            v.x += v.y * v.z;
            v.y += v.z * v.x;
            v.z += v.x * v.y;

            // only top 16 bits are well shuffled
            return v >> 16;
        }

        // 3D random number generator inspired by PCGs (permuted congruential generator)
        // Using a **simple** Feistel cipher in place of the usual xor shift permutation step
        // @param v = 3D integer coordinate
        // @return three elements w/ 32 random bits each (0-0xffffffff).
        // ~12 ALU operations for result.x   (10 mad, 3 >>)
        // ~14 ALU operations for result.xy  (11 mad, 3 >>)
        // ~15 ALU operations for result.xyz (12 mad, 3 >>)
        public static uint3 Rand3DPCG32(int3 p)
        {
            // taking a signed int then reinterpreting as unsigned gives good behavior for negatives
            uint3 v = uint3(p);

            // Linear congruential step.
            v = v * 1664525u + 1013904223u;

            // swapping low and high bits makes all 32 bits pretty good
            v = v * (1u << 16) + (v >> 16);

            // final shuffle
            v.x += v.y * v.z;
            v.y += v.z * v.x;
            v.z += v.x * v.y;
            v.x += v.y * v.z;
            v.y += v.z * v.x;
            v.z += v.x * v.y;

            return v;
        }



        // 4D random number generator inspired by PCGs (permuted congruential generator)
        // Using a **simple** Feistel cipher in place of the usual xor shift permutation step
        // @param v = 4D integer coordinate
        // @return four elements w/ 32 random bits each (0-0xffffffff).
        // ~12 ALU operations for result.x   (10 mad, 3 >>)
        // ~14 ALU operations for result.xy  (11 mad, 3 >>)
        // ~15 ALU operations for result.xyz (12 mad, 3 >>)
        public static uint4 Rand4DPCG32(int4 p)
        {
            // taking a signed int then reinterpreting as unsigned gives good behavior for negatives
            uint4 v = uint4(p);

            // Linear congruential step.
            v = v * 1664525u + 1013904223u;

            // swapping low and high bits makes all 32 bits pretty good
            v = v * (1u << 16) + (v >> 16);

            // final shuffle
            v.x += v.y * v.w;
            v.y += v.z * v.x;
            v.z += v.x * v.y;
            v.w += v.y * v.z;
            v.x += v.y * v.w;
            v.y += v.z * v.x;
            v.z += v.x * v.y;
            v.w += v.y * v.z;

            return v;
        }
    }
}