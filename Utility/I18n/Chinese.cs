using UnityEngine;

namespace TreeNode.Utility
{
    public class Chinese : Language
    {
        public const string SelfName = "中文";
        public const string ChangeLanguage = "切换语言";

        // 现有的菜单项
        public const string TreeNode = "树节点";
        public const string Hotkeys = "热键";
        public const string ForceReloadIcon = "刷新图标";
        public const string ForceReloadView = "重新加载";
        public const string CreateNode = "创建节点";
        public const string PrintNodePath = "打印节点路径";
        public const string PrintFieldPath = "打印字段路径";
        public const string Copy = "复制";
        public const string Paste = "粘贴";
        public const string Cut = "剪切";
        public const string Delete = "删除";
        public const string SetRoot = "设置根节点";
        public const string EditNode = "编辑节点";
        public const string EnumNothing = "无";
        public const string EnumEverything = "全部";
        public const string Format = "整理节点";
        public const string ShowTreeView = "显示树视图";
        public const string MoveUp = "向上移动";
        public const string Move2Top = "移动到顶部";
        public const string MoveDown = "向下移动";
        public const string Move2Bottom = "移动到底部";
        public const string SetIndex = "设置索引";
        public const string DeleteItem = "删除项目";
        public const string SetID = "设置ID";
        public const string Goto = "前往";

        // 编辑器 - 对话框
        public const string Editor_Dialog_PasteFailed = "粘贴失败";
        public const string Editor_Dialog_PasteValidation = "粘贴节点验证";
        public const string Editor_Dialog_Confirm = "确认";

        // 编辑器 - 按钮
        public const string Editor_Button_Cancel = "取消操作";
        public const string Editor_Button_RemoveInvalidAndContinue = "剔除不合法节点并继续";
        public const string Editor_Button_Confirm = "确定";

        // 运行时 - 错误信息 (带格式化参数)
        public const string Runtime_Error_PathEmpty = "路径不能为空";
        public const string Runtime_Error_TargetNull = "目标对象为null";
        public const string Runtime_Error_CollectionNull = "集合为null";
        public const string Runtime_Error_TypeMismatch = "路径 '{0}' 类型不匹配: 期望 '{1}', 实际 '{2}'";
        public const string Runtime_Error_PathNotFound = "路径 '{0}' 在类型 '{1}' 中未找到，有效长度为 {2}";
        public const string Runtime_Error_MemberInaccessible = "成员 '{0}' 在类型 '{1}' 中不可访问";
        public const string Runtime_Error_NestedCollectionNotSupported = "嵌套集合在路径 '{0}' 中不被支持,使用List<JsonNode>替代";
        public const string Runtime_Error_ValueTypeRootModification = "无法修改值类型的根对象";

        // 运行时 - 警告信息 (带格式化参数)
        public const string Runtime_Warning_InitializationFailed = "初始化失败: {0}";
        public const string Runtime_Warning_PropertyRefreshFailed = "属性刷新失败 {0}: {1}";
        public const string Runtime_Warning_InvalidPortReference = "发现无效的端口引用 {0}";
        public const string Runtime_Warning_PortInconsistency = "发现不一致的端口 键:{0} 实际路径:{1}";

        // 运行时 - 验证信息 (带格式化参数)
        public const string Runtime_Validation_TargetNull = "目标对象为null";
        public const string Runtime_Validation_PathEmpty = "属性路径不能为空";
        public const string Runtime_Validation_PathInvalid = "路径无效，有效部分长度: {0}";
        public const string Runtime_Validation_Exception = "验证异常: {0}";
        public const string Runtime_Validation_RequiredPathInvalid = "必需路径 '{0}' 无效: {1}";
        public const string Runtime_Validation_RequiredPathNull = "必需路径 '{0}' 的值为null";
        public const string Runtime_Validation_AccessException = "访问路径 '{0}' 时发生异常: {1}";
        public const string Runtime_Validation_TypeConversionFailed = "类型转换失败: {0}";
        public const string Runtime_Validation_InvalidCollectionType = "对象不是有效的集合类型";
        public const string Runtime_Validation_IndexOutOfRange = "索引 {0} 超出范围 [0, {1})";
        public const string Runtime_Validation_CollectionException = "集合验证异常: {0}";

        // 运行时 - 消息前缀 (用于格式化)
        public const string Runtime_Message_ErrorPrefix = "错误: {0}";
        public const string Runtime_Message_WarningPrefix = "警告: {0}";
    }
}