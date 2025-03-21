using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
namespace TreeNode.Utility
{
    public static class ExceptionExtensions
    {
        public static void ThrowIfNull([NotNull] this object? argument, string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
            }
        }
        public static void ThrowIfNullOrEmpty([NotNull] this string? argument, string? paramName = null)
        {
            if (string.IsNullOrEmpty(argument))
            {
                throw new ArgumentException($"Property path cannot be null or empty:{paramName}");
            }
        }




    }
}
