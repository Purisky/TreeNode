using UnityEngine;

namespace TreeNode.Utility
{
    public class English : Language
    {
        public const string SelfName = "English";
        public const string ChangeLanguage = "Change Language";

        // Existing menu items
        public const string TreeNode = "Tree Node";
        public const string Hotkeys = "Hotkeys";
        public const string ForceReloadIcon = "Force Reload Icon";
        public const string ForceReloadView = "Reload View";
        public const string CreateNode = "Create Node";
        public const string PrintNodePath = "Print Node Path";
        public const string PrintFieldPath = "Print Field Path";
        public const string Copy = "Copy";
        public const string Paste = "Paste";
        public const string Cut = "Cut";
        public const string Delete = "Delete";
        public const string SetRoot = "Set Root";
        public const string EditNode = "Edit Node";
        public const string Format = "Format";
        public const string ShowTreeView = "Show Tree View";
        public const string EnumNothing = "Nothing";
        public const string EnumEverything = "Everything";
        public const string MoveUp = "Move Up";
        public const string Move2Top = "Move to Top";
        public const string MoveDown = "Move Down";
        public const string Move2Bottom = "Move to Bottom";
        public const string SetIndex = "Set Index";
        public const string DeleteItem = "Delete Item";
        public const string SetID = "Set ID";
        public const string Goto = "Go to";

        // Editor - Dialogs
        public const string Editor_Dialog_PasteFailed = "Paste Failed";
        public const string Editor_Dialog_PasteValidation = "Paste Node Validation";
        public const string Editor_Dialog_Confirm = "Confirm";

        // Editor - Buttons
        public const string Editor_Button_Cancel = "Cancel Operation";
        public const string Editor_Button_RemoveInvalidAndContinue = "Remove Invalid Nodes and Continue";
        public const string Editor_Button_Confirm = "OK";

        // Runtime - Error Messages (with formatting parameters)
        public const string Runtime_Error_PathEmpty = "Path cannot be empty";
        public const string Runtime_Error_TargetNull = "Target object is null";
        public const string Runtime_Error_CollectionNull = "Collection is null";
        public const string Runtime_Error_TypeMismatch = "Type mismatch at path '{0}': expected '{1}', actual '{2}'";
        public const string Runtime_Error_PathNotFound = "Path '{0}' not found in type '{1}', valid length is {2}";
        public const string Runtime_Error_MemberInaccessible = "Member '{0}' is not accessible in type '{1}'";
        public const string Runtime_Error_NestedCollectionNotSupported = "Nested collection at path '{0}' is not supported, use List<JsonNode> instead";
        public const string Runtime_Error_ValueTypeRootModification = "Cannot modify root object of value type";

        // Runtime - Warning Messages (with format parameters)
        public const string Runtime_Warning_InitializationFailed = "Initialization failed: {0}";
        public const string Runtime_Warning_PropertyRefreshFailed = "Property refresh failed {0}: {1}";
        public const string Runtime_Warning_InvalidPortReference = "Invalid port reference found {0}";
        public const string Runtime_Warning_PortInconsistency = "Port inconsistency detected key:{0} actual path:{1}";

        // Runtime - Validation Messages (with format parameters)
        public const string Runtime_Validation_TargetNull = "Target object is null";
        public const string Runtime_Validation_PathEmpty = "Property path cannot be empty";
        public const string Runtime_Validation_PathInvalid = "Path is invalid, valid length: {0}";
        public const string Runtime_Validation_Exception = "Validation exception: {0}";
        public const string Runtime_Validation_RequiredPathInvalid = "Required path '{0}' is invalid: {1}";
        public const string Runtime_Validation_RequiredPathNull = "Required path '{0}' value is null";
        public const string Runtime_Validation_AccessException = "Exception occurred while accessing path '{0}': {1}";
        public const string Runtime_Validation_TypeConversionFailed = "Type conversion failed: {0}";
        public const string Runtime_Validation_InvalidCollectionType = "Object is not a valid collection type";
        public const string Runtime_Validation_IndexOutOfRange = "Index {0} is out of range [0, {1})";
        public const string Runtime_Validation_CollectionException = "Collection validation exception: {0}";

        // Runtime - Message Prefixes (for formatting)
        public const string Runtime_Message_ErrorPrefix = "Error: {0}";
        public const string Runtime_Message_WarningPrefix = "Warning: {0}";
    }
}