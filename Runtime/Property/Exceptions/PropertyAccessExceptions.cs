using System;
using TreeNode.Utility;

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
            : base(string.Format(I18n.Runtime.Error.PathNotFound, propertyPath, targetType.Name, validLength), propertyPath, targetType)
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
            : base(string.Format(I18n.Runtime.Error.TypeMismatch, propertyPath, expectedType.Name, actualType.Name), propertyPath, targetType)
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
            : base(string.Format(I18n.Runtime.Error.MemberNotAccessible, memberName, targetType.Name), propertyPath, targetType)
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
            : base(string.Format(I18n.Runtime.Validation.IndexOutOfRange, index, collectionSize), propertyPath, targetType)
        {
            Index = index;
            CollectionSize = collectionSize;
        }
    }

    public class NestedCollectionException : PropertyAccessException
    {
        public NestedCollectionException(string propertyPath, Type targetType)
            : base(string.Format(I18n.Runtime.Error.NestedCollectionNotSupported, propertyPath), propertyPath, targetType)
        {
        }
    }

}