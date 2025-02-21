using Newtonsoft.Json.Linq;
using System;
using TreeNode.Utility;
using UnityEngine;
namespace TreeNode.Runtime
{
    public class JsonAsset
    {
        public TreeNodeAsset Data;





        public static JsonAsset GetJsonAsset(string filePath)
        {
            JsonAsset jsonAsset = null;
            string text = System.IO.File.ReadAllText(filePath);
            JObject job = JObject.Parse(text);
            if (job == null)
            {
                Debug.LogError($"Unknown asset type : {filePath}");
                return null;
            }
            CheckTypeRecursively(job);
            try
            {
                jsonAsset = Json.Get<JsonAsset>(text);
            }
            catch (Exception)
            {
                Debug.LogError($"Json parse error : {filePath}");
                return null;
            }
            return jsonAsset;
        }
        public static JsonAsset GetJsonAssetByText(string text)
        {
            JsonAsset jsonAsset = null;
            JObject job = JObject.Parse(text);
            if (job == null)
            {
                Debug.LogError($"Unknown asset type : {text}");
                return null;
            }
            CheckTypeRecursively(job);
            try
            {
                jsonAsset = Json.Get<JsonAsset>(text);
            }
            catch (Exception)
            {
                Debug.LogError($"Json parse error : {text}");
                return null;
            }
            return jsonAsset;
        }
        private static void CheckTypeRecursively(JObject job)
        {
            foreach (var property in job.Properties())
            {
                if (property.Value is JObject obj)
                {
                    CheckAndLogType(obj);
                    CheckTypeRecursively(obj);
                }
                else if (property.Value is JArray array)
                {
                    foreach (var item in array)
                    {
                        if (item is JObject arrayObj)
                        {
                            CheckAndLogType(arrayObj);
                            CheckTypeRecursively(arrayObj);
                        }
                    }
                }
            }
        }

        private static void CheckAndLogType(JObject obj)
        {
            if (obj["$type"] != null)
            {
                string typeName = obj["$type"].ToString();
                Type type = Type.GetType(typeName);
                if (type == null)
                {
                    Debug.LogError($"Type '{typeName}' not found in assemblies.");
                }
            }
        }
    }
}

