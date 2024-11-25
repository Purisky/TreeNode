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
    
    [ScriptedImporter(1,new string[] { "ja", "pja" })]
    public class JsonAssetImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            TextAsset defaultAsset = new(File.ReadAllText(ctx.assetPath));
            ctx.AddObjectToAsset("icon", defaultAsset, JsonAssetHelper.GetIconByPath(ctx.assetPath));
            ctx.SetMainObject(defaultAsset);
        }
    }



}