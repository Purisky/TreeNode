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

