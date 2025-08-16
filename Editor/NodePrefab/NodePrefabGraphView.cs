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
    public class NodePrefabGraphView : TreeNodeGraphView
    {
        public NodePrefabAsset AssetData => (NodePrefabAsset)Asset.Data;

        public NodePrefabInfo NodePrefabInfo;
        public NodePrefabGraphView(TreeNodeGraphWindow window) : base(window)
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
            NodePrefabInfo.UpdateProperties();
        }

        public override void RemoveViewNode(ViewNode node)
        {
            bool root = AssetData.RootNode == node.Data;
            base.RemoveViewNode(node);
            if (root)
            {
                if (AssetData.RootNode != null)
                {
                    NodeDic[AssetData.RootNode].AddToClassList("PrefabRoot");
                }
            }
            NodePrefabInfo.UpdateProperties();
        }
        public override void RemoveEdge(Edge edge)
        {
            base.RemoveEdge(edge);
            NodePrefabInfo.UpdateProperties();
        }


        public override void AddNode(JsonNode node)
        {
            base.AddNode(node);
            if (AssetData.Nodes.Count == 1)
            {
                ViewNodes[0].AddToClassList("PrefabRoot");
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
                ViewNodes[i].RemoveFromClassList("PrefabRoot");
            }
            JsonNode jsonNode = AssetData.Nodes[index];
            AssetData.Nodes.Remove(jsonNode);
            AssetData.Nodes.Insert(0, jsonNode);
            node.AddToClassList("PrefabRoot");
        }

        public override void OnSave()
        {
            if (AssetData.RootNode == null) { return; }
            for (int i = 0; i < NodePrefabInfo.Properties.Count; i++)
            {
                NodePrefabInfoProperty property = NodePrefabInfo.Properties[i];
                Debug.Log(property.PropertyElement.layout.width);
                AssetData.Width = Math.Max(AssetData.Width, (int)property.PropertyElement.layout.width);
            }
            PrefabPreviewData prefabPreviewData = new(Window.Path, AssetData);
            NodePrefabManager.Previews[Window.Path] = prefabPreviewData;
            PrefabDataCodeGen.GenCode(Path.GetFileNameWithoutExtension(Window.Path), AssetData);


            //AssetDatabase.ImportAsset("Assets/TreeNode/Runtime/Plugins/TreeNodeCodeGen.dll");
            //CompilationPipeline.RequestScriptCompilation();
            //AssetDatabase.Refresh();
        }


    }
}
