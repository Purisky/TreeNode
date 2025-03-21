using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TreeNode.Exceptions;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
namespace TreeNode.Editor
{
    public static class NodePrefabManager
    {
        public static Dictionary<string, PrefabPreviewData> Previews = Init();


        public static Dictionary<string, PrefabPreviewData> Init()
        {
            Dictionary<string, PrefabPreviewData> dic = new();
            List<string> paths = AssetDatabase.GetAllAssetPaths().Where(n => n.EndsWith(".pja")).ToList();
            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    PrefabPreviewData data = GetDataByPath(paths[i]);
                    dic[data.ID] = data;
                }
                catch (Exception e)
                {
                    Debug.Log(e);
                }
            }
            return dic;
        }





        public static PrefabPreviewData GetData(string id)
        {
            Previews.TryGetValue(id, out PrefabPreviewData previewData);
            return previewData;
        }

        static PrefabPreviewData GetDataByPath(string path)
        {
            JsonAsset jsonAsset = JsonAsset.GetJsonAsset(path) ?? throw new NodePrefabAssetException.JsonEmpty(path);
            if (jsonAsset.Data is NodePrefabAsset nodePrefabAsset)
            {
                if (!nodePrefabAsset.Nodes.Any())
                {
                    throw new NodePrefabAssetException.NodeEmpty(path);
                }
                PrefabPreviewData prefabPreviewData = new(path, nodePrefabAsset);
                return prefabPreviewData;
            }
            throw new NodePrefabAssetException.DataTypeError(path);
        }





    }
    public class PrefabPreviewData
    {
        public string ID;
        public string Path;
        public string Name;
        public Type OutputType;
        public List<PreviewField> Fields;
        public int Width;

        public PrefabPreviewData(string path, NodePrefabAsset asset)
        {
            Path = path;
            ID = System.IO.Path.GetFileNameWithoutExtension(Path);
            Name = asset.Name;
            Width = asset.Width;
            Debug.Log(Width);
            //NodeInfoAttribute nodeInfo = asset.RootNode.GetType().GetCustomAttribute<NodeInfoAttribute>();
            OutputType = asset.RootNode.GetType();
            Fields = new();
            for (int i = 0; i < asset.Properties.Count; i++)
            {
                object def = asset.GetValue<object>(asset.Properties[i].Path);
                Fields.Add(new(asset.Properties[i].ID, asset.Properties[i].Name, ByName(asset.Properties[i].Type), def));
            }
        }
        private static Type ByName(string name)
        {
            return
                AppDomain.CurrentDomain.GetAssemblies()
                    .Reverse()
                    .Select(assembly => assembly.GetType(name))
                    .FirstOrDefault(t => t != null)
                ??
                AppDomain.CurrentDomain.GetAssemblies()
                    .Reverse()
                    .SelectMany(assembly => assembly.GetTypes())
                    .FirstOrDefault(t => t.Name.Contains(name));
        }
        public class PreviewField
        {
            public string ID;
            public string Name;
            public Type Type;
            public object DefaultValue;
            public PreviewField(string iD, string name, Type type, object def)
            {
                ID = iD;
                Name = name;
                Type = type;
                DefaultValue = def;
            }

            public object DeepClone()
            {
                if (DefaultValue is (null or ValueType or string))
                {
                    return DefaultValue;
                }
                return Json.DeepCopy(DefaultValue);
            }
        }


        public JsonNode CreateNode()
        {
            Debug.Log(OutputType);
            JsonNode node = Activator.CreateInstance(OutputType) as JsonNode;
            node.PrefabData = Activator.CreateInstance(ByName(ID)) as PrefabData;
            for (int i = 0; i < Fields.Count; i++)
            {
                PropertyAccessor.SetValue(node.PrefabData, $"_{Fields[i].ID}", Fields[i].DeepClone());
            }





            //node.PrefabId = ID;
            //node.Dic = new();
            //for (int i = 0; i < Fields.Count; i++)
            //{
            //    node.Dic[Fields[i].ID] = Fields[i].DeepClone();
            //}
            return node;
        }

    }


}
