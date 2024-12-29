using UnityEngine;

namespace TreeNode.Utility
{
    public abstract class Singleton<T> where T : class, new()
    {
        private static readonly T instance = new();
        static Singleton() { }
        protected Singleton() { Init(); }
        public static T Inst => instance;
        public virtual void Init() { }
    }
}
