using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TreeNode.Utility;
using UnityEditor;
using UnityEngine;

namespace TreeNode.Runtime
{
    public interface IUniqNode
    {
        public string ID { get; set; }
        public string Name { get; set; }
        [JsonIgnore]
        public string DisplayName => $"{Name}({ID})";
    }
    public class UniqNodeManager<TN,TA> where TN: JsonNode, IUniqNode where TA : TreeNodeAsset
    {
        public static DropdownList<string> Dropdowns = GenDropDownList();
        static Dictionary<string, string> Dic;
        static DropdownList<string> GenDropDownList()
        {
            Dic??= InitDic();
            DropdownList<string> dropdownItems = new ();
            foreach (var item in Dic)
            {
                dropdownItems.Add(new DropdownItem<string>(item.Value, item.Key));
            }
            return dropdownItems;
        }
        static Dictionary<string, string> InitDic()
        {
            Dictionary<string, string> dic = new();
            List<string> paths = AssetDatabase.GetAllAssetPaths().Where(n => n.EndsWith(".ja")).ToList();
            for (int i = 0; i < paths.Count; i++)
            {
                TreeNodeAsset jsonAsset = JsonAsset.GetJsonAsset(paths[i]).Data;
                if (jsonAsset is TA asset)
                {
                    foreach (var node in asset.Nodes)
                    {
                        if (node is TN tNode && !string.IsNullOrEmpty(tNode.ID))
                        {
                            dic[tNode.ID] = tNode.DisplayName;
                        }
                    }
                }
            }
            //Debug.Log(dic.Count);
            return dic;
        }
        public static void Update(TA asset)
        {
            bool change = false;
            foreach (var node in asset.Nodes)
            {
                if (node is TN tNode && !string.IsNullOrEmpty(tNode.ID))
                {
                    string value = tNode.DisplayName;
                    if (Dic.TryGetValue(tNode.ID, out string value_) && value_ == value)
                    {
                        continue;
                    }
                    Dic[tNode.ID] = $"{tNode.Name}({tNode.ID})";
                    change = true;
                }
            }
            if (change)
            {
                Dropdowns = GenDropDownList();
            }
        }





    }
}
