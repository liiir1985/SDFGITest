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
        SerializedProperty depthTexture, normalTexture, albedoTexture, metallicTexture, sun, rawImg, equiatorColor, skyColor;
        private void OnEnable()
        {
            depthTexture = serializedObject.FindProperty("depthTexture");
            normalTexture = serializedObject.FindProperty("normalTexture");
            albedoTexture = serializedObject.FindProperty("albedoTexture");
            metallicTexture = serializedObject.FindProperty("metallicTexture");
            sun = serializedObject.FindProperty("sun");
            equiatorColor = serializedObject.FindProperty("equiatorColor");
            skyColor = serializedObject.FindProperty("skyColor");
            rawImg = serializedObject.FindProperty("rawImg");
        }
        public unsafe override void OnInspectorGUI()
        {
            SDFGI sdfgi = target as SDFGI;
            int maxDepth = sdfgi.MaxDepth;

            EditorGUILayout.PropertyField(sun);
            EditorGUILayout.PropertyField(equiatorColor);
            EditorGUILayout.PropertyField(skyColor);
            EditorGUILayout.PropertyField(rawImg);
            EditorGUILayout.PropertyField(depthTexture);
            EditorGUILayout.PropertyField(normalTexture);
            EditorGUILayout.PropertyField(albedoTexture);
            EditorGUILayout.PropertyField(metallicTexture);

            serializedObject.ApplyModifiedProperties();
            curDepth = EditorGUILayout.IntSlider("Draw Depth", curDepth, 0, maxDepth);
            sdfgi.GizmoDepth = curDepth;

            if(GUILayout.Button("Do GI"))
            {
                sdfgi.DoGI();
            }
        }
    }
}
