using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Pool;
using UnityEngine.UIElements;
using Timer = TreeNode.Utility.Timer;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView : GraphView
    {
        public JsonAsset Asset => Window.JsonAsset;
        public TreeNodeGraphWindow Window;
        public TreeNodeWindowSearchProvider SearchProvider;
        public VisualElement ViewContainer;
        protected ContentZoomer m_Zoomer;


        #region 构造函数和初始化

        public TreeNodeGraphView(TreeNodeGraphWindow window)
        {
            Window = window;
            style.flexGrow = 1;

            // 立即初始化逻辑层树结构
            //InitializeNodeTreeSync();

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
            window.RemoveChangeMark();
        }

        #endregion
        #region 右键菜单和用户交互

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            //Debug.Log(evt.target.GetType());
            if (evt.target is PrefabViewNode node)
            {
                evt.menu.AppendAction(I18n.Editor.List.Goto, (d) => { node.OpenPrefabAsset(); }, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is GraphView && nodeCreationRequest != null)
            {
                evt.menu.AppendAction(I18n.Editor.Menu.CreateNode, OnContextMenuNodeCreate, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is PropertyElement element)
            {
                evt.menu.AppendAction(I18n.Editor.Menu.PrintFieldPath, delegate
                {
                    string path = element.GetGlobalPath();
                    Debug.Log(path);
                });
                evt.menu.AppendSeparator();
            }
            if (evt.target is ViewNode viewNode)
            {
                evt.menu.AppendAction(I18n.Editor.Menu.EditNode, delegate
                {
                    EditNodeScript(viewNode.Data.GetType());
                });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction(I18n.Editor.Menu.PrintNodePath, delegate
                {
                    Debug.Log(viewNode.GetNodePath());
                });
                evt.menu.AppendSeparator();
            }


            if (evt.target is GraphView || evt.target is Node || evt.target is Group || evt.target is Edge)
            {
                evt.menu.AppendAction(I18n.Editor.Menu.Delete, delegate
                {
                    DeleteSelectionCallback(AskUser.DontAskUser);
                }, (DropdownMenuAction a) => canDeleteSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is GraphView)
            {
                evt.menu.AppendAction(I18n.Editor.Menu.Format, FormatAllNodes, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendAction(I18n.Editor.Menu.ShowTreeView, ShowTreeView, DropdownMenuAction.AlwaysEnabled);
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
            System.Text.RegularExpressions.Regex classRegex = new($@"\bclass\s+{typeName}\b");

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
        }

        private void ShowTreeView(DropdownMenuAction a)
        {
            string treeView = GetTreeView();
            Debug.Log(treeView);
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

        private void ShowSearchWindow(NodeCreationContext context)
        {
            SearchProvider.Target = (VisualElement)focusController.focusedElement;
            SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), SearchProvider);
        }

        #endregion
        #region 资源管理和保存

        public void MakeDirty()
        {
            Window.MakeDirty();
        }

        public void SaveAsset()
        {
            Asset.Data.Nodes = Asset.Data.Nodes.Distinct().ToList();
            File.WriteAllText(Window.Path, Json.ToJson(Asset));
        }

        public virtual void OnSave() { }

        #endregion

        #region 渲染系统

        /// <summary>
        /// 节点渲染
        /// </summary>
        private void DrawNodes()
        {
            using (new Timer($"Draw [{Window.Title}]"))
            {
                for (int i = 0; i < Asset.Data.Nodes.Count; i++)
                {
                    PAPath path = PAPath.Index(i);
                    DrawNode(Asset.Data.Nodes[i], path);

                }
            }

        }

        ViewNode DrawNode(JsonNode node, PAPath path)
        {
            ViewNode viewNode;
            //if (node.PrefabData != null)
            //{
            //    viewNode = new PrefabViewNode(node, this);
            //}
            //else
            {
                viewNode = new(node, this);
            }
            ViewNodes.Add(viewNode);
            NodeDic.Add(node, viewNode);
            AddElement(viewNode);

            var list = node.CollectNodes(path, 1);
            for (int i = 0; i < list.Count; i++)
            {
                PAPath childPath = list[i].Item1;
                ViewNode childNode = DrawNode(list[i].Item2, childPath);
                PAPath local = PAPath.GetRelativePath(childPath,path);
                ChildPort childPort =  viewNode.GetChildPort(local);
                //Debug.Log($"childPort{childPort}({childPath}) ->childNode:{childNode}.ParentPort{childNode.ParentPort}");
                var edge = childPort.ConnectTo(childNode.ParentPort);
                AddElement(edge);
                if (childPath.ItemOfCollection)
                {
                    childNode.ParentPort.SetIndex(childPath.LastPart.Index);
                }
            }
            list.Release();
            return viewNode;
        }
        public virtual void ApplyChanges(List<ViewChange> changes)
        {
            //Debug.Log($"应用 {changes.Count} 个变化");
            if (changes == null || changes.Count == 0) { return; }
            ViewNode viewNode;
            for (int i = 0; i < changes.Count; i++)
            {
                //Debug.Log(changes[i].ChangeType);
                switch (changes[i].ChangeType)
                {
                    case ViewChangeType.NodeCreate:
                        CreateViewNodeSafe(changes[i].Node);
                        break;
                    case ViewChangeType.NodeDelete:
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode))
                        {
                            RemoveElement(viewNode);
                            ViewNodes.Remove(viewNode);
                            NodeDic.Remove(changes[i].Node);
                        }
                        break;
                    case ViewChangeType.NodeField:
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode))
                        {
                            if (changes[i].Path == PAPath.Position)
                            {
                                Rect rect = viewNode.GetPosition();
                                rect.position = viewNode.Data.Position;
                                viewNode.SetPosition(rect);
                            }
                            else
                            {
                                viewNode.RefreshPropertyElements(changes[i].Path);
                            }
                        }
                        break;
                    case ViewChangeType.EdgeCreate:
                        PAPath path = changes[i].Path;
                        int index = 0;
                        List<(int depth,JsonNode node)> list = Utility.ListPool <(int , JsonNode )>.GetList();
                        Asset.Data.Nodes.GetAllInPath(ref path, ref index, list);
                        JsonNode parentJsonNode = list[^2].node;
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode) && NodeDic.TryGetValue(parentJsonNode, out ViewNode parentNode))
                        {
                            PAPath local = path.GetSubPath(list[^2].depth + 1);
                            ChildPort childPort = parentNode.GetChildPort(local);
                            Edge edge = childPort.ConnectTo(viewNode.ParentPort);
                            childPort.OnAddEdge(edge);
                            AddElement(edge);
                        }
                        break;
                    case ViewChangeType.EdgeDelete:
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode))
                        {
                            if (viewNode.ParentPort.connected)
                            {
                                Edge edge = viewNode.ParentPort.connections.First();
                                viewNode.ParentPort.DisconnectAll();
                                edge.ChildPort().OnRemoveEdge(edge);
                                RemoveElement(edge);
                            }
                        }
                        break;
                    case ViewChangeType.ListItem:
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode))
                        {
                            viewNode.RefreshList(changes[i]);
                        }
                        break;
                }
            }
        }


        /// <summary>
        /// 安全创建ViewNode
        /// </summary>
        private void CreateViewNodeSafe(JsonNode node)
        {
            if (NodeDic.ContainsKey(node))
            {
                return; // 已存在，跳过
            }

            try
            {
                ViewNode viewNode;
                if (node.PrefabData != null)
                {
                    viewNode = new PrefabViewNode(node, this);
                }
                else
                {
                    viewNode = new ViewNode(node, this);
                }

                ViewNodes.Add(viewNode);
                NodeDic.Add(node, viewNode);
                AddElement(viewNode);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"创建ViewNode失败 {node?.GetType().Name}: {e.Message}");
            }
        }
        /// <summary>
        /// 在主线程执行
        /// </summary>
        private async Task ExecuteOnMainThreadAsync(Action action)
        {
            // Unity 编辑器中的主线程执行优化
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == 1)
            {
                // 已经在主线程，直接执行
                action();
                return;
            }

            // 使用TaskCompletionSource在主线程执行
            var tcs = new TaskCompletionSource<bool>();

            schedule.Execute(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            await tcs.Task;
        }

        /// <summary>
        /// 优化的重绘方法 - 支持增量和全量渲染
        /// </summary>
        public void Redraw()
        {
            // 清理现有视图
            ViewNodes.Clear();
            NodeDic.Clear();
            ViewContainer.Query<Layer>().ForEach(p => p.Clear());

            // 重新初始化逻辑层
            //InitializeNodeTreeSync();

            // 启动异步重新渲染
            DrawNodes();
            Window.RemoveChangeMark();
        }
        #endregion

        public Vec2 GetMousePosition()
        {
            Vector2 mousePositionInWindow = Mouse.current.position.ReadValue()/ EditorGUIUtility.pixelsPerPoint;
            mousePositionInWindow.y -= 24;
            mousePositionInWindow /= scale;
            Vector2 vector2 = ViewContainer.localBound.position;
            vector2 /= scale;
            var graphMousePosition = mousePositionInWindow - vector2;
            return graphMousePosition;
        }
        
    }

    /// <summary>
    /// 节点预准备数据结构
    /// </summary>
    public class NodePrepData
    {
        public JsonNode Node { get; set; }
        public Type NodeType { get; set; }
        public bool IsPrefab { get; set; }
        public BaseDrawer Drawer { get; set; }
        public NodeInfoAttribute NodeInfo { get; set; }
        public Vec2 Position { get; set; }
    }
}
