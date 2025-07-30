using UnityEngine;

namespace TreeNode.Runtime
{
    public struct PAPath
    {
        public PAPart[] Parts;


        public PAPath(string path)
        {
            Parts = new PAPart[0];
        }
    }
    public struct PAPart
    {
        public int Index;
        public string Name;

        public readonly bool IsIndex => Name == null;
    }
}
