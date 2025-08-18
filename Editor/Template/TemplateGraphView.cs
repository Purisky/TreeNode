using System;
using System.IO;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    [Serializable]
    public class TemplateGraphView : TreeNodeGraphView
    {
        public TemplateAsset AssetData => (TemplateAsset)Asset.Data;

        public TemplateInfo TemplateInfo;
        public TemplateGraphView(TreeNodeGraphWindow window) : base(window)
        {


        }
        public override void CreateEdge(Edge edge)
        {
            bool root = AssetData.RootNode == edge.ParentPort().node.Data;
            base.CreateEdge(edge);
            edge.ChildPort().GetFirstAncestorOfType<PropertyElement>().SetOutput(false);
            if (root)
            {
                SetRoot(edge.ChildPort().node.GetRoot());
            }
            TemplateInfo.UpdateProperties();
        }

        public override void RemoveViewNode(ViewNode node)
        {
            bool root = AssetData.RootNode == node.Data;
            base.RemoveViewNode(node);
            if (root)
            {
                if (AssetData.RootNode != null)
                {
                    NodeDic[AssetData.RootNode].AddToClassList("TemplateRoot");
                }
            }
            TemplateInfo.UpdateProperties();
        }
        public override void RemoveEdge(Edge edge)
        {
            base.RemoveEdge(edge);
            TemplateInfo.UpdateProperties();
        }


        public override void AddNode(JsonNode node)
        {
            base.AddNode(node);
            if (AssetData.Nodes.Count == 1)
            {
                ViewNodes[0].AddToClassList("TemplateRoot");
            }
        }



        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            if (evt.target is ViewNode node)
            {
                evt.menu.AppendAction(I18n.Editor.Menu.SetRoot, delegate
                {
                    SetRoot(node);
                }, SetRootAble(node) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Hidden);
                evt.menu.AppendSeparator();
            }


        }

        public bool SetRootAble(ViewNode node)
        {
            int index = node.GetIndex();
            return index != -1 && 0 != node.GetIndex();
        }


        public void SetRoot(ViewNode node)
        {
            int index = node.GetIndex();
            if (index == -1) { return; }
            for (int i = 0; i < ViewNodes.Count; i++)
            {
                ViewNodes[i].RemoveFromClassList("TemplateRoot");
            }
            JsonNode jsonNode = AssetData.Nodes[index];
            AssetData.Nodes.Remove(jsonNode);
            AssetData.Nodes.Insert(0, jsonNode);
            node.AddToClassList("TemplateRoot");
        }

        public override void OnSave()
        {
            if (AssetData.RootNode == null) { return; }
            for (int i = 0; i < TemplateInfo.Properties.Count; i++)
            {
                TemplateInfoProperty property = TemplateInfo.Properties[i];
                //Debug.Log(property.PropertyElement.layout.width);
                AssetData.Width = Math.Max(AssetData.Width, (int)property.PropertyElement.layout.width);
            }
            TemplatePreviewData templatePreviewData = new(Window.Path, AssetData);
            TemplateManager.Previews[Window.Path] = templatePreviewData;
            TemplateDataCodeGen.GenCode(Path.GetFileNameWithoutExtension(Window.Path), AssetData);


            //AssetDatabase.ImportAsset("Assets/TreeNode/Runtime/Plugins/TreeNodeCodeGen.dll");
            //CompilationPipeline.RequestScriptCompilation();
            //AssetDatabase.Refresh();
        }


    }
}
