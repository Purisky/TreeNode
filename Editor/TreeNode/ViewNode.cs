using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
namespace TreeNode.Editor
{
    public class ViewNode : Node
    {
        public JsonNode Data;
        public TreeNodeGraphView View;
        public ParentPort ParentPort;
        public List<ChildPort> ChildPorts;
        public VisualElement Content;

        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("ViewNode");

        public ViewNode(JsonNode data, TreeNodeGraphView view, ChildPort childPort = null)
        {
            Data = data;
            View = view;
            styleSheets.Add(StyleSheet);
            AddToClassList("view-node");
            Type typeInfo = Data.GetType();
            NodeInfoAttribute nodeInfo = typeInfo.GetCustomAttribute<NodeInfoAttribute>();
            if (nodeInfo != null)
            {
                title = nodeInfo.Title;
                style.width = nodeInfo.Width;
                titleContainer.style.backgroundColor = nodeInfo.Color;
                titleContainer.Q<Label>().style.marginRight = 6;
            }
            titleContainer.Q<Label>().style.unityFontStyleAndWeight = FontStyle.Bold;
            this.name = typeInfo.Name;
            Content = this.Q<VisualElement>("contents");
            Content.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            Content.RemoveAt(1);
            Content.Q("divider").AddToClassList("hidden");
            this.Q("title-button-container").style.display = DisplayStyle.None;
            this.Q("title-label").style.unityTextAlign = TextAnchor.MiddleCenter;
            this.Q("title-label").style.flexGrow = 1;
            Draw(childPort);
            OnChange();
        }


        public virtual void Draw(ChildPort childPort = null)
        {
            NodeInfoAttribute nodeInfo = Data.GetType().GetCustomAttribute<NodeInfoAttribute>();
            DrawParentPort(nodeInfo.Type,childPort);
            DrawPropertiesAndPorts();
        }




        public List<Edge> GetAllEdges()
        {
            List<Edge> edges = new();
            for (int i = 0; i < ChildPorts.Count; i++)
            {
                ChildPort childPort = ChildPorts[i];
                edges.AddRange(childPort.connections);
            }
            if (ParentPort != null && ParentPort.connected)
            {
                edges.Add(ParentPort.connections.First());
            }
            return edges;

        }




        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Data.Position = newPos.position;
        }

        public void DrawParentPort(Type parentType, ChildPort childPort = null)
        {
            if (parentType == null) { return; }
            ParentPort = ParentPort.Create(parentType);
            ParentPort.OnChange = OnChange;
            titleContainer.Insert(1, ParentPort);
            if (childPort != null)
            {
                Edge edge = childPort.ConnectTo(ParentPort);
                View.AddElement(edge);
            }
        }

        public ViewNode GetRoot()
        {
            if (ParentPort == null || !ParentPort.connected) { return this; }
            return (ParentPort.connections.First().ChildPort().node).GetRoot();
        }
        public ViewNode GetParent()
        {
            if (ParentPort == null || !ParentPort.connected) { return null; }
            return ParentPort.connections.First().ChildPort().node;
        }

        public int GetDepth()
        {
            if (ParentPort == null || !ParentPort.connected) { return 0; }
            return (ParentPort.connections.First().ChildPort().node).GetDepth() + 1;
        }
        public int GetChildMaxDepth()
        {
            int maxDepth = GetDepth();
            foreach (var item in ChildPorts)
            {
                foreach (var child in item.GetChildValues())
                {
                    ViewNode viewNode = View.NodeDic[child];
                    maxDepth = Math.Max(maxDepth, viewNode.GetChildMaxDepth());
                }
            }
            return maxDepth;
        }
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
        }

        public void DrawPropertiesAndPorts()
        {
            ChildPorts = new();
            BaseDrawer baseDrawer = DrawerManager.Get(Data.GetType());
            if (baseDrawer != null)
            {
                MemberMeta meta = new()
                {
                    Type = Data.GetType(),
                    LabelInfo = new() { Text = Data.GetType().Name,Hide = true },
                };
                VisualElement visualElement = baseDrawer.Create(meta, this,null,OnChange);
                Content.Add(visualElement);
            }
        }

        List<ListView> listViews;
        public bool CheckListInited()
        {
            for (int i = 0; i < listViews.Count; i++)
            {
                ListView listView = listViews[i];
                if (listView.userData is not bool)
                {
                    return false;
                }
            }
            return true;
        }
        public void AddChildNodesUntilListInited()
        {
            listViews = Content.Query<ListView>().ToList();
            IVisualElementScheduledItem visualElementScheduledItem = schedule.Execute(AddChildNodes).Until(CheckListInited);
        }





        public HashSet<ChildPort> visitedChildPorts = new();
        public void AddChildNodes()
        {
            //Debug.Log("AddChildNodes"+ CheckListInited());
            for (int i = 0; i < ChildPorts.Count; i++)
            {
                ChildPort childPort = ChildPorts[i];
                if (visitedChildPorts.Contains(childPort))
                {
                    continue;
                }
                visitedChildPorts.Add(childPort);
                InitChildPort(childPort);
            }
        }
        public void InitChildPort(ChildPort childPort)
        {
            List<JsonNode> child = childPort.GetChildValues();
            for (int j = 0; j < child.Count; j++)
            {
                ViewNode childNode = View.AddViewNode(child[j], childPort);
                if (childPort is MultiPort) { childNode.ParentPort.SetIndex(j); }
            }
        }


        public string GetNodePath()
        {
            ViewNode parentNode = GetParent();
            if (parentNode == null)
            {
                int index = View.Asset.Data.Nodes.IndexOf(Data);
                return $"[{index}]";
            }
            ChildPort childPort = ParentPort.connections.First().ChildPort();

            PropertyElement element = childPort.GetFirstAncestorOfType<PropertyElement>();
            string path = element.LocalPath;
            if (childPort is NumPort)
            {
                path = $"{path}.Node";
            }
            else if (childPort is MultiPort)
            {
                path = $"{path}[{ParentPort.Index}]";
            }
            return $"{parentNode.GetNodePath()}.{path}";
        }

        public int GetIndex()
        {
            return View.Asset.Data.Nodes.IndexOf(Data);
        }




        public void MakeDirty()
        {
            View.MakeDirty();
        }

        public List<ViewNode> GetChildNodes()
        {
            List<ViewNode> nodes = new();
            List<ChildPort> childPorts = ChildPorts.Where(n => n.connected).OrderBy(n => n.worldBound.position.y).ToList();
            for (int i = 0; i < childPorts.Count; i++)
            {
                foreach (var item in childPorts[i].connections)
                {
                    nodes.Add(item.ParentPort().node);
                }
            }

            return nodes;

        }






        public HashSet<ShowIfElement> ShowIfElements = new();

        public void OnChange()
        {
            foreach (var item in ShowIfElements)
            {
                item.Refresh();
            }
        }


    }
}
