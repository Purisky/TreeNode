using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView : GraphView
    {
        public JsonAsset Asset => Window.JsonAsset;
        public TreeNodeGraphWindow Window;

        public List<ViewNode> ViewNodes;
        public Dictionary<JsonNode, ViewNode> NodeDic;

        public TreeNodeWindowSearchProvider SearchProvider;
        public VisualElement ViewContainer;
        protected ContentZoomer m_Zoomer;
        public TreeNodeGraphView(TreeNodeGraphWindow window)
        {
            Window = window;
            style.flexGrow = 1;
            StyleSheet styleSheet = ResourcesUtil.LoadStyleSheet("TreeNodeGraphView");
            styleSheets.Add(styleSheet);
            ViewNodes = new();
            NodeDic = new();
            SearchProvider = ScriptableObject.CreateInstance<TreeNodeWindowSearchProvider>();
            SearchProvider.Graph = this;
            this.nodeCreationRequest = ShowSearchWindow;
            graphViewChanged = OnGraphViewChanged;
            


            ViewContainer = this.Q("contentViewContainer");
            GridBackground background = new()
            {
                name = "Grid",
            };
            Insert(0, background);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());

            DrawNodes();

            SetupZoom(0.2f, 2f);
            canPasteSerializedData = CanPaste;
            serializeGraphElements = Copy;
            unserializeAndPaste = Paste;
            
        }

        public virtual string Copy(IEnumerable<GraphElement> elements)
        {
            return "";
        }
        public virtual bool CanPaste(string data)
        {
            return false;
        }

        public virtual void Paste(string operationName, string data)
        {

        }




        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is PrefabViewNode node)
            {
                evt.menu.AppendAction(I18n.Goto,(d)=> { node.OpenPrefabAsset(); }, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is GraphView && nodeCreationRequest != null)
            {
                evt.menu.AppendAction(I18n.CreateNode, OnContextMenuNodeCreate, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is GraphView || evt.target is Node || evt.target is Group || evt.target is Edge)
            {
                evt.menu.AppendAction(I18n.Delete, delegate
                {
                    DeleteSelectionCallback(AskUser.DontAskUser);
                }, (DropdownMenuAction a) => canDeleteSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is GraphView)
            {
                evt.menu.AppendAction(I18n.Format, FormatAllNodes, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            


        }

        private void FormatAllNodes(DropdownMenuAction a)
        {
            FormatNodes();
            Window.History.AddStep();
        }


        private void OnContextMenuNodeCreate(DropdownMenuAction a)
        {
            RequestNodeCreation(null, -1, a.eventInfo.mousePosition);
        }
        private void RequestNodeCreation(VisualElement target, int index, Vector2 position)
        {
            if (nodeCreationRequest != null)
            {
                Vector2 screenMousePosition = Window.position.position + position;
                nodeCreationRequest(new NodeCreationContext
                {
                    screenMousePosition = screenMousePosition,
                    target = target,
                    index = index
                });
            }
        }


        public void Redraw()
        {
            ViewNodes.Clear();
            NodeDic.Clear();
            ViewContainer.Query<Layer>().ForEach(p => p.Clear());
            schedule.Execute(DrawNodes);
        }


        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (graphViewChange.elementsToRemove != null)
            {
                IEnumerable<ViewNode> nodes = graphViewChange.elementsToRemove.OfType<ViewNode>().Reverse();
                List<Edge> temp = new();
                if (nodes.Any())
                {
                    foreach (ViewNode viewNode in nodes)
                    {
                        temp.AddRange(viewNode.GetAllEdges());
                    }
                }
                graphViewChange.elementsToRemove.AddRange(temp);
                IEnumerable<Edge> edges = graphViewChange.elementsToRemove.OfType<Edge>().Distinct();
                if (edges.Any())
                {
                    foreach (Edge edge in edges)
                    {
                        RemoveEdge(edge);
                    }
                }
                foreach (ViewNode viewNode in nodes)
                {
                    RemoveViewNode(viewNode);
                }
            }
            if (graphViewChange.edgesToCreate != null)
            {
                foreach (Edge edge in graphViewChange.edgesToCreate)
                {
                    CreateEdge(edge);
                }
            }
            Window.History.AddStep();
            return graphViewChange;
        }





        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> allPorts = new();
            List<Port> ports = new();


            foreach (var node in ViewNodes)
            {
                if (node == startPort.node) { continue; }

                if (startPort.direction == Direction.Output)
                {
                    if (node.ParentPort != null)
                    {
                        allPorts.Add(node.ParentPort);
                    }
                }
                else
                {
                    allPorts.AddRange(node.ChildPorts);
                }
            }





            ports = allPorts.Where(x => CheckPort(startPort, x)).ToList();
            return ports;
        }

        public virtual bool CheckPort(Port start, Port end)
        {
            if (start.portType != end.portType) { return false; }
            ParentPort parentPort;
            ChildPort childPort;
            if (start is ParentPort)
            {
                parentPort = start as ParentPort;
                childPort = end as ChildPort;
            }
            else
            {
                parentPort = end as ParentPort;
                childPort = start as ChildPort;
            }
            return CheckMulti(parentPort, childPort) && CheckLoop(childPort.node, parentPort.node);
        }

        public bool CheckMulti(ParentPort parentPort, ChildPort childPort)
        {
            return !parentPort.Collection || childPort is MultiPort;
        }


        public bool CheckLoop(ViewNode parent, ViewNode child)
        {
            ViewNode node = parent;
            while (node != null)
            {
                if (node == child) { return false; }
                node = node.GetParent();
            }
            return true;
        }



        public virtual void CreateEdge(Edge edge)
        {
            //Debug.Log("CreateEdge");
            ViewNode childNode = edge.ParentPort().node;
            Asset.Data.Nodes.Remove(childNode.Data);
            ChildPort childport_of_parent = edge.ChildPort();
            childport_of_parent.SetNodeValue(childNode.Data, false);
            edge.ParentPort().Connect(edge);
            childport_of_parent.Connect(edge);
            childport_of_parent.OnAddEdge(edge);
        }
        public virtual void RemoveEdge(Edge edge)
        {
            ViewNode parent = edge.ChildPort()?.node;
            ViewNode child = edge.ParentPort()?.node;
            if (parent == null || child == null) { return; }
            ChildPort childport_of_parent = edge.ChildPort();
            childport_of_parent.SetNodeValue(child.Data);
            Asset.Data.Nodes.Add(child.Data);
            edge.ParentPort().DisconnectAll();
            childport_of_parent.OnRemoveEdge(edge);
        }




        private void ShowSearchWindow(NodeCreationContext context)
        {
            SearchProvider.Target = (VisualElement)focusController.focusedElement;
            SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), SearchProvider);


        }


        public virtual void DrawNodes()
        {
            for (int i = 0; i < Asset.Data.Nodes.Count; i++)
            {
                AddViewNode(Asset.Data.Nodes[i]);
            }
        }

        public virtual void AddNode(JsonNode node)
        {
            Asset.Data.Nodes.Add(node);
            Window.History.AddStep();
            AddViewNode(node);
        }
        void RemoveNode(JsonNode node)
        {
            Asset.Data.Nodes.Remove(node);
        }
        public ViewNode AddViewNode(JsonNode node,ChildPort childPort = null)
        {
            if (NodeDic.TryGetValue(node, out ViewNode viewNode)) { return viewNode; };
            if (node.PrefabData!=null)
            {
                viewNode = new PrefabViewNode(node, this, childPort);
            }
            else
            {
                viewNode = new(node, this, childPort);
            }
            viewNode.SetPosition(new Rect(node.Position, new()));
            ViewNodes.Add(viewNode);
            NodeDic.Add(node, viewNode);
            AddElement(viewNode);
            viewNode.AddChildNodesUntilListInited();
            return viewNode;


        }

        public virtual void OnSave() { }


        public virtual void RemoveViewNode(ViewNode node)
        {
            RemoveNode(node.Data);
            ViewNodes.Remove(node);
            NodeDic.Remove(node.Data);
        }

        public void MakeDirty()
        {
            Window.MakeDirty();
        }


        public void SaveAsset()
        {
            Asset.Data.Nodes = Asset.Data.Nodes.Distinct().ToList();
            File.WriteAllText(Window.Path, Json.ToJson(Asset));
        }
    }

}
