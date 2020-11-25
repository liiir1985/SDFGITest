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
        SerializedProperty depthTexture, normalTexture;
        private void OnEnable()
        {
            depthTexture = serializedObject.FindProperty("depthTexture");
            normalTexture = serializedObject.FindProperty("normalTexture");
        }
        public unsafe override void OnInspectorGUI()
        {
            SDFGI sdfgi = target as SDFGI;
            int maxDepth = sdfgi.MaxDepth;

            EditorGUILayout.PropertyField(depthTexture);
            EditorGUILayout.PropertyField(normalTexture);

            serializedObject.ApplyModifiedProperties();
            curDepth = EditorGUILayout.IntSlider("Draw Depth", curDepth, 0, maxDepth);
            sdfgi.GizmoDepth = curDepth;
        }
    }
}
