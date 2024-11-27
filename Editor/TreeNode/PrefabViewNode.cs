using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class PrefabViewNode : ViewNode
    {
        public PrefabViewNode(JsonNode data, TreeNodeGraphView view, ChildPort childPort = null) : base(data, view, childPort)
        {





        }

        public override void Draw(ChildPort childPort = null)
        {
            PrefabPreviewData ppData = NodePrefabManager.GetData(Data.PrefabData.ID);

            style.width = ppData.Width;
            DrawParentPort(ppData.OutputType, childPort);
            title = ppData.Name;
            ChildPorts = new();
            for (int i = 0; i < ppData.Fields.Count; i++)
            {
                BaseDrawer baseDrawer = DrawerManager.Get(ppData.Fields[i].Type);
                PropertyPath path = new($"PrefabData._{ppData.Fields[i].ID}");
                if (baseDrawer != null)
                {
                    //MemberInfo memberInfo = new MemberInfo()
                    MemberMeta meta = new()
                    {
                        Path = path,
                        Type = ppData.Fields[i].Type,
                        LabelInfo = new() { Text = ppData.Fields[i].Name },
                        ShowInNode = new(),
                        Json = true,
                    };
                    VisualElement visualElement = baseDrawer.Create(meta, this, path, null);
                    Content.Add(visualElement);
                }
            }

        }
        public void OpenPrefabAsset()
        {
            PrefabPreviewData ppData = NodePrefabManager.GetData(Data.PrefabData.ID);
            JsonAssetHandler.OpenPrefabJsonAsset(ppData.Path);
        }


    }
}
