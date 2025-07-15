using UnityEngine;

namespace TreeNode.Utility
{
    public interface IValidator
    {
        bool Validate(out string msg);
    }
}
