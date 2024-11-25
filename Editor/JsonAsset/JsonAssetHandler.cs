using UnityEditor.Callbacks;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using TreeNode.Utility;
using System.Collections.Generic;
using System;
using System.Linq;
using TreeNode.Runtime;
using UnityEditor.VersionControl;
using Newtonsoft.Json;
using System.Reflection;

namespace TreeNode.Editor
{
    public class JsonAssetHandler
    {
        static Dictionary<Type,Type > AssetWindows;

        static JsonAssetHandler()
        {

            AssetWindows = new();
            foreach (var item in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                .Where(n => n.Inherited(typeof(TreeNodeGraphWindow))&& n.GetCustomAttribute<NodeAssetAttribute>()!=null)
                )
            {
                Type assetType = item.GetCustomAttribute<NodeAssetAttribute>().Type;
                AssetWindows[assetType] = item;
            }
        }


        [OnOpenAsset(0)]
        public static bool OnOpenFile(int instanceID, int line)
        {
            //Debug.Log("OnOpenFile");
            UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceID);
            string filePath = AssetDatabase.GetAssetPath(obj);
            //Debug.Log(filePath);
            if (filePath.EndsWith(".ja"))
            {
                OpenJsonAsset(filePath);
                return true;
            }
            if (filePath.EndsWith(".pja"))
            {
                OpenPrefabJsonAsset(filePath);
                return true;
            }
            return false;
        }






        public static void OpenJsonAsset(string filePath)
        {
            JsonAsset jsonAsset = GetJsonAsset(filePath);
            if (jsonAsset == null) { return; }
            Type assetType = jsonAsset.Data.GetType();
            if (!AssetWindows.TryGetValue(assetType, out  Type window))
            {
                Debug.LogError($"Asset window class not exist : {assetType.Name}");
                return;
            }
            //Debug.Log("OnOpenFile");
            MethodInfo method = typeof(WindowManager).GetMethod("Open");
            method.MakeGenericMethod(window, assetType).Invoke(null, new object[] { jsonAsset.Data, filePath });
        }
        public static void OpenPrefabJsonAsset(string filePath)
        {
            JsonAsset jsonAsset = GetJsonAsset(filePath);
            if (jsonAsset == null) { return; }
            if (jsonAsset.Data is NodePrefabAsset nodePrefabAsset)
            {
                WindowManager.Open<NodePrefabWindow, NodePrefabAsset>(nodePrefabAsset, filePath);
                return;
            }
            Debug.LogError($"Asset data type error");
        }


        public static JsonAsset GetJsonAsset(string filePath)
        {
            JsonAsset jsonAsset = null;
            try
            {
                jsonAsset = Json.Get<JsonAsset>(System.IO.File.ReadAllText(filePath));
            }
            catch (Exception)
            {
                Debug.LogError($"Json parse error : {filePath}");
                return null;
            }
            if (jsonAsset == null || jsonAsset.Data is null)
            {
                Debug.LogError($"Unknown asset type : {filePath}");
                return null;
            }
            return jsonAsset;
        }




    }







}

