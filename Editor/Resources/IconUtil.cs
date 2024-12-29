using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TreeNode.Editor
{
    public class IconUtil
    {
        static readonly Dictionary<string, Texture2D> Dic = new();
        public static Texture2D Get(string path)
        {
            if (Dic.TryGetValue(path, out Texture2D texture))
            {
                return texture;
            }
            Dic[path] = texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return texture;
        }

    }
}
