using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using TreeNode.Runtime;

namespace TreeNode.Editor
{
    /// <summary>
    /// 提供History系统的共享组件，包括枚举、数据结构、扩展方法和工具类
    /// </summary>

    /// <summary>
    /// 操作类型枚举
    /// </summary>
    public enum OperationType
    {
        Create,
        Delete,
        Move,
    }
    /// <summary>
    /// History系统扩展方法
    /// </summary>
    public static class HistoryExtensions
    {
        public static void SetDirty(this VisualElement visualElement)
        {
            ViewNode viewNode = visualElement.GetFirstAncestorOfType<ViewNode>();
            viewNode?.View.Window.History.AddStep();
        }

        /// <summary>
        /// 获取类型的默认值
        /// </summary>
        public static object GetDefaultValue(this Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    /// <summary>
    /// 操作配置常量
    /// </summary>
    public static class HistoryConstants
    {
        public const int DEFAULT_MAX_STEPS = 20;
        public const int DEFAULT_MAX_MEMORY_MB = 50;
        public const int DEFAULT_MERGE_WINDOW_MS = 500;
        public const long DEFAULT_GC_INTERVAL_TICKS = TimeSpan.TicksPerMinute * 5; // 每5分钟
    }

}