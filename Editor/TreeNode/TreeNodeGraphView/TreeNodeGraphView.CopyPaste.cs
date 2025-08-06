using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView
    {
        #region Copy功能实现

        public virtual string Copy(IEnumerable<GraphElement> elements)
        {
            if (elements == null || !elements.Any())
                return string.Empty;

            try
            {
                // 1. 提取选中的JsonNode
                var selectedNodes = ExtractSelectedNodes(elements);
                if (!selectedNodes.Any())
                    return string.Empty;

                // 2. 构建选中节点的父子关系
                var selectedParentChildMap = BuildSelectedParentChildMap(selectedNodes);

                // 3. 识别顶层选中节点
                var topLevelNodes = GetTopLevelSelectedNodes(selectedNodes);

                // 4. 递归构建选中节点树
                var rootNodeData = topLevelNodes.Select(node => 
                    BuildSelectedNodeTree(node, selectedParentChildMap)).ToList();

                // 5. 计算原始中心点
                var positions = selectedNodes.Select(n => (Vector2)n.Position).ToList();
                var originalCenter = CalculateCenter(positions);

                // 6. 创建复制数据
                var copyData = new CopyPasteData
                {
                    RootNodes = rootNodeData,
                    OriginalCenter = originalCenter
                };

                // 7. 序列化为JSON
                return Json.ToJson(copyData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Copy操作失败: {ex.Message}");
                return string.Empty;
            }
        }

        private List<JsonNode> ExtractSelectedNodes(IEnumerable<GraphElement> elements)
        {
            var viewNodes = elements.OfType<ViewNode>();
            var selectedNodes = viewNodes.Select(vn => vn.Data).ToList();
            return selectedNodes.Distinct().ToList();
        }

        private Dictionary<JsonNode, List<JsonNode>> BuildSelectedParentChildMap(List<JsonNode> selectedNodes)
        {
            var selectedSet = selectedNodes.ToHashSet();
            var parentChildMap = new Dictionary<JsonNode, List<JsonNode>>();

            foreach (var node in selectedNodes)
            {
                var allChildren = GetAllChildrenNodes(node);
                var selectedChildren = allChildren.Where(child => selectedSet.Contains(child)).ToList();
                
                if (selectedChildren.Any())
                {
                    parentChildMap[node] = selectedChildren;
                }
            }

            return parentChildMap;
        }

        private List<JsonNode> GetTopLevelSelectedNodes(List<JsonNode> selectedNodes)
        {
            var selectedSet = selectedNodes.ToHashSet();
            var topLevelNodes = new List<JsonNode>();

            foreach (var node in selectedNodes)
            {
                var parentNode = GetParentNode(node);
                if (parentNode == null || !selectedSet.Contains(parentNode))
                {
                    topLevelNodes.Add(node);
                }
            }

            return topLevelNodes;
        }

        private NodeCopyData BuildSelectedNodeTree(JsonNode node, Dictionary<JsonNode, List<JsonNode>> selectedParentChildMap)
        {
            var nodeData = new NodeCopyData
            {
                TypeName = node.GetType().AssemblyQualifiedName ?? string.Empty,
                JsonData = SerializeNodeOnly(node),
                Position = node.Position,
                Children = new List<NodeCopyData>()
            };

            if (selectedParentChildMap.TryGetValue(node, out var selectedChildren))
            {
                foreach (var child in selectedChildren)
                {
                    var childData = BuildSelectedNodeTree(child, selectedParentChildMap);
                    childData = childData with { PropertyPath = GetPropertyPath(node, child) };
                    nodeData.Children.Add(childData);
                }
            }

            return nodeData;
        }

        private Vector2 CalculateCenter(List<Vector2> positions)
        {
            if (!positions.Any()) return Vector2.zero;
            
            var sum = positions.Aggregate(Vector2.zero, (acc, pos) => acc + pos);
            return sum / positions.Count;
        }

        private List<JsonNode> GetAllChildrenNodes(JsonNode node)
        {
            var children = new List<JsonNode>();
            var nodeType = node.GetType();
            var properties = nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<ChildAttribute>() != null)
                {
                    var value = prop.GetValue(node);
                    if (value is JsonNode childNode)
                    {
                        children.Add(childNode);
                    }
                    else if (value is IEnumerable<JsonNode> childNodes)
                    {
                        children.AddRange(childNodes);
                    }
                }
            }

            return children;
        }

        private JsonNode GetParentNode(JsonNode targetNode)
        {
            return NodeTree.GetNodeMetadata(targetNode)?.Parent?.Node;
        }

        private string SerializeNodeOnly(JsonNode node)
        {
            return JsonUtility.ToJson(node);
        }

        private string GetPropertyPath(JsonNode parent, JsonNode child)
        {
            var parentType = parent.GetType();
            var properties = parentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<ChildAttribute>() != null)
                {
                    var value = prop.GetValue(parent);
                    if (value == child)
                    {
                        return prop.Name;
                    }
                    else if (value is IEnumerable<JsonNode> childNodes && childNodes.Contains(child))
                    {
                        return prop.Name;
                    }
                }
            }

            return string.Empty;
        }

        #endregion

        #region CanPaste功能实现

        public virtual bool CanPaste(string data)
        {
            bool canPaste = CanPasteWithUserChoice(data, Asset, out _, out _);
            return canPaste;
        }

        private bool CanPasteWithUserChoice(string data, JsonAsset currentAsset, out CopyPasteData finalData, out string errorMessage)
        {
            finalData = null;
            errorMessage = string.Empty;

            try
            {
                if (string.IsNullOrEmpty(data))
                    return false;

                var copyData = Json.Get<CopyPasteData>(data);
                if (copyData?.RootNodes == null || !copyData.RootNodes.Any())
                    return false;

                var validationResult = ValidateAndPrepareData(copyData, currentAsset);

                if (!validationResult.HasInvalidNodes)
                {
                    finalData = copyData;
                    return true;
                }

                if (!validationResult.HasValidNodesAfterClean)
                {
                    errorMessage = $"所有节点都不合法，无法粘贴:\n{string.Join("\n", validationResult.InvalidNodeTypes)}";
                    EditorUtility.DisplayDialog("粘贴失败", errorMessage, "确定");
                    return false;
                }

                var decision = ShowPasteDecisionDialog(validationResult.InvalidNodeTypes);

                switch (decision)
                {
                    case PasteDecision.Cancel:
                        return false;
                    case PasteDecision.RemoveInvalid:
                        finalData = validationResult.CleanedData;
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"数据验证失败: {ex.Message}";
                return false;
            }
        }

        private PasteValidationResult ValidateAndPrepareData(CopyPasteData copyData, JsonAsset currentAsset)
        {
            var invalidTypes = new List<string>();
            var validRootNodes = new List<NodeCopyData>();

            foreach (var rootNode in copyData.RootNodes)
            {
                var result = ValidateAndCleanNodeWithPromotion(rootNode, currentAsset, invalidTypes);
                validRootNodes.AddRange(result);
            }

            var hasInvalidNodes = invalidTypes.Any();
            var hasValidNodes = validRootNodes.Any();
            var cleanedData = copyData with { RootNodes = validRootNodes };

            return new PasteValidationResult
            {
                HasInvalidNodes = hasInvalidNodes,
                InvalidNodeTypes = invalidTypes.Distinct().ToList(),
                ErrorMessage = hasInvalidNodes ? $"发现 {invalidTypes.Count} 个不合法节点类型" : string.Empty,
                CleanedData = cleanedData,
                HasValidNodesAfterClean = hasValidNodes
            };
        }

        private List<NodeCopyData> ValidateAndCleanNodeWithPromotion(NodeCopyData nodeData, JsonAsset currentAsset, List<string> invalidTypes)
        {
            var result = new List<NodeCopyData>();
            var nodeType = Type.GetType(nodeData.TypeName);
            var isCurrentNodeValid = nodeType != null && IsNodeTypeAllowedInAsset(nodeType, currentAsset);

            if (!isCurrentNodeValid)
            {
                invalidTypes.Add($"{nodeData.TypeName} ({(nodeType == null ? "类型不存在" : "不符合Asset过滤规则")})");

                foreach (var child in nodeData.Children)
                {
                    var promotedChildren = ValidateAndCleanNodeWithPromotion(child, currentAsset, invalidTypes);
                    result.AddRange(promotedChildren);
                }

                return result;
            }

            var validChildren = new List<NodeCopyData>();

            foreach (var child in nodeData.Children)
            {
                var childResults = ValidateAndCleanNodeWithPromotion(child, currentAsset, invalidTypes);
                var childType = Type.GetType(child.TypeName);
                var isChildValid = childType != null && IsNodeTypeAllowedInAsset(childType, currentAsset);

                if (isChildValid)
                {
                    var validChild = child with { Children = childResults.ToList() };
                    validChildren.Add(validChild);
                }
                else
                {
                    validChildren.AddRange(childResults);
                }
            }

            var cleanedNode = nodeData with { Children = validChildren };
            result.Add(cleanedNode);

            return result;
        }

        private bool IsNodeTypeAllowedInAsset(Type nodeType, JsonAsset asset)
        {
            var assetFilterAttr = asset.GetType().GetCustomAttribute<AssetFilterAttribute>();
            if (assetFilterAttr != null)
            {
                if (assetFilterAttr.Types.Contains(nodeType))
                {
                    return assetFilterAttr.Allowed;
                }

                if (assetFilterAttr.BanPrefab && IsPrefabNode(nodeType))
                {
                    return false;
                }
            }

            var nodeFilterAttr = nodeType.GetCustomAttribute<AssetFilterAttribute>();
            if (nodeFilterAttr != null)
            {
                return nodeFilterAttr.Allowed;
            }

            return true;
        }

        private bool IsPrefabNode(Type nodeType)
        {
            return nodeType.GetProperty("PrefabData") != null;
        }

        private PasteDecision ShowPasteDecisionDialog(List<string> invalidNodeTypes)
        {
            var message = $"检测到以下不合法的节点类型:\n{string.Join("\n", invalidNodeTypes)}\n\n您希望如何处理?";

            var choice = EditorUtility.DisplayDialogComplex(
                "粘贴节点验证",
                message,
                "剔除不合法节点并继续",
                "取消操作",
                ""
            );

            return choice switch
            {
                0 => PasteDecision.RemoveInvalid,
                1 => PasteDecision.Cancel,
                _ => PasteDecision.Cancel
            };
        }

        #endregion

        #region Paste功能实现

        public virtual void Paste(string operationName, string data)
        {
            if (CanPasteWithUserChoice(data, Asset, out var finalData, out var errorMessage))
            {
                PasteWithCleanedData(operationName, finalData);
            }
            else if (!string.IsNullOrEmpty(errorMessage))
            {
                Debug.LogWarning($"Paste操作被取消: {errorMessage}");
            }
        }

        private void PasteWithCleanedData(string operationName, CopyPasteData cleanedData)
        {
            try
            {
                Window.History?.BeginBatch();

                var createdNodes = new List<JsonNode>();

                foreach (var rootNodeData in cleanedData.RootNodes)
                {
                    var createdNode = CreateNodeTreeRecursively(rootNodeData);
                    createdNodes.Add(createdNode);
                }

                ApplyPositionOffset(createdNodes, cleanedData.OriginalCenter);
                NodeTree.RefreshIfNeeded();
                Redraw();

                Window.History?.EndBatch();
                MakeDirty();

                Debug.Log($"成功粘贴 {createdNodes.Count} 个节点");
            }
            catch (Exception ex)
            {
                Window.History?.EndBatch();
                Debug.LogError($"Paste操作失败: {ex.Message}");
                throw;
            }
        }

        private JsonNode CreateNodeTreeRecursively(NodeCopyData nodeData)
        {
            var nodeType = Type.GetType(nodeData.TypeName);
            var node = JsonUtility.FromJson(nodeData.JsonData, nodeType) as JsonNode;

            node.Position = nodeData.Position;

            foreach (var childData in nodeData.Children)
            {
                var childNode = CreateNodeTreeRecursively(childData);

                if (!string.IsNullOrEmpty(childData.PropertyPath))
                {
                    SetChildNode(node, childData.PropertyPath, childNode);
                }
                else
                {
                    Asset.Data.Nodes.Add(childNode);
                }
            }

            Asset.Data.Nodes.Add(node);
            return node;
        }

        private void SetChildNode(JsonNode parent, string propertyPath, JsonNode child)
        {
            var parentType = parent.GetType();
            var property = parentType.GetProperty(propertyPath);

            if (property != null && property.CanWrite)
            {
                if (property.PropertyType == typeof(JsonNode) || property.PropertyType.IsSubclassOf(typeof(JsonNode)))
                {
                    property.SetValue(parent, child);
                }
                else if (property.PropertyType.IsGenericType && 
                         property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var list = property.GetValue(parent) as System.Collections.IList;
                    list?.Add(child);
                }
            }
        }

        private void ApplyPositionOffset(List<JsonNode> nodes, Vector2 originalCenter)
        {
            var offset = GetMousePosition() - originalCenter + Vector2.one * 50; // 添加50像素偏移避免重叠

            foreach (var node in nodes)
            {
                node.Position = (Vector2)node.Position + offset;
            }
        }

        #endregion
    }
}
