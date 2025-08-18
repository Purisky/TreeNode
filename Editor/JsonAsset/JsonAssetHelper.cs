using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace TreeNode.Editor
{
    public static class JsonAssetHelper
    {
        static Dictionary<string, Texture2D> Icons;
        static Texture2D defaultIcon;
        [MenuItem(I18n.Editor.Menu.TreeNode + "/" + I18n.Editor.Menu.ForceReloadIcon)]
        public static void ReloadIcon()
        {
            Icons = new();
            foreach (var item in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                .Where(n => n.BaseType == typeof(TreeNodeAsset) && n.GetCustomAttribute<Utility.IconAttribute>() != null))
            {
                Utility.IconAttribute attribute = item.GetCustomAttribute<Utility.IconAttribute>();
                attribute.path ??= $"{ResourcesUtil.ROOTPATH}Icons/{item.Name}.png";
                //Debug.Log(item.Name);
                Texture2D texture2D = AssetDatabase.LoadAssetAtPath<Texture2D>(attribute.path);
                if (texture2D != null)
                {
                    Icons[item.Name] = texture2D;
                }

            }
            defaultIcon = ResourcesUtil.DefaultIcon();
            AssetDatabase.GetAllAssetPaths().Where(n => n.EndsWith(".ja") || n.EndsWith(".tpl")).ToList().ForEach(n =>
            {
                AssetDatabase.ImportAsset(n);
            });
        }


        public static Texture2D GetIcon(string type)
        {
            if (Icons == null)
            {
                ReloadIcon();
            }
            if (type == null || !Icons.TryGetValue(type, out Texture2D icon))
            {
                icon = defaultIcon;
            }
            return icon;
        }
        public static Dictionary<string, string> TypeCaches = new();

        public static string GetType(string path)
        {
            if (!TypeCaches.TryGetValue(path, out string type))
            {
                type = null;
                try
                {
                    type = Json.Get<JsonAsset>(File.ReadAllText(path)).Data.GetType().Name;
                }
                catch (Exception)
                {
                }
                TypeCaches[path] = type;
            }
            return type;
        }
        public static Texture2D GetIconByPath(string path) => GetIcon(GetType(path));


    }




}
