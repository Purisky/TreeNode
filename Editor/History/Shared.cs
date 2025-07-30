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
    /// 位置类型枚举
    /// </summary>
    public enum LocationType
    {
        Root,
        Child,
        Deleted,
        Unknown
    }

    /// <summary>
    /// 操作复杂度枚举 - 用于性能优化
    /// </summary>
    public enum OperationComplexity
    {
        Simple,    // 简单操作，影响单个节点
        Moderate,  // 中等复杂度，影响多个节点
        Complex    // 复杂操作，影响整个图结构
    }

    /// <summary>
    /// 节点位置描述
    /// </summary>
    public class NodeLocation
    {
        public LocationType Type { get; set; }
        public int RootIndex { get; set; } = -1;
        public JsonNode ParentNode { get; set; }
        public string PortName { get; set; } = "";
        public bool IsMultiPort { get; set; } = false;
        public int ListIndex { get; set; } = -1;

        public static NodeLocation Root(int index = -1) => new()
        {
            Type = LocationType.Root,
            RootIndex = index
        };

        public static NodeLocation Child(JsonNode parent, string portName, bool isMultiPort, int listIndex) => new()
        {
            Type = LocationType.Child,
            ParentNode = parent,
            PortName = portName,
            IsMultiPort = isMultiPort,
            ListIndex = listIndex
        };

        public static NodeLocation Deleted() => new()
        {
            Type = LocationType.Deleted
        };

        public static NodeLocation Unknown() => new()
        {
            Type = LocationType.Unknown
        };

        public string GetFullPath()
        {
            return Type switch
            {
                LocationType.Root => $"Root[{RootIndex}]",
                LocationType.Child => $"{ParentNode?.GetType().Name}.{PortName}" +
                                     (IsMultiPort ? $"[{ListIndex}]" : ""),
                LocationType.Deleted => "Deleted",
                LocationType.Unknown => "Unknown",
                _ => "Invalid"
            };
        }
    }
    



    /// <summary>
    /// 操作元数据
    /// </summary>
    public class OperationMetadata
    {
        public DateTime Timestamp { get; set; }
        public OperationComplexity Complexity { get; set; }
        public string UserId { get; set; }
        public bool IsSystemGenerated { get; set; }

        public OperationMetadata()
        {
            Timestamp = DateTime.Now;
            Complexity = OperationComplexity.Simple;
            IsSystemGenerated = false;
        }
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

        /// <summary>
        /// 尝试解析Position字符串
        /// </summary>
        public static bool TryParsePosition(this string positionStr, out Vec2 position)
        {
            position = default;

            if (string.IsNullOrEmpty(positionStr))
                return false;

            // 移除括号和空格
            positionStr = positionStr.Trim('(', ')', ' ');
            var parts = positionStr.Split(',');

            if (parts.Length != 2)
                return false;

            if (float.TryParse(parts[0].Trim(), out var x) &&
                float.TryParse(parts[1].Trim(), out var y))
            {
                position = new Vec2(x, y);
                return true;
            }

            return false;
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