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

            var viewNodes = elements.OfType<ViewNode>();
            Dictionary<PAPath,ViewNode> nodes = viewNodes.ToDictionary(vn => vn.GetNodePath(), vn => vn);
            Dictionary<ViewNode, List<ViewNode>> root = GetRoots(viewNodes);
            CopyPasteData copyPasteData = new() { RootNodes = new()};
            var list = ListPool<(PAPath, JsonNode)>.GetList();
            foreach (var item in root)
            {
                PAPath rootPath = item.Key.GetNodePath();
                JsonNode rootNode = Json.DeepCopy(item.Key.Data);
                IEnumerable<PAPath> children = item.Value.Select(n => n.GetNodePath());
                HandleNode(children, list, rootPath, rootNode);
                copyPasteData.RootNodes.Add(rootNode);
            }

            for (int i = 0; i < root.Count; i++)
            {

            }
            list.Release();
            string json = Json.ToJson(copyPasteData);
            //Debug.Log(json);
            return json;
            static Dictionary<ViewNode, List<ViewNode>> GetRoots(IEnumerable<ViewNode> viewNodes)
            {
                List<ViewNode> root = new(viewNodes);
                var remove = new List<ViewNode>();
                foreach (var item in viewNodes)
                {
                    ViewNode parent = item.GetParent();
                    if (parent != null && viewNodes.Contains(parent))
                    {
                        remove.Add(item);
                    }
                }
                for (int i = 0; i < remove.Count; i++)
                {
                    root.Remove(remove[i]);
                }
                Dictionary<ViewNode, List<ViewNode>> ChildrenDict = root.ToDictionary(
                    r => r,
                    r => new List<ViewNode>()
                );
                foreach (var item in viewNodes.Except(root))
                {
                    ViewNode current = item.GetParent();
                    List<ViewNode> list_ = null;
                    while (!ChildrenDict.TryGetValue(current, out list_))
                    {
                        current = current.GetParent();
                    }
                    list_.Add(item);
                }
                return ChildrenDict;
            }
            static void HandleNode(IEnumerable<PAPath> children, List<(PAPath, JsonNode)> list, PAPath rootPath, JsonNode rootNode)
            {
                list.Clear();
                rootNode.CollectNodes(list, rootPath, 1);
                Stack<PAPath> remove = new();

                var temp = ListPool<(PAPath, JsonNode)>.GetList();
                for (int j = 0; j < list.Count; j++)
                {
                    PAPath itemPath = list[j].Item1;
                    if (children.Any(n => n.StartsWith(itemPath)))
                    {
                        IEnumerable<PAPath> pAPaths = children.Where(n => n.IsChildOf(itemPath));
                        HandleNode(pAPaths, temp, itemPath, list[j].Item2);
                    }
                    else
                    {
                        remove.Push(itemPath);
                    }
                }
                while (remove.Count > 0)
                {
                    PAPath item = remove.Pop();
                    int index = rootPath.Depth;
                    rootNode.RemoveValueInternal(ref item, ref index);
                }
                temp.Release();
            }
        }





        #endregion

        #region CanPaste功能实现

        public virtual bool CanPaste(string data)
        {
            return true;
            //bool canPaste = CanPasteWithUserChoice(data, Asset, out _, out _);
            //return canPaste;
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

                var validationResult = ValidateAndPrepareData(copyData, currentAsset.Data);

                if (!validationResult.HasInvalidNodes)
                {
                    finalData = copyData;
                    return true;
                }

                if (!validationResult.HasValidNodesAfterClean)
                {
                    errorMessage = $"所有节点都不合法，无法粘贴:\n{string.Join("\n", validationResult.InvalidNodeTypes)}";
                    EditorUtility.DisplayDialog(I18n.Editor.Dialog.PasteFailed, errorMessage, I18n.Editor.Button.Confirm);
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

        private PasteValidationResult ValidateAndPrepareData(CopyPasteData copyData, TreeNodeAsset currentAsset)
        {
            var invalidTypes = new List<string>();
            var validRootNodes = new List<JsonNode>();

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

        private List<JsonNode> ValidateAndCleanNodeWithPromotion(JsonNode node, TreeNodeAsset currentAsset, List<string> invalidTypes)
        {
            var result = new List<JsonNode>();
            HandleNode(node, PAPath.Empty);
            return result;

            bool HandleNode(JsonNode node,PAPath parent)
            {
                Type type = node.GetType();
                bool isValid = IsNodeTypeAllowedInAsset(type, currentAsset);
                if (isValid)
                {
                    result.Add(node);
                }
                Debug.Log($"{type.Name}->{currentAsset.GetType().Name}:{isValid}");
                List<(PAPath,JsonNode)> list = ListPool<(PAPath,JsonNode)>.GetList();
                node.CollectNodes(list, parent, 1);
                if (!isValid)
                {
                    invalidTypes.Add(type.Name);
                    for (int i = 1; i <= list.Count; i++)
                    {
                        int index = parent.Depth;
                        PAPath path = list[^i].Item1;
                        node.RemoveValueInternal(ref path, ref index);
                    }
                }
                for (int i = 1; i <= list.Count; i++)
                {
                    bool valid = HandleNode(list[^i].Item2, list[^i].Item1);
                    if (!valid && isValid)
                    {
                        int index = parent.Depth;
                        PAPath path = list[^i].Item1;
                        node.RemoveValueInternal(ref path, ref index);
                    }
                }
                list.Release();
                return isValid;
            }
        }






        private bool IsNodeTypeAllowedInAsset(Type nodeType, TreeNodeAsset asset)
        {
            Type assetType = asset.GetType();
            var nodeFilterAttr = nodeType.GetCustomAttribute<AssetFilterAttribute>();
            if (nodeFilterAttr != null)
            {
                if (nodeFilterAttr.BanPrefab && asset is NodePrefabAsset)
                {
                    return false;
                }
                return nodeFilterAttr.Types.Contains(assetType) == nodeFilterAttr.Allowed;
            }

            return true;
        }

        private PasteDecision ShowPasteDecisionDialog(List<string> invalidNodeTypes)
        {
            var message = $"检测到以下不合法的节点类型:\n{string.Join("\n", invalidNodeTypes)}\n\n您希望如何处理?";

            var choice = EditorUtility.DisplayDialogComplex(
                I18n.Editor.Dialog.PasteValidation,
                message,
                I18n.Editor.Button.RemoveInvalidAndContinue,
                I18n.Editor.Button.Cancel,
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
                Window.History.BeginBatch();

                var createdNodes = cleanedData.RootNodes;
                int count = 0;
                ApplyPositionOffset(createdNodes);
                List<(PAPath,JsonNode)> list = ListPool<(PAPath,JsonNode)>.GetList();
                for (int i = 0; i < createdNodes.Count; i++)
                {
                    PAPath path = PAPath.Index(Asset.Data.Nodes.Count);
                    Asset.Data.Nodes.Add(createdNodes[i]);
                    ViewNode viewNode = AddViewNode(createdNodes[i]);
                    Window.History.Record(NodeOperation.Create(createdNodes[i], path, Asset));
                    list.Clear();
                    count++;
                    createdNodes[i].CollectNodes(list, path, -1);
                    count+= list.Count;
                    for (int j = 0; j < list.Count; j++)
                    {
                        AddViewNodeWithConnection(list[j].Item2, list[j].Item1);
                        Window.History.Record(NodeOperation.Create(list[j].Item2, list[j].Item1, Asset));
                    }
                }
                list.Release();
                Window.History.EndBatch();
                MakeDirty();

                Debug.Log($"成功粘贴 {count} 个节点");
            }
            catch (Exception ex)
            {
                Window.History?.EndBatch();
                Debug.LogError($"Paste操作失败: {ex.Message}");
                throw;
            }
        }

        private void ApplyPositionOffset(List<JsonNode> nodes)
        {
            Vec2 originalCenter= nodes[0].Position; ;

            for (int i = 1; i < nodes.Count; i++)
            {
                originalCenter = Vec2.Min(nodes[i].Position, originalCenter);
            }
            var offset = GetMousePosition() - originalCenter + new Vec2(50, 50); // 添加50像素偏移避免重叠
            List<(PAPath, JsonNode)> list = ListPool<(PAPath, JsonNode)>.GetList();
            foreach (var node in nodes)
            {
                list.Clear();
                node.CollectNodes(list, PAPath.Empty, -1);
                list.ForEach(n => n.Item2.Position = n.Item2.Position + offset);
                node.Position = node.Position + offset;
            }
            list.Release();
        }

        #endregion
    }
}
