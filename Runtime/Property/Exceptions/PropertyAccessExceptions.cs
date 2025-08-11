using System;

namespace TreeNode.Runtime.Property.Exceptions
{
    /// <summary>
    /// 属性访问异常基类
    /// </summary>
    public abstract class PropertyAccessException : Exception
    {
        public string PropertyPath { get; }
        public Type TargetType { get; }

        protected PropertyAccessException(string message, string propertyPath, Type targetType) 
            : base(message)
        {
            PropertyPath = propertyPath;
            TargetType = targetType;
        }

        protected PropertyAccessException(string message, string propertyPath, Type targetType, Exception innerException) 
            : base(message, innerException)
        {
            PropertyPath = propertyPath;
            TargetType = targetType;
        }
    }

    /// <summary>
    /// 路径未找到异常
    /// </summary>
    public class PathNotFoundException : PropertyAccessException
    {
        public int ValidLength { get; }

        public PathNotFoundException(string propertyPath, Type targetType, int validLength)
            : base($"路径 '{propertyPath}' 在类型 '{targetType.Name}' 中未找到，有效长度为 {validLength}", propertyPath, targetType)
        {
            ValidLength = validLength;
        }
    }

    /// <summary>
    /// 类型不匹配异常
    /// </summary>
    public class TypeMismatchException : PropertyAccessException
    {
        public Type ExpectedType { get; }
        public Type ActualType { get; }

        public TypeMismatchException(string propertyPath, Type targetType, Type expectedType, Type actualType)
            : base($"路径 '{propertyPath}' 类型不匹配: 期望 '{expectedType.Name}', 实际 '{actualType.Name}'", propertyPath, targetType)
        {
            ExpectedType = expectedType;
            ActualType = actualType;
        }
    }

    /// <summary>
    /// 成员不可访问异常
    /// </summary>
    public class MemberInaccessibleException : PropertyAccessException
    {
        public string MemberName { get; }

        public MemberInaccessibleException(string propertyPath, Type targetType, string memberName)
            : base($"成员 '{memberName}' 在类型 '{targetType.Name}' 中不可访问", propertyPath, targetType)
        {
            MemberName = memberName;
        }
    }

    /// <summary>
    /// 索引超出范围异常
    /// </summary>
    public class IndexOutOfRangeException : PropertyAccessException
    {
        public int Index { get; }
        public int CollectionSize { get; }

        public IndexOutOfRangeException(string propertyPath, Type targetType, int index, int collectionSize)
            : base($"索引 {index} 超出集合范围 [0, {collectionSize})", propertyPath, targetType)
        {
            Index = index;
            CollectionSize = collectionSize;
        }
    }

    public class NestedCollectionException : PropertyAccessException
    {
        public NestedCollectionException(string propertyPath, Type targetType)
            : base($"嵌套集合在路径 '{propertyPath}' 中不被支持,使用List<JsonNode>替代", propertyPath, targetType)
        {
        }
    }

}