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
        public List<JsonNode> RootNodes { get; set; } = new();
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
