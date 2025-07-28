using System;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class PrefabViewNode : ViewNode
    {
        public PrefabViewNode(JsonNode data, TreeNodeGraphView view) : base(data, view)
        {
        }

        public override void Draw()
        {
            PrefabPreviewData ppData = NodePrefabManager.GetData(Data.PrefabData.ID);

            style.width = ppData.Width;
            DrawParentPort(ppData.OutputType);
            title = ppData.Name;
            ChildPorts = new();
            for (int i = 0; i < ppData.Fields.Count; i++)
            {
                Type type = ppData.Fields[i].Type;
                BaseDrawer baseDrawer = DrawerManager.Get(type);
                if (baseDrawer == null && (type == typeof(NumValue) || type.Inherited(typeof(NumValue))))
                {
                    baseDrawer = DrawerManager.Get(typeof(NumValue));
                }
                if (baseDrawer != null)
                {
                    string path = $"PrefabData._{ppData.Fields[i].ID}";
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
