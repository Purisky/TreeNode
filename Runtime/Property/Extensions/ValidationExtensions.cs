using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Utility;

namespace TreeNode.Runtime.Property.Extensions
{
    /// <summary>
    /// 验证结果
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 是否验证成功
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误消息列表
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 警告消息列表
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// 验证的路径
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 有效路径长度
        /// </summary>
        public int ValidLength { get; set; }

        /// <summary>
        /// 添加错误
        /// </summary>
        public void AddError(string error)
        {
            IsValid = false;
            Errors.Add(error);
        }

        /// <summary>
        /// 添加警告
        /// </summary>
        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }

        /// <summary>
        /// 获取所有消息
        /// </summary>
        public string GetAllMessages()
        {
            var messages = new List<string>();
            
            if (Errors.Any())
            {
                messages.Add(string.Format(I18n.Runtime.Message.ErrorPrefix, string.Join("; ", Errors)));
            }
            
            if (Warnings.Any())
            {
                messages.Add(string.Format(I18n.Runtime.Message.WarningPrefix, string.Join("; ", Warnings)));
            }
            
            return string.Join("\n", messages);
        }
    }

    /// <summary>
    /// 验证扩展方法
    /// </summary>
    public static class ValidationExtensions
    {
        /// <summary>
        /// 验证属性路径
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <returns>验证结果</returns>
        public static ValidationResult ValidatePropertyPath(this object obj, string path)
        {
            var result = new ValidationResult { Path = path };

            if (obj == null)
            {
                result.AddError(I18n.Runtime.Error.TargetNull);
                return result;
            }

            if (string.IsNullOrEmpty(path))
            {
                result.AddError(I18n.Runtime.Validation.PathEmpty);
                return result;
            }

            try
            {
                bool isValid = PropertyAccessor.GetValidPath(obj, path, out int validLength);
                result.ValidLength = validLength;

                if (isValid)
                {
                    result.IsValid = true;
                }
                else
                {
                    result.AddError(string.Format(I18n.Runtime.Validation.PathInvalid, validLength));
                }
            }
            catch (Exception ex)
            {
                result.AddError(string.Format(I18n.Runtime.Validation.ValidationException, ex.Message));
            }

            return result;
        }

        /// <summary>
        /// 批量验证属性路径
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="paths">属性路径列表</param>
        /// <returns>验证结果列表</returns>
        public static List<ValidationResult> ValidatePropertyPaths(this object obj, params string[] paths)
        {
            return paths.Select(path => obj.ValidatePropertyPath(path)).ToList();
        }

        /// <summary>
        /// 验证对象的完整性
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="requiredPaths">必需的属性路径</param>
        /// <returns>验证结果</returns>
        public static ValidationResult ValidateObjectIntegrity(this object obj, params string[] requiredPaths)
        {
            var result = new ValidationResult();

            if (obj == null)
            {
                result.AddError(I18n.Runtime.Error.TargetNull);
                return result;
            }

            foreach (var path in requiredPaths)
            {
                var pathResult = obj.ValidatePropertyPath(path);
                if (!pathResult.IsValid)
                {
                    result.AddError(string.Format(I18n.Runtime.Validation.RequiredPathInvalid, path, pathResult.GetAllMessages()));
                }
                else
                {
                    // 检查值是否为null
                    try
                    {
                        var value = PropertyAccessor.GetValue<object>(obj, path);
                        if (value == null)
                        {
                            result.AddWarning(string.Format(I18n.Runtime.Validation.RequiredPathNull, path));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AddError(string.Format(I18n.Runtime.Validation.AccessException, path, ex.Message));
                    }
                }
            }

            result.IsValid = !result.Errors.Any();
            return result;
        }

        /// <summary>
        /// 验证类型兼容性
        /// </summary>
        /// <typeparam name="T">期望类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <returns>验证结果</returns>
        public static ValidationResult ValidateTypeCompatibility<T>(this object obj, string path)
        {
            var result = new ValidationResult { Path = path };

            var pathResult = obj.ValidatePropertyPath(path);
            if (!pathResult.IsValid)
            {
                result.Errors.AddRange(pathResult.Errors);
                return result;
            }

            try
            {
                var value = PropertyAccessor.GetValue<T>(obj, path);
                result.IsValid = true;
            }
            catch (InvalidCastException ex)
            {
                result.AddError(string.Format(I18n.Runtime.Validation.TypeConversionFailed, ex.Message));
            }
            catch (Exception ex)
            {
                result.AddError(string.Format(I18n.Runtime.Validation.ValidationException, ex.Message));
            }

            return result;
        }

        /// <summary>
        /// 验证集合索引
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="collectionPath">集合路径</param>
        /// <param name="index">索引</param>
        /// <returns>验证结果</returns>
        public static ValidationResult ValidateCollectionIndex(this object obj, string collectionPath, int index)
        {
            var result = new ValidationResult { Path = $"{collectionPath}[{index}]" };

            var pathResult = obj.ValidatePropertyPath(collectionPath);
            if (!pathResult.IsValid)
            {
                result.Errors.AddRange(pathResult.Errors);
                return result;
            }

            try
            {
                var collection = PropertyAccessor.GetValue<object>(obj, collectionPath);
                
                if (collection == null)
                {
                    result.AddError(I18n.Runtime.Error.CollectionNull);
                    return result;
                }

                int count = 0;
                if (collection is System.Collections.IList list)
                {
                    count = list.Count;
                }
                else if (collection is Array array)
                {
                    count = array.Length;
                }
                else
                {
                    result.AddError(I18n.Runtime.Validation.InvalidCollectionType);
                    return result;
                }

                if (index < 0 || index >= count)
                {
                    result.AddError(string.Format(I18n.Runtime.Validation.IndexOutOfRange, index, count));
                }
                else
                {
                    result.IsValid = true;
                }
            }
            catch (Exception ex)
            {
                result.AddError(string.Format(I18n.Runtime.Validation.CollectionValidationException, ex.Message));
            }

            return result;
        }

        /// <summary>
        /// 获取路径的类型信息
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <returns>类型信息，如果路径无效则返回null</returns>
        public static Type GetPathType(this object obj, string path)
        {
            try
            {
                var value = PropertyAccessor.GetValue<object>(obj, path);
                return value?.GetType();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 检查路径是否可写
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <returns>是否可写</returns>
        public static bool IsPathWritable(this object obj, string path)
        {
            try
            {
                // 尝试获取当前值
                var currentValue = PropertyAccessor.GetValue<object>(obj, path);
                
                // 尝试设置相同的值
                PropertyAccessor.SetValue(obj, path, currentValue);
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}