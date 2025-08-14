using System;

namespace TreeNode.Runtime
{
    /// <summary>
    /// 标记该类型的嵌套结构不可能包含JsonNode，用于优化CollectNodes性能
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class NoJsonNodeContainerAttribute : Attribute
    {
    }
}
