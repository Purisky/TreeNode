using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Editor
{
    /// <summary>
    /// 复制粘贴数据结构 - 仅包含根节点列表
    /// </summary>
    [Serializable]
    public record CopyPasteData
    {
        public List<NodeCopyData> RootNodes { get; set; } = new();
        public Vec2 OriginalCenter { get; set; }
    }

    /// <summary>
    /// 节点复制数据结构 - 包含完整的节点层次结构
    /// </summary>
    [Serializable]
    public record NodeCopyData
    {
        public string TypeName { get; set; } = string.Empty;
        public string JsonData { get; set; } = string.Empty;
        public Vec2 Position { get; set; }
        public List<NodeCopyData> Children { get; set; } = new();
        public string PropertyPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// 用户粘贴决策枚举
    /// </summary>
    public enum PasteDecision
    {
        Cancel,
        RemoveInvalid,
        ForceAll
    }

    /// <summary>
    /// 粘贴验证结果
    /// </summary>
    public class PasteValidationResult
    {
        public bool HasInvalidNodes { get; set; }
        public List<string> InvalidNodeTypes { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;
        public CopyPasteData CleanedData { get; set; }
        public bool HasValidNodesAfterClean { get; set; }
    }
}
