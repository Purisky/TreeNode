using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    /// <summary>
    /// 基于增量变化的历史记录管理器
    /// 使用命令模式记录操作，支持高效的Undo/Redo
    /// </summary>
    public class History
    {
        const int MaxStep = 50; // 增加历史步数，因为增量模式内存占用更少
        
        TreeNodeGraphWindow Window;
        List<IHistoryCommand> Commands = new();
        int CurrentIndex = -1; // 当前命令索引，-1表示初始状态
        
        // 缓存初始状态用于完全重建
        private string InitialStateJson;

        public History(TreeNodeGraphWindow window)
        {
            Window = window;
            // 保存初始状态
            InitialStateJson = Json.ToJson(Window.JsonAsset);
        }

        public void Clear()
        {
            Commands.Clear();
            CurrentIndex = -1;
            // 更新初始状态
            InitialStateJson = Json.ToJson(Window.JsonAsset);
        }

        /// <summary>
        /// 添加新的历史步骤
        /// </summary>
        /// <param name="command">要执行的命令</param>
        /// <param name="dirty">是否标记为脏数据</param>
        public void AddCommand(IHistoryCommand command, bool dirty = true)
        {
            if (command == null) return;

            if (dirty)
            {
                Window.MakeDirty();
            }

            // 清除当前索引之后的所有命令（分支历史）
            if (CurrentIndex < Commands.Count - 1)
            {
                Commands.RemoveRange(CurrentIndex + 1, Commands.Count - CurrentIndex - 1);
            }

            // 添加新命令
            Commands.Add(command);
            CurrentIndex++;

            // 限制历史步数
            if (Commands.Count > MaxStep)
            {
                Commands.RemoveAt(0);
                CurrentIndex--;
                // 如果删除了历史记录，需要更新初始状态
                if (Commands.Count > 0)
                {
                    RebuildInitialState();
                }
            }

            Debug.Log($"History: Added command {command.GetType().Name}, total: {Commands.Count}, current: {CurrentIndex}");
        }

        /// <summary>
        /// 兼容旧接口的方法，创建完整状态快照命令
        /// </summary>
        /// <param name="dirty">是否标记为脏数据</param>
        public void AddStep(bool dirty = true)
        {
            var command = new FullStateCommand(Window.JsonAsset);
            AddCommand(command, dirty);
        }

        public bool Undo()
        {
            if (CurrentIndex < 0) return false;

            Debug.Log($"Undo: Current index {CurrentIndex}");
            
            try
            {
                var command = Commands[CurrentIndex];
                command.Undo(Window.JsonAsset);
                CurrentIndex--;
                
                Window.GraphView.Redraw();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Undo failed: {ex.Message}");
                // 尝试重建状态
                RebuildCurrentState();
                return false;
            }
        }

        public bool Redo()
        {
            if (CurrentIndex >= Commands.Count - 1) return false;

            Debug.Log($"Redo: Current index {CurrentIndex}");

            try
            {
                CurrentIndex++;
                var command = Commands[CurrentIndex];
                command.Redo(Window.JsonAsset);
                
                Window.GraphView.Redraw();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Redo failed: {ex.Message}");
                CurrentIndex--; // 回滚索引
                RebuildCurrentState();
                return false;
            }
        }

        /// <summary>
        /// 重建当前状态（用于错误恢复）
        /// </summary>
        private void RebuildCurrentState()
        {
            try
            {
                // 从初始状态开始重建
                Window.JsonAsset = Json.Get<JsonAsset>(InitialStateJson);
                
                // 依次应用所有命令直到当前索引
                for (int i = 0; i <= CurrentIndex && i < Commands.Count; i++)
                {
                    Commands[i].Redo(Window.JsonAsset);
                }
                
                Window.GraphView.Redraw();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to rebuild state: {ex.Message}");
            }
        }

        /// <summary>
        /// 重建初始状态（当删除历史记录时）
        /// </summary>
        private void RebuildInitialState()
        {
            try
            {
                // 从原始初始状态开始
                var tempAsset = Json.Get<JsonAsset>(InitialStateJson);
                
                // 应用已删除的命令来更新初始状态
                if (Commands.Count > 0)
                {
                    Commands[0].Redo(tempAsset);
                    InitialStateJson = Json.ToJson(tempAsset);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to rebuild initial state: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取历史信息（用于调试）
        /// </summary>
        public string GetHistoryInfo()
        {
            var info = $"History Commands: {Commands.Count}, Current Index: {CurrentIndex}\n";
            for (int i = 0; i < Commands.Count; i++)
            {
                var marker = i == CurrentIndex ? " -> " : "    ";
                info += $"{marker}[{i}] {Commands[i].GetType().Name}: {Commands[i].Description}\n";
            }
            return info;
        }

        /// <summary>
        /// 获取Window的内部访问方法
        /// </summary>
        internal TreeNodeGraphWindow GetWindow() => Window;
    }

    /// <summary>
    /// 历史命令接口
    /// </summary>
    public interface IHistoryCommand
    {
        string Description { get; }
        void Undo(JsonAsset asset);
        void Redo(JsonAsset asset);
    }

    /// <summary>
    /// 全状态快照命令（兼容原有实现）
    /// </summary>
    public class FullStateCommand : IHistoryCommand
    {
        private string beforeJson;
        private string afterJson;

        public string Description => "Full State Snapshot";

        public FullStateCommand(JsonAsset currentState)
        {
            afterJson = Json.ToJson(currentState);
        }

        public void Undo(JsonAsset asset)
        {
            if (beforeJson == null) return;
            
            var beforeState = Json.Get<JsonAsset>(beforeJson);
            asset.Data = beforeState.Data;
        }

        public void Redo(JsonAsset asset)
        {
            // 在首次执行时保存before状态
            if (beforeJson == null)
            {
                beforeJson = Json.ToJson(asset);
            }
            
            var afterState = Json.Get<JsonAsset>(afterJson);
            asset.Data = afterState.Data;
        }
    }

    /// <summary>
    /// 节点添加命令
    /// </summary>
    public class AddNodeCommand : IHistoryCommand
    {
        private JsonNode node;
        private string nodePath;
        private int nodeIndex = -1; // 用于列表中的位置

        public string Description => $"Add Node: {node?.GetType().Name}";

        public AddNodeCommand(JsonNode node, string nodePath = null)
        {
            this.node = node;
            this.nodePath = nodePath;
        }

        public void Undo(JsonAsset asset)
        {
            if (string.IsNullOrEmpty(nodePath))
            {
                asset.Data.Nodes.Remove(node);
            }
            else
            {
                RemoveNodeFromPath(asset, node, nodePath);
            }
        }

        public void Redo(JsonAsset asset)
        {
            if (string.IsNullOrEmpty(nodePath))
            {
                if (!asset.Data.Nodes.Contains(node))
                {
                    asset.Data.Nodes.Add(node);
                }
            }
            else
            {
                AddNodeToPath(asset, node, nodePath);
            }
        }

        private void AddNodeToPath(JsonAsset asset, JsonNode node, string path)
        {
            try
            {
                object parent = PropertyAccessor.GetParentObject(asset.Data.Nodes, path, out string last);
                object oldValue = PropertyAccessor.GetValue<object>(parent, last);
                
                if (oldValue is System.Collections.IList list)
                {
                    if (!list.Contains(node))
                    {
                        if (nodeIndex >= 0 && nodeIndex < list.Count)
                        {
                            list.Insert(nodeIndex, node);
                        }
                        else
                        {
                            list.Add(node);
                            nodeIndex = list.Count - 1;
                        }
                    }
                }
                else
                {
                    PropertyAccessor.SetValue(parent, last, node);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to add node to path {path}: {ex.Message}");
            }
        }

        private void RemoveNodeFromPath(JsonAsset asset, JsonNode node, string path)
        {
            try
            {
                object parent = PropertyAccessor.GetParentObject(asset.Data.Nodes, path, out string last);
                object value = PropertyAccessor.GetValue<object>(parent, last);
                
                if (value is System.Collections.IList list)
                {
                    nodeIndex = list.IndexOf(node);
                    list.Remove(node);
                }
                else if (ReferenceEquals(value, node))
                {
                    PropertyAccessor.SetValue<object>(parent, last, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to remove node from path {path}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 节点删除命令 - 增强版，正确处理嵌套节点的恢复
    /// </summary>
    public class RemoveNodeCommand : IHistoryCommand
    {
        private JsonNode node;
        private string nodePath;
        private int nodeIndex = -1;
        private NodeConnectionInfo connectionInfo; // 新增：连接信息

        public string Description => $"Remove Node: {node?.GetType().Name}";

        public RemoveNodeCommand(JsonNode node, string nodePath = null)
        {
            this.node = node;
            this.nodePath = nodePath;
            
            // 在删除前收集节点的连接信息
            CollectConnectionInfo();
        }

        /// <summary>
        /// 收集节点的连接信息，用于正确恢复
        /// </summary>
        private void CollectConnectionInfo()
        {
            // 这里需要通过JsonNodeTree获取节点的完整路径信息
            // 但由于我们在命令创建时可能无法访问到TreeNodeGraphView，
            // 我们需要另一种方式来收集这些信息
            
            connectionInfo = new NodeConnectionInfo();
            
            // 如果没有提供路径，说明这是根节点
            if (string.IsNullOrEmpty(nodePath))
            {
                connectionInfo.IsRootNode = true;
                connectionInfo.ParentPath = null;
                connectionInfo.PropertyName = null;
            }
            else
            {
                connectionInfo.IsRootNode = false;
                connectionInfo.FullPath = nodePath;
                
                // 解析路径以获取父路径和属性名
                var pathParts = nodePath.Split('.');
                if (pathParts.Length > 1)
                {
                    connectionInfo.ParentPath = string.Join(".", pathParts.Take(pathParts.Length - 1));
                    connectionInfo.PropertyName = pathParts[pathParts.Length - 1];
                }
            }
        }

        public void Undo(JsonAsset asset)
        {
            // Undo删除 = 重新添加到正确位置
            if (connectionInfo.IsRootNode)
            {
                // 恢复为根节点
                if (!asset.Data.Nodes.Contains(node))
                {
                    if (nodeIndex >= 0 && nodeIndex < asset.Data.Nodes.Count)
                    {
                        asset.Data.Nodes.Insert(nodeIndex, node);
                    }
                    else
                    {
                        asset.Data.Nodes.Add(node);
                    }
                }
            }
            else
            {
                // 恢复为子节点 - 需要重新建立到父节点的连接
                RestoreToParent(asset);
            }
        }

        public void Redo(JsonAsset asset)
        {
            // Redo删除 = 执行删除并记录位置信息
            if (connectionInfo.IsRootNode)
            {
                nodeIndex = asset.Data.Nodes.IndexOf(node);
                asset.Data.Nodes.Remove(node);
            }
            else
            {
                RemoveFromParent(asset);
            }
        }

        /// <summary>
        /// 将节点恢复到父节点的正确位置
        /// </summary>
        private void RestoreToParent(JsonAsset asset)
        {
            try
            {
                if (string.IsNullOrEmpty(connectionInfo.ParentPath) || string.IsNullOrEmpty(connectionInfo.PropertyName))
                    return;

                // 首先确保节点不在根节点列表中
                asset.Data.Nodes.Remove(node);

                // 获取父对象
                object parent = PropertyAccessor.GetValue<object>(asset.Data, connectionInfo.ParentPath);
                if (parent == null) return;

                // 获取目标属性
                var propertyInfo = parent.GetType().GetProperty(connectionInfo.PropertyName);
                var fieldInfo = parent.GetType().GetField(connectionInfo.PropertyName);
                
                if (propertyInfo != null)
                {
                    RestoreToProperty(parent, propertyInfo);
                }
                else if (fieldInfo != null)
                {
                    RestoreToField(parent, fieldInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to restore node to parent: {ex.Message}");
            }
        }

        /// <summary>
        /// 恢复到属性
        /// </summary>
        private void RestoreToProperty(object parent, System.Reflection.PropertyInfo propertyInfo)
        {
            var propertyType = propertyInfo.PropertyType;
            var currentValue = propertyInfo.GetValue(parent);

            if (propertyType.IsSubclassOf(typeof(JsonNode)))
            {
                // 直接JsonNode属性
                propertyInfo.SetValue(parent, node);
            }
            else if (typeof(System.Collections.IList).IsAssignableFrom(propertyType))
            {
                // 列表属性
                if (currentValue is System.Collections.IList list)
                {
                    if (nodeIndex >= 0 && nodeIndex < list.Count)
                    {
                        list.Insert(nodeIndex, node);
                    }
                    else
                    {
                        list.Add(node);
                    }
                }
                else
                {
                    // 创建新列表
                    var newList = Activator.CreateInstance(propertyType) as System.Collections.IList;
                    newList?.Add(node);
                    propertyInfo.SetValue(parent, newList);
                }
            }
        }

        /// <summary>
        /// 恢复到字段
        /// </summary>
        private void RestoreToField(object parent, System.Reflection.FieldInfo fieldInfo)
        {
            var fieldType = fieldInfo.FieldType;
            var currentValue = fieldInfo.GetValue(parent);

            if (fieldType.IsSubclassOf(typeof(JsonNode)))
            {
                // 直接JsonNode字段
                fieldInfo.SetValue(parent, node);
            }
            else if (typeof(System.Collections.IList).IsAssignableFrom(fieldType))
            {
                // 列表字段
                if (currentValue is System.Collections.IList list)
                {
                    if (nodeIndex >= 0 && nodeIndex < list.Count)
                    {
                        list.Insert(nodeIndex, node);
                    }
                    else
                    {
                        list.Add(node);
                    }
                }
                else
                {
                    // 创建新列表
                    var newList = Activator.CreateInstance(fieldType) as System.Collections.IList;
                    newList?.Add(node);
                    fieldInfo.SetValue(parent, newList);
                }
            }
        }

        /// <summary>
        /// 从父节点中移除
        /// </summary>
        private void RemoveFromParent(JsonAsset asset)
        {
            try
            {
                if (string.IsNullOrEmpty(connectionInfo.ParentPath) || string.IsNullOrEmpty(connectionInfo.PropertyName))
                    return;

                // 获取父对象
                object parent = PropertyAccessor.GetValue<object>(asset.Data, connectionInfo.ParentPath);
                if (parent == null) return;

                // 获取目标属性
                var propertyInfo = parent.GetType().GetProperty(connectionInfo.PropertyName);
                var fieldInfo = parent.GetType().GetField(connectionInfo.PropertyName);

                if (propertyInfo != null)
                {
                    RemoveFromProperty(parent, propertyInfo);
                }
                else if (fieldInfo != null)
                {
                    RemoveFromField(parent, fieldInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to remove node from parent: {ex.Message}");
            }
        }

        /// <summary>
        /// 从属性中移除
        /// </summary>
        private void RemoveFromProperty(object parent, System.Reflection.PropertyInfo propertyInfo)
        {
            var propertyType = propertyInfo.PropertyType;
            var currentValue = propertyInfo.GetValue(parent);

            if (propertyType.IsSubclassOf(typeof(JsonNode)) && ReferenceEquals(currentValue, node))
            {
                // 直接JsonNode属性
                propertyInfo.SetValue(parent, null);
            }
            else if (typeof(System.Collections.IList).IsAssignableFrom(propertyType) && currentValue is System.Collections.IList list)
            {
                // 列表属性
                nodeIndex = list.IndexOf(node);
                list.Remove(node);
            }
        }

        /// <summary>
        /// 从字段中移除
        /// </summary>
        private void RemoveFromField(object parent, System.Reflection.FieldInfo fieldInfo)
        {
            var fieldType = fieldInfo.FieldType;
            var currentValue = fieldInfo.GetValue(parent);

            if (fieldType.IsSubclassOf(typeof(JsonNode)) && ReferenceEquals(currentValue, node))
            {
                // 直接JsonNode字段
                fieldInfo.SetValue(parent, null);
            }
            else if (typeof(System.Collections.IList).IsAssignableFrom(fieldType) && currentValue is System.Collections.IList list)
            {
                // 列表字段
                nodeIndex = list.IndexOf(node);
                list.Remove(node);
            }
        }

        /// <summary>
        /// 节点连接信息
        /// </summary>
        private class NodeConnectionInfo
        {
            public bool IsRootNode { get; set; }
            public string FullPath { get; set; }
            public string ParentPath { get; set; }
            public string PropertyName { get; set; }
        }
    }

    /// <summary>
    /// 属性修改命令
    /// </summary>
    public class PropertyChangeCommand : IHistoryCommand
    {
        private JsonNode targetNode;
        private string propertyPath;
        private object oldValue;
        private object newValue;

        public string Description => $"Change Property: {targetNode?.GetType().Name}.{propertyPath}";

        public PropertyChangeCommand(JsonNode targetNode, string propertyPath, object oldValue, object newValue)
        {
            this.targetNode = targetNode;
            this.propertyPath = propertyPath;
            this.oldValue = oldValue;
            this.newValue = newValue;
        }

        public void Undo(JsonAsset asset)
        {
            SetProperty(oldValue);
        }

        public void Redo(JsonAsset asset)
        {
            SetProperty(newValue);
        }

        private void SetProperty(object value)
        {
            try
            {
                PropertyAccessor.SetValue(targetNode, propertyPath, value);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to set property {propertyPath}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 批量操作命令
    /// </summary>
    public class BatchCommand : IHistoryCommand
    {
        private List<IHistoryCommand> commands = new();

        public string Description => $"Batch Operation ({commands.Count} commands)";

        public void AddCommand(IHistoryCommand command)
        {
            commands.Add(command);
        }

        public void Undo(JsonAsset asset)
        {
            // 反向执行撤销
            for (int i = commands.Count - 1; i >= 0; i--)
            {
                commands[i].Undo(asset);
            }
        }

        public void Redo(JsonAsset asset)
        {
            // 正向执行重做
            foreach (var command in commands)
            {
                command.Redo(asset);
            }
        }
    }

    /// <summary>
    /// 历史扩展方法
    /// </summary>
    public static class HistoryExtensions
    {
        public static void SetDirty(this VisualElement visualElement)
        {
            ViewNode viewNode = visualElement.GetFirstAncestorOfType<ViewNode>();
            viewNode?.View.Window.History.AddStep();
        }

        /// <summary>
        /// 记录节点添加操作
        /// </summary>
        public static void RecordAddNode(this History history, JsonNode node, string nodePath = null)
        {
            var command = new AddNodeCommand(node, nodePath);
            history.AddCommand(command);
        }

        /// <summary>
        /// 记录节点删除操作 - 使用增强版删除命令
        /// </summary>
        public static void RecordRemoveNode(this History history, JsonNode node, string nodePath = null)
        {
            var command = new EnhancedRemoveNodeCommand(node, history.GetWindow().JsonAsset);
            history.AddCommand(command);
        }

        /// <summary>
        /// 记录属性变更操作
        /// </summary>
        public static void RecordPropertyChange(this History history, JsonNode targetNode, string propertyPath, object oldValue, object newValue)
        {
            var command = new PropertyChangeCommand(targetNode, propertyPath, oldValue, newValue);
            history.AddCommand(command);
        }

        /// <summary>
        /// 开始批量操作
        /// </summary>
        public static BatchCommand BeginBatch(this History history)
        {
            return new BatchCommand();
        }

        /// <summary>
        /// 结束批量操作
        /// </summary>
        public static void EndBatch(this History history, BatchCommand batchCommand)
        {
            if (batchCommand != null)
            {
                history.AddCommand(batchCommand);
            }
        }
    }

    /// <summary>
    /// 增强版节点删除命令创建器 - 在删除时准确捕获位置信息
    /// </summary>
    public class EnhancedRemoveNodeCommand : IHistoryCommand
    {
        private JsonNode node;
        private JsonAsset asset; // 保存资产引用以便访问完整数据
        private NodeRemovalInfo removalInfo;

        public string Description => $"Remove Node: {node?.GetType().Name} from {removalInfo?.LocationDescription}";

        public EnhancedRemoveNodeCommand(JsonNode node, JsonAsset asset)
        {
            this.node = node;
            this.asset = asset;
            
            // 在删除前立即捕获节点的位置信息
            CaptureRemovalInfo();
        }

        /// <summary>
        /// 捕获节点删除信息
        /// </summary>
        private void CaptureRemovalInfo()
        {
            removalInfo = new NodeRemovalInfo();
            
            // 检查是否是根节点
            int rootIndex = asset.Data.Nodes.IndexOf(node);
            if (rootIndex >= 0)
            {
                removalInfo.IsRootNode = true;
                removalInfo.RootIndex = rootIndex;
                removalInfo.LocationDescription = $"Root[{rootIndex}]";
                return;
            }

            // 不是根节点，查找其在父节点中的位置
            FindNodeInParentStructure();
        }

        /// <summary>
        /// 在父结构中查找节点位置
        /// </summary>
        private void FindNodeInParentStructure()
        {
            foreach (var rootNode in asset.Data.Nodes)
            {
                var searchResult = SearchNodeInObject(rootNode, node, $"[{asset.Data.Nodes.IndexOf(rootNode)}]");
                if (searchResult != null)
                {
                    removalInfo = searchResult;
                    return;
                }
            }
            
            // 如果没找到，标记为孤立节点
            removalInfo.IsOrphanedNode = true;
            removalInfo.LocationDescription = "Orphaned Node";
        }

        /// <summary>
        /// 在对象中搜索节点并返回位置信息
        /// </summary>
        private NodeRemovalInfo SearchNodeInObject(object obj, JsonNode targetNode, string currentPath)
        {
            if (obj == null) return null;

            var objType = obj.GetType();
            var members = objType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field);

            foreach (var member in members)
            {
                try
                {
                    object value = GetMemberValue(obj, member);
                    if (value == null) continue;

                    string memberPath = $"{currentPath}.{member.Name}";

                    // 直接匹配
                    if (ReferenceEquals(value, targetNode))
                    {
                        return new NodeRemovalInfo
                        {
                            IsRootNode = false,
                            ParentObject = obj,
                            ParentPath = currentPath,
                            MemberInfo = member,
                            MemberPath = memberPath,
                            LocationDescription = memberPath,
                            IsCollection = false,
                            CollectionIndex = -1
                        };
                    }

                    // 集合匹配
                    if (value is System.Collections.IEnumerable enumerable && !(value is string))
                    {
                        var result = SearchInCollection(enumerable, obj, member, memberPath, targetNode);
                        if (result != null) return result;
                    }
                    
                    // 递归搜索复杂对象
                    else if (IsUserDefinedType(value.GetType()))
                    {
                        var result = SearchNodeInObject(value, targetNode, memberPath);
                        if (result != null) return result;
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }

            return null;
        }

        /// <summary>
        /// 在集合中搜索节点
        /// </summary>
        private NodeRemovalInfo SearchInCollection(System.Collections.IEnumerable collection, object parentObj, 
            System.Reflection.MemberInfo member, string memberPath, JsonNode targetNode)
        {
            int index = 0;
            foreach (var item in collection)
            {
                if (ReferenceEquals(item, targetNode))
                {
                    return new NodeRemovalInfo
                    {
                        IsRootNode = false,
                        ParentObject = parentObj,
                        ParentPath = memberPath.Substring(0, memberPath.LastIndexOf('.')),
                        MemberInfo = member,
                        MemberPath = memberPath,
                        LocationDescription = $"{memberPath}[{index}]",
                        IsCollection = true,
                        CollectionIndex = index,
                        Collection = collection as System.Collections.IList
                    };
                }
                
                // 递归搜索集合项中的嵌套对象
                if (item != null && IsUserDefinedType(item.GetType()))
                {
                    var result = SearchNodeInObject(item, targetNode, $"{memberPath}[{index}]");
                    if (result != null) return result;
                }
                
                index++;
            }
            return null;
        }

        /// <summary>
        /// 获取成员值
        /// </summary>
        private object GetMemberValue(object obj, System.Reflection.MemberInfo member)
        {
            if (member is System.Reflection.PropertyInfo prop && prop.CanRead)
                return prop.GetValue(obj);
            else if (member is System.Reflection.FieldInfo field)
                return field.GetValue(obj);
            return null;
        }

        /// <summary>
        /// 判断是否为用户定义类型
        /// </summary>
        private bool IsUserDefinedType(Type type)
        {
            if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string))
                return false;

            var namespaceName = type.Namespace ?? "";
            return !namespaceName.StartsWith("System") && !namespaceName.StartsWith("Unity");
        }

        public void Undo(JsonAsset asset)
        {
            // Undo删除 = 重新添加到原位置
            if (removalInfo.IsRootNode)
            {
                RestoreAsRootNode(asset);
            }
            else if (!removalInfo.IsOrphanedNode)
            {
                RestoreToParentLocation(asset);
            }
        }

        public void Redo(JsonAsset asset)
        {
            // Redo删除 = 从原位置移除
            if (removalInfo.IsRootNode)
            {
                asset.Data.Nodes.Remove(node);
            }
            else if (!removalInfo.IsOrphanedNode)
            {
                RemoveFromParentLocation(asset);
            }
        }

        /// <summary>
        /// 恢复为根节点
        /// </summary>
        private void RestoreAsRootNode(JsonAsset asset)
        {
            if (removalInfo.RootIndex >= 0 && removalInfo.RootIndex <= asset.Data.Nodes.Count)
            {
                asset.Data.Nodes.Insert(removalInfo.RootIndex, node);
            }
            else
            {
                asset.Data.Nodes.Add(node);
            }
        }

        /// <summary>
        /// 恢复到父位置
        /// </summary>
        private void RestoreToParentLocation(JsonAsset asset)
        {
            try
            {
                if (removalInfo.IsCollection && removalInfo.Collection != null)
                {
                    // 集合类型恢复
                    if (removalInfo.CollectionIndex >= 0 && removalInfo.CollectionIndex <= removalInfo.Collection.Count)
                    {
                        removalInfo.Collection.Insert(removalInfo.CollectionIndex, node);
                    }
                    else
                    {
                        removalInfo.Collection.Add(node);
                    }
                }
                else if (removalInfo.MemberInfo != null && removalInfo.ParentObject != null)
                {
                    // 直接成员恢复
                    SetMemberValue(removalInfo.ParentObject, removalInfo.MemberInfo, node);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to restore node to parent location: {ex.Message}");
            }
        }

        /// <summary>
        /// 从父位置移除
        /// </summary>
        private void RemoveFromParentLocation(JsonAsset asset)
        {
            try
            {
                if (removalInfo.IsCollection && removalInfo.Collection != null)
                {
                    removalInfo.Collection.Remove(node);
                }
                else if (removalInfo.MemberInfo != null && removalInfo.ParentObject != null)
                {
                    SetMemberValue(removalInfo.ParentObject, removalInfo.MemberInfo, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to remove node from parent location: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置成员值
        /// </summary>
        private void SetMemberValue(object obj, System.Reflection.MemberInfo member, object value)
        {
            if (member is System.Reflection.PropertyInfo prop && prop.CanWrite)
                prop.SetValue(obj, value);
            else if (member is System.Reflection.FieldInfo field)
                field.SetValue(obj, value);
        }

        /// <summary>
        /// 节点移除信息
        /// </summary>
        private class NodeRemovalInfo
        {
            public bool IsRootNode { get; set; }
            public int RootIndex { get; set; } = -1;
            public bool IsOrphanedNode { get; set; }
            
            public object ParentObject { get; set; }
            public string ParentPath { get; set; }
            public System.Reflection.MemberInfo MemberInfo { get; set; }
            public string MemberPath { get; set; }
            public string LocationDescription { get; set; }
            
            public bool IsCollection { get; set; }
            public int CollectionIndex { get; set; } = -1;
            public System.Collections.IList Collection { get; set; }
        }
    }
}
