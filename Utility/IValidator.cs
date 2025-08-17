using UnityEngine;

namespace TreeNode.Utility
{
    public interface IValidator
    {
        ValidationResult Validate(out string msg);
    }
    public enum ValidationResult
    {
        Success,
        Warning,
        Failure,
    }

}
