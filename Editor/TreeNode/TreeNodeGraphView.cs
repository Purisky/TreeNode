using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.SocialPlatforms;
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
            //Debug.Log(evt.target.GetType());
            if (evt.target is PrefabViewNode node)
            {
                evt.menu.AppendAction(I18n.Goto, (d) => { node.OpenPrefabAsset(); }, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is GraphView && nodeCreationRequest != null)
            {
                evt.menu.AppendAction(I18n.CreateNode, OnContextMenuNodeCreate, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is PropertyElement element)
            {
                evt.menu.AppendAction(I18n.PrintFieldPath, delegate
                {
                    string path = element.GetGlobalPath();
                    Debug.Log(path);
                });
                evt.menu.AppendSeparator();
            }
            if (evt.target is ViewNode viewNode)
            {
                evt.menu.AppendAction(I18n.EditNode, delegate
                {
                    EditNodeScript(viewNode.Data.GetType());
                });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction(I18n.PrintNodePath, delegate
                {
                    Debug.Log(viewNode.GetNodePath());
                });
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
                evt.menu.AppendAction("Show Tree View", ShowTreeView, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
        }

        /// <summary>
        /// Open the script editor
        /// </summary>
        /// <param name="type"></param>
        void EditNodeScript(Type type)
        {
            string typeName = type.Name;
            string[] guids = AssetDatabase.FindAssets("t:Script a:assets");
            System.Text.RegularExpressions.Regex classRegex = new System.Text.RegularExpressions.Regex($@"\bclass\s+{typeName}\b");

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string[] fileLines = File.ReadAllLines(assetPath);

                for (int i = 0; i < fileLines.Length; i++)
                {
                    if (classRegex.IsMatch(fileLines[i]))
                    {
                        // 打开脚本文件并定位到类定义的行
                        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath), i + 1);
                        return;
                    }
                }
            }

            Debug.LogError($"Script for {type.Name} not found.");
        }



        private void FormatAllNodes(DropdownMenuAction a)
        {
            FormatNodes();
            Window.History.AddStep();
        }

        private void ShowTreeView(DropdownMenuAction a)
        {
            string treeView = GetTreeView();
            Debug.Log("Node Tree View:\n" + treeView);
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

        public bool SetNodeByPath(JsonNode node, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Asset.Data.Nodes.Add(node);
                return true;
            }
            try
            {
                object parent = PropertyAccessor.GetParentObject(Asset.Data.Nodes, path, out string last);
                object oldValue = PropertyAccessor.GetValue<object>(parent, last);
                if (oldValue is null)
                {
                    Type parentType = parent.GetType();
                    Type valueType = parentType.GetMember(last).First().GetValueType();
                    if (valueType.Inherited(typeof(IList)))
                    {
                        oldValue = Activator.CreateInstance(valueType);
                        PropertyAccessor.SetValue(parent, last, oldValue);
                    }
                }
                if (oldValue is IList list)
                {
                    list.Add(node);
                }
                else
                {
                    PropertyAccessor.SetValue(parent, last, node);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error setting node by path: {e.Message}");
                return false;
            }
        }






        void RemoveNode(JsonNode node)
        {
            Asset.Data.Nodes.Remove(node);
        }
        public ViewNode AddViewNode(JsonNode node, ChildPort childPort = null)
        {
            if (NodeDic.TryGetValue(node, out ViewNode viewNode)) { return viewNode; }
            ;
            if (node.PrefabData != null)
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
            
            // Use enhanced initialization that prefers synchronous approach
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

        public PropertyElement Find(string path)
        {
            JsonNode node = PropertyAccessor.GetLast<JsonNode>(Asset.Data.Nodes, path, false, out int index);
            if (node is null) { return null; }
            if (index >= path.Length - 1) { return null; }
            string local = path[index..];
            return NodeDic[node]?.FindByLocalPath(local);
        }
        public ChildPort GetPort(string path) => Find(path)?.Q<ChildPort>();

        public virtual string Validate()
        {
            string result = "";
            for (int i = 0; i < ViewNodes.Count; i++)
            {
                if (!ViewNodes[i].Validate(out string msg))
                {
                    result += i > 0 ? ("\n" + msg) : msg;
                }
            }
            if (result.Length > 0)
            {
                return result;
            }
            return "Success";
        }

        public List<ViewNode> GetSortNodes()
        {
            List<ViewNode> sortNodes = new();

            // Create a list to hold all nodes with their sorting keys
            List<(ViewNode node, int rootIndex, float parentPortY, int listIndex)> nodesToSort = new();

            foreach (ViewNode viewNode in ViewNodes)
            {
                int rootIndex = -1;
                float parentPortY = 0f;
                int listIndex = 0;

                // Rule 1: Root nodes by their index in Asset.Data.Nodes
                if (viewNode.ParentPort == null || !viewNode.ParentPort.connected)
                {
                    // This is a root node
                    rootIndex = Asset.Data.Nodes.IndexOf(viewNode.Data);
                }
                else
                {
                    // This is a child node, find its root and get the root's index
                    ViewNode rootNode = viewNode.GetRoot();
                    rootIndex = Asset.Data.Nodes.IndexOf(rootNode.Data);

                    // Rule 2: Connected parent node Port's Y position
                    Edge edge = viewNode.ParentPort.connections.First();
                    ChildPort parentChildPort = edge.ChildPort();
                    parentPortY = parentChildPort.worldBound.position.y;

                    // Rule 3: Index in the list (for MultiPort)
                    if (parentChildPort is MultiPort)
                    {
                        listIndex = viewNode.ParentPort.Index;
                    }
                }

                nodesToSort.Add((viewNode, rootIndex, parentPortY, listIndex));
            }

            // Sort by the three criteria in order
            var sortedNodes = nodesToSort.OrderBy(x => x.rootIndex)
                                        .ThenBy(x => x.parentPortY)
                                        .ThenBy(x => x.listIndex)
                                        .Select(x => x.node)
                                        .ToList();

            return sortedNodes;
        }
        
        public virtual List<(string, string)> GetAllNodePaths()
        {
            List<(string, string)> paths = new();
            foreach (var node in GetSortNodes())
            {
                string path = node.GetNodePath();
                paths.Add((node.GetNodePath(), $"{path}--{node.Data.GetType().Name}"));
            }
            paths = paths.OrderBy(n => n.Item1).ToList();
            return paths;
        }

        /// <summary>
        /// Get total count of all JsonNodes including nested ones
        /// </summary>
        public int GetTotalJsonNodeCount()
        {
            HashSet<JsonNode> allNodes = new HashSet<JsonNode>();
            
            // Start with root nodes
            foreach (JsonNode rootNode in Asset.Data.Nodes)
            {
                CollectAllJsonNodesRecursively(rootNode, allNodes);
            }
            
            return allNodes.Count;
        }

        /// <summary>
        /// Recursively collect all JsonNodes including nested ones
        /// </summary>
        private void CollectAllJsonNodesRecursively(JsonNode node, HashSet<JsonNode> collected)
        {
            if (node == null || collected.Contains(node))
                return;
                
            collected.Add(node);
            
            // Use reflection to find all JsonNode properties and collections
            Type nodeType = node.GetType();
            var fields = nodeType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var properties = nodeType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            // Check fields
            foreach (var field in fields)
            {
                CollectFromMember(field.FieldType, field.GetValue(node), collected);
            }
            
            // Check properties
            foreach (var property in properties)
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        CollectFromMember(property.PropertyType, property.GetValue(node), collected);
                    }
                    catch
                    {
                        // Skip properties that can't be accessed
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to collect JsonNodes from a member value
        /// </summary>
        private void CollectFromMember(Type memberType, object value, HashSet<JsonNode> collected)
        {
            if (value == null) return;
            
            // Direct JsonNode
            if (memberType.IsSubclassOf(typeof(JsonNode)) && value is JsonNode jsonNode)
            {
                CollectAllJsonNodesRecursively(jsonNode, collected);
            }
            // List/Array of JsonNodes
            else if (value is IEnumerable enumerable && !typeof(string).IsAssignableFrom(memberType))
            {
                foreach (var item in enumerable)
                {
                    if (item is JsonNode childNode)
                    {
                        CollectAllJsonNodesRecursively(childNode, collected);
                    }
                }
            }
        }

        /// <summary>
        /// Check if all ViewNodes are fully initialized (all child nodes have been added)
        /// </summary>
        public bool AreAllViewNodesInitialized()
        {
            // Get the total expected number of JsonNodes
            int expectedNodeCount = GetTotalJsonNodeCount();
            
            // Check if we have the expected number of ViewNodes
            if (ViewNodes.Count < expectedNodeCount)
            {
                return false;
            }

            // Check if all existing ViewNodes are fully initialized
            foreach (ViewNode viewNode in ViewNodes)
            {
                if (!viewNode.CheckListInited())
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Wait for all ViewNodes to be fully initialized, then execute the callback
        /// </summary>
        public void WaitForViewNodesInitialized(System.Action callback)
        {
            if (AreAllViewNodesInitialized())
            {
                callback?.Invoke();
            }
            else
            {
                schedule.Execute(() =>
                {
                    if (AreAllViewNodesInitialized())
                    {
                        callback?.Invoke();
                    }
                    else
                    {
                        // Schedule to check again in the next frame
                        WaitForViewNodesInitialized(callback);
                    }
                });
            }
        }
        public virtual string GetTreeView()
        {
            if (ViewNodes.Count == 0)
                return "No nodes found";

            var sortedNodes = GetSortNodes();
            var treeBuilder = new System.Text.StringBuilder();
            var processedNodes = new HashSet<ViewNode>();
            
            // Find all root nodes (nodes without parent connections)
            var rootNodes = new List<ViewNode>();
            foreach (var node in sortedNodes)
            {
                if (node.ParentPort == null || !node.ParentPort.connected)
                {
                    rootNodes.Add(node);
                }
            }
            
            // Process each root node and its descendants
            for (int i = 0; i < rootNodes.Count; i++)
            {
                var root = rootNodes[i];
                if (processedNodes.Contains(root))
                    continue;
                
                // Add separator line between different root trees (except for the first tree)
                if (i > 0)
                {
                    treeBuilder.AppendLine();
                }
                    
                BuildTreeRecursive(root, "", true, treeBuilder, processedNodes, true);
            }
            
            return treeBuilder.ToString().TrimEnd();
        }
        private void BuildTreeRecursive(ViewNode node, string prefix, bool isLast, System.Text.StringBuilder builder, HashSet<ViewNode> processedNodes, bool isRoot = false)
        {
            if (processedNodes.Contains(node))
                return;
                
            processedNodes.Add(node);
            
            // Get the display name for the node
            string nodeName = node.Data.GetInfo();
            
            // Add the current node to the tree
            if (isRoot)
            {
                // Root nodes don't have tree characters, just the name
                builder.AppendLine(nodeName);
            }
            else
            {
                builder.AppendLine(prefix + (isLast ? "└── " : "├── ") + nodeName);
            }
            
            // Get child nodes sorted by Y position
            var children = node.GetChildNodes();
            
            // Build tree recursively for children
            for (int i = 0; i < children.Count; i++)
            {
                bool isLastChild = (i == children.Count - 1);
                string childPrefix = prefix + (isRoot ? "" : (isLast ? "    " : "│   "));
                BuildTreeRecursive(children[i], childPrefix, isLastChild, builder, processedNodes, false);
            }
        }
    }
}
