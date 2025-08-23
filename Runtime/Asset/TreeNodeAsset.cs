using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TreeNode.Utility;
using UnityEditor.VersionControl;
namespace TreeNode.Runtime
{
    [Serializable]
    public abstract class TreeNodeAsset
    {
        public List<JsonNode> Nodes = new();


        public T GetValue<T>(string path)
        {
            return PropertyAccessor.GetValue<T>(Nodes, path);
        }
        public virtual string GetTreeView(Func<JsonNode,string> text = null,bool appendText = true)
        {
            // 直接使用新的递归实现，不再依赖JsonNodeTree
            if (Nodes == null || Nodes.Count == 0)
            {
                return "Empty Tree";
            }
            text ??= (n => n.GetInfo() ?? "Unknown");
            var sb = new StringBuilder();

            // 获取根节点 - Asset.Data.Nodes中的节点都是根节点
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                }

                var rootNode = Nodes[i];
                var rootPath = PAPath.Index(i);
                sb.Append($"[{i}]");
                // 构建根节点，从根节点开始递归
                BuildTreeNodeRecursive(rootNode, sb, "", true, true, rootPath, text);

                if (appendText)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[{i}]{Nodes[i].GetText()}");
                }
            }

            return sb.ToString();
        }
        private void BuildTreeNodeRecursive(JsonNode node, StringBuilder sb,
            string prefix, bool isLast, bool isRoot, PAPath nodePath, Func<JsonNode, string> text)
        {
            // 输出当前节点信息
            string displayName = text?.Invoke(node);
            if (isRoot)
            {
                // 根节点没有连接符前缀
                sb.AppendLine($"{displayName}");
            }
            else
            {
                // 非根节点使用连接符
                string connector = isLast ? "└─ " : "├─ ";
                sb.AppendLine($"{prefix}{connector}{displayName}");
            }

            // 获取直接子节点，深度为1只获取直接子节点
            var childrenList = ListPool<(PAPath, JsonNode)>.GetList();
            try
            {
                node.CollectNodes(childrenList, nodePath, 1);

                if (childrenList.Count > 0)
                {
                    // 计算子节点的前缀
                    string childPrefix;
                    if (isRoot)
                    {
                        childPrefix = "";
                    }
                    else
                    {
                        childPrefix = prefix + (isLast ? "\t" : "│\t");
                    }

                    // 递归处理每个子节点
                    for (int i = 0; i < childrenList.Count; i++)
                    {
                        bool isLastChild = i == childrenList.Count - 1;
                        var (childPath, childNode) = childrenList[i];

                        BuildTreeNodeRecursive(childNode, sb, childPrefix, isLastChild, false, childPath, text);
                    }
                }
            }
            finally
            {
                childrenList.Release();
            }
        }


        public List<(string, string)> GetAllNodeInfo()
        { 
            PAPath root = PAPath.Empty;
            List<(PAPath, JsonNode)> list = ListPool<(PAPath, JsonNode)>.GetList();
            Nodes.CollectNodes(list, root);
            List<(string, string)> result = list.Select(n => (n.Item1.ToString(), n.Item2.GetInfo())).ToList();
            list.Release();
            return result;
        }



    }

}
