using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
namespace TreeNode.Editor
{
    public class ResourcesUtil
    {
        public const string ROOTPATH = "Assets/TreeNode/Editor/Resources/";


        public static T Load<T>(string path) where T: Object
        {
            
            //Debug.Log($"{ROOTPATH}{path}");
            return AssetDatabase.LoadAssetAtPath<T>($"{ROOTPATH}{path}");
        }
        public static Texture2D DefaultIcon() => Load<Texture2D>("Icons/Icon.png");

         


        public static StyleSheet LoadStyleSheet(string filename)=> Load<StyleSheet>($"Styles/{filename}.uss");



    }
}