using UnityEngine;

#if TREENODE_CHINESE
using Current = TreeNode.Utility.Chinese;
#else
using Current = TreeNode.Utility.English;
#endif

namespace TreeNode.Utility
{
    public class I18n
    {
        public static string CurrentLanguage = typeof(Current).Name;

        // 基础信息
        public const string SelfName = Current.SelfName;
        public const string ChangeLanguage = Current.ChangeLanguage;

        // 编辑器相关
        public static class Editor
        {
            // 对话框相关
            public static class Dialog
            {
                public const string PasteFailed = Current.Editor_Dialog_PasteFailed;
                public const string PasteValidation = Current.Editor_Dialog_PasteValidation;
                public const string Confirm = Current.Editor_Dialog_Confirm;
            }

            // 按钮相关
            public static class Button
            {
                public const string Cancel = Current.Editor_Button_Cancel;
                public const string RemoveInvalidAndContinue = Current.Editor_Button_RemoveInvalidAndContinue;
                public const string Confirm = Current.Editor_Button_Confirm;
            }

            // 菜单和操作
            public static class Menu
            {
                public const string TreeNode = Current.TreeNode;
                public const string Hotkeys = Current.Hotkeys;
                public const string ForceReloadIcon = Current.ForceReloadIcon;
                public const string ForceReloadView = Current.ForceReloadView;
                public const string CreateNode = Current.CreateNode;
                public const string PrintNodePath = Current.PrintNodePath;
                public const string PrintFieldPath = Current.PrintFieldPath;
                public const string Copy = Current.Copy;
                public const string Paste = Current.Paste;
                public const string Cut = Current.Cut;
                public const string Delete = Current.Delete;
                public const string SetRoot = Current.SetRoot;
                public const string EditNode = Current.EditNode;
                public const string Format = Current.Format;
                public const string ShowTreeView = Current.ShowTreeView;
            }

            // 列表操作
            public static class List
            {
                public const string MoveUp = Current.MoveUp;
                public const string Move2Top = Current.Move2Top;
                public const string MoveDown = Current.MoveDown;
                public const string Move2Bottom = Current.Move2Bottom;
                public const string SetIndex = Current.SetIndex;
                public const string DeleteItem = Current.DeleteItem;
                public const string SetID = Current.SetID;
                public const string Goto = Current.Goto;
            }

            // 枚举相关
            public static class Enum
            {
                public const string Nothing = Current.EnumNothing;
                public const string Everything = Current.EnumEverything;
            }
        }

        // 运行时相关
        public static class Runtime
        {
            // 错误信息
            public static class Error
            {
                public const string PathEmpty = Current.Runtime_Error_PathEmpty;
                public const string TargetNull = Current.Runtime_Error_TargetNull;
                public const string CollectionNull = Current.Runtime_Error_CollectionNull;
                public const string TypeMismatch = Current.Runtime_Error_TypeMismatch;
                public const string PathNotFound = Current.Runtime_Error_PathNotFound;
                public const string IndexOutOfRange = Current.Runtime_Validation_IndexOutOfRange;
                public const string MemberNotAccessible = Current.Runtime_Error_MemberInaccessible;
                public const string NestedCollectionNotSupported = Current.Runtime_Error_NestedCollectionNotSupported;
                public const string ValueTypeRootModification = Current.Runtime_Error_ValueTypeRootModification;
            }

            // 警告信息
            public static class Warning
            {
                public const string InitializationFailed = Current.Runtime_Warning_InitializationFailed;
                public const string PropertyRefreshFailed = Current.Runtime_Warning_PropertyRefreshFailed;
                public const string InvalidPortReference = Current.Runtime_Warning_InvalidPortReference;
                public const string PortInconsistency = Current.Runtime_Warning_PortInconsistency;
            }

            // 验证信息
            public static class Validation
            {
                public const string TargetNull = Current.Runtime_Validation_TargetNull;
                public const string PathEmpty = Current.Runtime_Validation_PathEmpty;
                public const string PathInvalid = Current.Runtime_Validation_PathInvalid;
                public const string ValidationException = Current.Runtime_Validation_Exception;
                public const string RequiredPathInvalid = Current.Runtime_Validation_RequiredPathInvalid;
                public const string RequiredPathNull = Current.Runtime_Validation_RequiredPathNull;
                public const string AccessException = Current.Runtime_Validation_AccessException;
                public const string TypeConversionFailed = Current.Runtime_Validation_TypeConversionFailed;
                public const string InvalidCollectionType = Current.Runtime_Validation_InvalidCollectionType;
                public const string IndexOutOfRange = Current.Runtime_Validation_IndexOutOfRange;
                public const string CollectionValidationException = Current.Runtime_Validation_CollectionException;
            }

            // 消息类型
            public static class Message
            {
                public const string ErrorPrefix = Current.Runtime_Message_ErrorPrefix;
                public const string WarningPrefix = Current.Runtime_Message_WarningPrefix;
            }
        }
    }

    public abstract class Language { 
        


    }

}
