//
// Copyright © Daniel Shervheim, 2019
// www.danielshervheim.com
//

using UnityEngine;
using UnityEditor;

using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace SDFGenerator
{
    [CustomEditor(typeof(SDFGI))]
    public class SDFGIEditor : Editor
    {
        int curDepth;
        public unsafe override void OnInspectorGUI()
        {
            SDFGI sdfgi = target as SDFGI;
            int maxDepth = sdfgi.MaxDepth;

            curDepth = EditorGUILayout.IntSlider("Draw Depth", curDepth, 0, maxDepth);
            sdfgi.GizmoDepth = curDepth;
        }
    }
}
