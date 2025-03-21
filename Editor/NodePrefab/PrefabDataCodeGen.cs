using System.IO;
using TreeNode.Runtime;
using UnityEditor.Compilation;
using UnityEngine;
namespace TreeNode.Editor
{
    public static class PrefabDataCodeGen
    {
        const string RootPath = "TreeNode/Prefab.Gen";

        public static void GenCode(string name, NodePrefabAsset asset)
        {
            string path = $"{Application.dataPath}/{RootPath}/{name}.gen.cs";
            if (File.Exists(path)) { File.Delete(path); }

            string fieldsText = "";

            for (int i = 0; i < asset.Properties.Count; i++)
            {
                PrefabProperty property = asset.Properties[i];
                fieldsText+= $@"
        [LabelInfo(Text=""{property.Name}"")]
        public {property.Type} _{property.ID};";
            }
            string code = $@"using Newtonsoft.Json;
using TreeNode.Utility;
namespace TreeNode.Runtime.Gen
{{
    public class {name} : PrefabData
    {{
        [JsonIgnore]public override string ID => ""{name}"";
        [JsonIgnore]public override string Name => ""{asset.Name}"";{fieldsText}
    }}
}}";
            Directory.CreateDirectory($"{Application.dataPath}/{RootPath}");
            File.WriteAllText(path, code);
            CompilationPipeline.RequestScriptCompilation();
        }


    }
}
