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
using Debug = TreeNode.Utility.Debug;

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
            if (filePath.EndsWith(".tpl"))
            {
                OpenTemplateJsonAsset(filePath);
                return true;
            }
            return false;
        }






        public static TreeNodeGraphWindow OpenJsonAsset(string filePath)
        {
            JsonAsset jsonAsset = JsonAsset.GetJsonAsset(filePath);
            if (jsonAsset == null) { return null; }
            Type assetType = jsonAsset.Data.GetType();
            if (!AssetWindows.TryGetValue(assetType, out  Type window))
            {
                Debug.LogError($"Asset window class not exist : {assetType.Name}");
                return null;
            }
            //Debug.Log("OnOpenFile");
            MethodInfo method = typeof(WindowManager).GetMethod("Open");
           return  method.MakeGenericMethod(window, assetType).Invoke(null, new object[] { jsonAsset.Data, filePath }) as TreeNodeGraphWindow;
        }
        public static void OpenTemplateJsonAsset(string filePath)
        {
            JsonAsset jsonAsset = JsonAsset. GetJsonAsset(filePath);
            if (jsonAsset == null) { return; }
            if (jsonAsset.Data is TemplateAsset templateAsset)
            {
                WindowManager.Open<TemplateWindow, TemplateAsset>(templateAsset, filePath);
                return;
            }
            Debug.LogError($"Asset data type error");
        }
    }







}

