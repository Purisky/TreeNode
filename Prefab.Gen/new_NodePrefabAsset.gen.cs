using Newtonsoft.Json;
using TreeNode.Utility;
namespace TreeNode.Runtime.Gen
{
    public class new_NodePrefabAsset : PrefabData
    {
        [JsonIgnore]public override string ID => "new_NodePrefabAsset";
        [JsonIgnore]public override string Name => "";
        [LabelInfo(Text="直接")]
        public System.Boolean _00;
        [LabelInfo(Text="真")]
        public SkillEditorDemo.Model.FuncValue _01;
    }
}