using UnityEngine;

#if TREENODE_CHINESE
using Language = TreeNode.Utility.Chinese;
#else
using Language = TreeNode.Utility.English;
#endif

namespace TreeNode.Utility
{
    public class I18n
    {
        public static string CurrentLanguage = typeof(Language).Name;


        public const string TreeNode = Language.TreeNode;
        public const string Hotkeys = Language.Hotkeys;
        public const string ForceReloadIcon = Language.ForceReloadIcon;
        public const string CreateNode = Language.CreateNode;
        public const string Copy = Language.Copy;
        public const string Paste = Language.Paste;
        public const string Cut = Language.Cut;
        public const string Delete = Language.Delete;
        public const string SetRoot = Language.SetRoot;
        public const string Confirm = Language.Confirm;

        public const string Format = Language.Format;

        public const string MoveUp = Language.MoveUp;
        public const string Move2Top = Language.Move2Top;
        public const string MoveDown = Language.MoveDown;
        public const string Move2Bottom = Language.Move2Bottom;
        public const string SetIndex = Language.SetIndex;
        public const string DeleteItem = Language.DeleteItem;
        public const string SetID = Language.SetID;
        public const string Goto = Language.Goto;

    }
}
