using UnityEngine;

#if TREENODE_CHINESE
using Current = TreeNode.Utility.Chinese;
#elif TREENODE_FRENCH
using Current = TreeNode.Utility.French;
#else
using Current = TreeNode.Utility.English;
#endif

namespace TreeNode.Utility
{
    public class I18n
    {
        public static string CurrentLanguage = typeof(Current).Name;

        public const string SelfName = Current.SelfName;
        public const string ChangeLanguage = Current.ChangeLanguage;


        public const string TreeNode = Current.TreeNode;
        public const string Hotkeys = Current.Hotkeys;
        public const string ForceReloadIcon = Current.ForceReloadIcon;
        public const string CreateNode = Current.CreateNode;
        public const string PrintNodePath = Current.PrintNodePath;
        public const string PrintFieldPath = Current.PrintFieldPath;
        public const string Copy = Current.Copy;
        public const string Paste = Current.Paste;
        public const string Cut = Current.Cut;
        public const string Delete = Current.Delete;

        public const string SetRoot = Current.SetRoot;
        public const string Confirm = Current.Confirm;
        public const string EditNode = Current.EditNode;

        public const string Format = Current.Format;

        public const string EnumNothing = Current.EnumNothing;
        public const string EnumEverything = Current.EnumEverything;



        public const string MoveUp = Current.MoveUp;
        public const string Move2Top = Current.Move2Top;
        public const string MoveDown = Current.MoveDown;
        public const string Move2Bottom = Current.Move2Bottom;
        public const string SetIndex = Current.SetIndex;
        public const string DeleteItem = Current.DeleteItem;
        public const string SetID = Current.SetID;
        public const string Goto = Current.Goto;

    }

    public abstract class Language { 
        


    }

}
