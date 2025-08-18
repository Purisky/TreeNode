using System;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class TemplateNode : ViewNode
    {
        public TemplateNode(JsonNode data, TreeNodeGraphView view) : base(data, view)
        {
        }

        public override void Draw()
        {
            TemplatePreviewData tpData = TemplateManager.GetData(Data.TemplateData.ID);

            style.width = tpData.Width;
            DrawParentPort(tpData.OutputType);
            title = tpData.Name;
            ChildPorts = new();
            for (int i = 0; i < tpData.Fields.Count; i++)
            {
                Type type = tpData.Fields[i].Type;
                BaseDrawer baseDrawer = DrawerManager.Get(type);
                if (baseDrawer == null && (type == typeof(NumValue) || type.Inherited(typeof(NumValue))))
                {
                    baseDrawer = DrawerManager.Get(typeof(NumValue));
                }
                if (baseDrawer != null)
                {
                    string path = $"TemplateData._{tpData.Fields[i].ID}";
                    //MemberInfo memberInfo = new MemberInfo()
                    MemberMeta meta = new()
                    {
                        Path = path,
                        Type = tpData.Fields[i].Type,
                        LabelInfo = new() { Text = tpData.Fields[i].Name },
                        ShowInNode = new(),
                        Json = true,
                    };
                    VisualElement visualElement = baseDrawer.Create(meta, this, path, null);
                    Content.Add(visualElement);
                }
            }

        }
        public void OpenTemplateAsset()
        {
            TemplatePreviewData ppData = TemplateManager.GetData(Data.TemplateData.ID);
            JsonAssetHandler.OpenTemplateJsonAsset(ppData.Path);
        }


    }
}
