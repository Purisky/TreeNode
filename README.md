# TreeNode 0.1.0

## 项目概述

TreeNode 是一个基于 Unity UI Toolkit 开发的节点编辑器系统，使用 JSON 进行数据存储，专门用于创建和管理树状结构的数据。该项目的灵感主要来源于 xNode 和 Odin Inspector，提供了直观的可视化节点编辑界面。

**核心特性：**
- 🌳 **树状结构支持**：专门针对树形数据结构优化的节点编辑器
- 🎨 **UI Toolkit 驱动**：基于 Unity UI Toolkit 构建，提供现代化的编辑体验
- 💾 **JSON 存储**：使用 JSON 格式进行数据持久化，便于版本控制和外部处理
- ⚡ **高性能**：使用源代码生成技术消除运行时反射开销
- 🔧 **可扩展**：支持自定义节点类型和属性绘制器
- 🌍 **多语言**：内置中英文国际化支持

## 安装说明

1. 将 TreeNode 文件夹复制到 Unity 项目的 `Assets` 目录下
2. 确保项目启用了 UI Toolkit 包
3. 源代码生成器会自动编译并生成必要的代码

## 核心架构

### 组件层次

```
TreeNode/
├── Runtime/           # 运行时组件
│   ├── Node/         # 节点基类和实现
│   ├── Property/     # 属性访问系统
│   ├── Asset/        # 资源管理
│   └── History/      # 撤销/重做系统
├── Editor/           # 编辑器专用组件
│   ├── TreeNode/     # 核心编辑器组件
│   ├── Drawer/       # 属性绘制器
│   ├── Template/     # 模板系统
│   └── JsonAsset/    # JSON 资源处理
├── Generator~/       # 源代码生成器项目
├── Plugins/          # 生成的 DLL 文件
└── Utility/          # 共享工具类
```

### 关键系统

**属性访问系统**
- 使用源代码生成技术创建高效的属性访问器
- 支持嵌套属性导航，使用 `PAPath` 路径系统
- 反射数据缓存优化性能

**节点图系统**
- 树状节点关系（仅支持父子关系）
- 可视化节点编辑，支持拖拽连接
- 🚧 **模板系统（开发中）**：计划用于可重用的节点配置

**资源管道**
- 基于 JSON 的资源存储，保留类型信息
- 自定义 `.json` 文件资源导入器
- 🚧 **模板资源系统（开发中）**：计划用于节点预制体


### 模板系统（开发中）

⚠️ **注意**：模板系统目前正在开发中，以下为计划功能：

1. 创建模板资源继承 `TemplateData`
2. 使用 `TemplateNode` 实例化模板
3. 模板将可以保存节点配置供重复使用

当前版本中，模板相关的基础架构已经存在，但完整的编辑功能尚未实现。

## 开发指南

### 构建源代码生成器

```bash
# 进入源代码生成器目录
cd "E:\Work\Github\SkillEditorDemo\Assets\TreeNode\Generator~"

# 构建项目
dotnet build TreeNodeSourceGenerator.csproj
```

构建完成后，生成的 DLL 会自动复制到 `../Plugins/TreeNodeSourceGenerator.dll`

### 代码约定

**JsonNode 实现**
- 继承自 `JsonNode` 基类
- 使用 `[GenIPropertyAccessor]` 特性自动生成属性访问器
- 使用 `[NoJsonNodeContainer]` 标记不应包含其他节点的类型

**属性访问**
- 使用 `PropertyAccessor.GetValue<T>(obj, path)` 和 `PropertyAccessor.SetValue<T>(obj, path)`
- 路径支持点符号和数组索引：`"parent.child[0].property"`
- 生成的访问器提供编译时类型安全

**模板系统（开发中）**
- 模板将实现 `TemplateData` 抽象类
- 将使用 `TemplateNode` 创建基于模板的节点实例
- 当前仅提供基础架构，完整功能正在开发中

## 更新日志

### v0.1.0
- 初始版本发布
- 基础节点编辑器功能
- JSON 存储支持
- 源代码生成器集成
- 中英文国际化支持
