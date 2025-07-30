# Operations.cs 激进统一重构计划

## 1. 重构背景与目标

### 1.1 当前状态分析
- `EdgeCreateOperation` 和 `EdgeRemoveOperation` 目前为 `TODO: Implement` 状态
- `NodeMoveOperation` 只有框架代码，Execute/Undo 返回 true 但未实现具体逻辑
- 三个操作本质上都是节点位置的变更，存在概念重复和代码冗余

### 1.2 激进重构目标
- **彻底统一**: 完全移除 `EdgeCreateOperation` 和 `EdgeRemoveOperation`
- **API 重新设计**: 不考虑向后兼容，重新设计更清晰的API
- **性能最优**: 移除多态调用开销，统一代码路径
- **概念简化**: 开发者只需理解一个操作类型：位置变更

## 2. 激进设计方案

### 2.1 核心概念重新定义
```text
所有节点操作都是位置变更：
- 创建节点 = Unknown → Location
- 删除节点 = Location → Deleted  
- 移动节点 = Location1 → Location2
- 创建边 = Root → Parent.Port
- 删除边 = Parent.Port → Root

统一操作类型: NodeLocationChange
```

### 2.2 新的操作枚举设计
```csharp
// 重新设计操作类型 - 更细粒度
public enum NodeOperationType
{
    // 基础位置变更
    LocationChange,      // 通用位置变更
    
    // 语义化别名（内部映射到LocationChange）
    Create,             // Unknown → Location
    Delete,             // Location → Deleted
    Move,               // Location → Location
    Connect,            // Root → Parent.Port
    Disconnect,         // Parent.Port → Root
}

// 移除原有的 OperationType.EdgeCreate/EdgeRemove
// 统一使用 NodeOperationType
```

## 3. 激进实现计划

### 阶段1: 完全重写核心操作类 (2天)

#### 任务1.1: 设计全新的NodeLocationChangeOperation
```csharp
/// <summary>
/// 统一的节点位置变更操作 - 替代所有原有操作类型
/// </summary>
public sealed class NodeLocationChangeOperation : IAtomicOperation
{
    // 强制使用新的操作类型系统
    public OperationType Type => MapToLegacyType();
    public DateTime Timestamp { get; }
    public string Description { get; }
    
    // 新的核心属性
    public NodeOperationType OperationType { get; }
    public JsonNode Node { get; }
    public NodeLocation FromLocation { get; }
    public NodeLocation ToLocation { get; }  
    public TreeNodeGraphView GraphView { get; }

    // 私有构造函数，强制使用工厂方法
    private NodeLocationChangeOperation(
        NodeOperationType operationType,
        JsonNode node, 
        NodeLocation from, 
        NodeLocation to, 
        TreeNodeGraphView graphView)
    {
        OperationType = operationType;
        Node = node;
        FromLocation = from;
        ToLocation = to;
        GraphView = graphView;
        Timestamp = DateTime.Now;
        Description = GenerateDescription();
    }

    // 工厂方法 - 唯一的创建入口
    public static NodeLocationChangeOperation Create(JsonNode node, NodeLocation to, TreeNodeGraphView graphView)
        => new(NodeOperationType.Create, NodeLocation.Unknown(), to, graphView);

    public static NodeLocationChangeOperation Delete(JsonNode node, TreeNodeGraphView graphView)
        => new(NodeOperationType.Delete, node.GetCurrentLocation(graphView), NodeLocation.Deleted(), graphView);

    public static NodeLocationChangeOperation Move(JsonNode node, NodeLocation to, TreeNodeGraphView graphView)
        => new(NodeOperationType.Move, node.GetCurrentLocation(graphView), to, graphView);

    public static NodeLocationChangeOperation Connect(JsonNode childNode, JsonNode parentNode, string portName, TreeNodeGraphView graphView, int listIndex = -1)
    {
        var from = childNode.GetCurrentLocation(graphView);
        var to = NodeLocation.Child(parentNode, portName, listIndex >= 0, listIndex);
        return new(NodeOperationType.Connect, childNode, from, to, graphView);
    }

    public static NodeLocationChangeOperation Disconnect(JsonNode childNode, JsonNode parentNode, string portName, TreeNodeGraphView graphView, int listIndex = -1)
    {
        var from = NodeLocation.Child(parentNode, portName, listIndex >= 0, listIndex);
        var to = NodeLocation.Root(-1); // 移回根级别
        return new(NodeOperationType.Disconnect, childNode, from, to, graphView);
    }
}
```

#### 任务1.2: 统一的执行逻辑
```csharp
public bool Execute()
{
    try
    {
        // 统一的事务性执行逻辑
        using var transaction = new NodeLocationTransaction(GraphView);
        
        // 1. 验证操作合法性
        if (!ValidateOperation())
        {
            Debug.LogError($"操作验证失败: {Description}");
            return false;
        }

        // 2. 从原位置移除（如果需要）
        if (ShouldRemoveFromSource())
        {
            if (!RemoveFromLocation(FromLocation))
            {
                Debug.LogError($"从原位置移除节点失败: {FromLocation.GetFullPath()}");
                return false;
            }
        }

        // 3. 添加到目标位置（如果需要）
        if (ShouldAddToTarget())
        {
            if (!AddToLocation(ToLocation))
            {
                Debug.LogError($"添加节点到目标位置失败: {ToLocation.GetFullPath()}");
                transaction.Rollback();
                return false;
            }
        }

        // 4. 更新视图状态
        UpdateViewState();
        
        transaction.Commit();
        Debug.Log($"成功执行: {Description}");
        return true;
    }
    catch (Exception e)
    {
        Debug.LogError($"执行节点位置变更失败: {e.Message}");
        return false;
    }
}

public bool Undo()
{
    // 创建反向操作
    var reverseOp = CreateReverseOperation();
    return reverseOp.Execute();
}

private NodeLocationChangeOperation CreateReverseOperation()
{
    // 根据操作类型创建精确的反向操作
    return OperationType switch
    {
        NodeOperationType.Create => new(NodeOperationType.Delete, Node, ToLocation, FromLocation, GraphView),
        NodeOperationType.Delete => new(NodeOperationType.Create, Node, ToLocation, FromLocation, GraphView),
        NodeOperationType.Move => new(NodeOperationType.Move, Node, ToLocation, FromLocation, GraphView),
        NodeOperationType.Connect => new(NodeOperationType.Disconnect, Node, ToLocation, FromLocation, GraphView),
        NodeOperationType.Disconnect => new(NodeOperationType.Connect, Node, ToLocation, FromLocation, GraphView),
        _ => throw new InvalidOperationException($"未知操作类型: {OperationType}")
    };
}
```

#### 任务1.3: 事务性位置管理器
```csharp
/// <summary>
/// 事务性节点位置管理器 - 确保操作的原子性
/// </summary>
public sealed class NodeLocationTransaction : IDisposable
{
    private readonly TreeNodeGraphView _graphView;
    private readonly Dictionary<JsonNode, NodeLocation> _originalLocations = new();
    private readonly List<Action> _rollbackActions = new();
    private bool _committed = false;

    public NodeLocationTransaction(TreeNodeGraphView graphView)
    {
        _graphView = graphView;
    }

    public void RecordNodeLocation(JsonNode node)
    {
        if (!_originalLocations.ContainsKey(node))
        {
            _originalLocations[node] = node.GetCurrentLocation(_graphView);
        }
    }

    public bool MoveNode(JsonNode node, NodeLocation from, NodeLocation to)
    {
        RecordNodeLocation(node);
        
        try
        {
            // 原子性移动：先添加到目标位置，再从原位置移除
            if (to.Type != LocationType.Deleted && to.Type != LocationType.Unknown)
            {
                if (!AddNodeToLocation(node, to))
                    return false;
                    
                _rollbackActions.Add(() => RemoveNodeFromLocation(node, to));
            }

            if (from.Type != LocationType.Unknown)
            {
                if (!RemoveNodeFromLocation(node, from))
                {
                    // 回滚已执行的添加操作
                    if (_rollbackActions.Count > 0)
                    {
                        _rollbackActions[^1]();
                        _rollbackActions.RemoveAt(_rollbackActions.Count - 1);
                    }
                    return false;
                }
                
                _rollbackActions.Add(() => AddNodeToLocation(node, from));
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"移动节点失败: {e.Message}");
            return false;
        }
    }

    public void Commit()
    {
        _committed = true;
        _rollbackActions.Clear();
        
        // 触发视图更新
        UpdateAffectedViews();
    }

    public void Rollback()
    {
        if (_committed) return;
        
        // 执行回滚操作（逆序）
        for (int i = _rollbackActions.Count - 1; i >= 0; i--)
        {
            try
            {
                _rollbackActions[i]();
            }
            catch (Exception e)
            {
                Debug.LogError($"回滚操作失败: {e.Message}");
            }
        }
        
        _rollbackActions.Clear();
    }

    public void Dispose()
    {
        if (!_committed)
        {
            Rollback();
        }
    }

    private void UpdateAffectedViews()
    {
        // 批量更新所有受影响的ViewNode
        var affectedViewNodes = _originalLocations.Keys
            .Where(node => _graphView.NodeDic.ContainsKey(node))
            .Select(node => _graphView.NodeDic[node])
            .ToList();

        foreach (var viewNode in affectedViewNodes)
        {
            viewNode.RefreshState();
        }
    }
}
```

### 阶段2: 激进API重设计 (1天)

#### 任务2.1: 移除所有旧操作类
```csharp
// 完全删除以下类，不保留任何兼容性
// - EdgeCreateOperation
// - EdgeRemoveOperation  
// - 原有的NodeMoveOperation

// 重新设计History记录接口
public static class HistoryOperations
{
    // 简化的API - 只暴露必要的操作
    public static void RecordCreate(this History history, JsonNode node, NodeLocation location)
    {
        var operation = NodeLocationChangeOperation.Create(node, location, history.Window.GraphView);
        history.RecordOperation(operation);
    }

    public static void RecordDelete(this History history, JsonNode node)
    {
        var operation = NodeLocationChangeOperation.Delete(node, history.Window.GraphView);
        history.RecordOperation(operation);
    }

    public static void RecordMove(this History history, JsonNode node, NodeLocation toLocation)
    {
        var operation = NodeLocationChangeOperation.Move(node, toLocation, history.Window.GraphView);
        history.RecordOperation(operation);
    }

    public static void RecordConnect(this History history, JsonNode child, JsonNode parent, string portName, int listIndex = -1)
    {
        var operation = NodeLocationChangeOperation.Connect(child, parent, portName, history.Window.GraphView, listIndex);
        history.RecordOperation(operation);
    }

    public static void RecordDisconnect(this History history, JsonNode child, JsonNode parent, string portName, int listIndex = -1)
    {
        var operation = NodeLocationChangeOperation.Disconnect(child, parent, portName, history.Window.GraphView, listIndex);
        history.RecordOperation(operation);
    }
}
```

#### 任务2.2: 重新设计调用接口
```csharp
// 在TreeNodeGraphView中的调用示例
public class TreeNodeGraphView
{
    // 旧代码：复杂的操作创建
    // var createOp = new NodeCreateOperation(node, location, this);
    // History.RecordOperation(createOp);

    // 新代码：简化的语义化调用
    public void CreateNode(JsonNode node, NodeLocation location)
    {
        History.RecordCreate(node, location);
    }

    public void DeleteNode(JsonNode node) 
    {
        History.RecordDelete(node);
    }

    public void MoveNode(JsonNode node, NodeLocation location)
    {
        History.RecordMove(node, location);
    }

    public void ConnectNodes(JsonNode child, JsonNode parent, string portName, int listIndex = -1)
    {
        History.RecordConnect(child, parent, portName, listIndex);
    }

    public void DisconnectNodes(JsonNode child, JsonNode parent, string portName, int listIndex = -1)
    {
        History.RecordDisconnect(child, parent, portName, listIndex);
    }
}
```

### 阶段3: 全面更新调用点 (1天)

#### 任务3.1: 批量替换所有调用点
```csharp
// 使用全局搜索替换，更新所有使用旧操作的代码

// 旧模式替换规则：
// EdgeCreateOperation(...) → History.RecordConnect(...)
// EdgeRemoveOperation(...) → History.RecordDisconnect(...)
// NodeCreateOperation(...) → History.RecordCreate(...)
// NodeDeleteOperation(...) → History.RecordDelete(...)
// new NodeMoveOperation(...) → History.RecordMove(...)
```

#### 任务3.2: 更新History系统内部逻辑
```csharp
public partial class History
{
    // 移除旧的操作类型处理逻辑
    private bool AreCompatibleOperationTypes(OperationType type1, OperationType type2)
    {
        // 简化：所有NodeLocationChangeOperation都兼容
        return true; // 因为只有一种操作类型
    }

    // 简化合并逻辑
    private bool CanMergeToStep(HistoryStep step, IAtomicOperation operation)
    {
        if (operation is not NodeLocationChangeOperation) return false;
        
        // 时间窗口检查
        var timeSinceStep = DateTime.Now - step.Timestamp;
        return timeSinceStep.TotalMilliseconds <= 1000;
    }

    // 移除复杂的操作类型判断
    private OperationType GetUnifiedOperationType(IAtomicOperation operation)
    {
        return operation.Type; // 直接返回，无需映射
    }
}
```

### 阶段4: 性能优化和测试 (1天)

#### 任务4.1: 激进性能优化
```csharp
// 移除多态调用，使用直接类型检查
public static class OperationOptimizations
{
    // 快速类型检查
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLocationChange(this IAtomicOperation operation)
    {
        return operation is NodeLocationChangeOperation;
    }

    // 快速类型转换
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static NodeLocationChangeOperation AsLocationChange(this IAtomicOperation operation)
    {
        return (NodeLocationChangeOperation)operation;
    }

    // 批量操作优化
    public static void ExecuteBatch(IEnumerable<NodeLocationChangeOperation> operations)
    {
        using var batchTransaction = new BatchLocationTransaction();
        
        foreach (var op in operations)
        {
            batchTransaction.AddOperation(op);
        }
        
        batchTransaction.ExecuteAll();
    }
}
```

#### 任务4.2: 全面测试重写
```csharp
[TestClass]
public class NodeLocationChangeOperationTests
{
    [TestMethod]
    public void TestCreateOperation()
    {
        var node = CreateTestNode();
        var location = NodeLocation.Root(0);
        var graphView = CreateTestGraphView();
        
        var operation = NodeLocationChangeOperation.Create(node, location, graphView);
        
        Assert.IsTrue(operation.Execute());
        Assert.AreEqual(NodeOperationType.Create, operation.OperationType);
        Assert.IsTrue(graphView.Window.JsonAsset.Data.Nodes.Contains(node));
        
        Assert.IsTrue(operation.Undo());
        Assert.IsFalse(graphView.Window.JsonAsset.Data.Nodes.Contains(node));
    }

    [TestMethod]
    public void TestConnectDisconnectCycle()
    {
        var child = CreateTestNode();
        var parent = CreateTestNode();
        var graphView = CreateTestGraphView();
        
        // 初始状态：子节点在根级别
        graphView.Window.JsonAsset.Data.Nodes.Add(child);
        
        // 连接操作
        var connectOp = NodeLocationChangeOperation.Connect(child, parent, "TestPort", graphView);
        Assert.IsTrue(connectOp.Execute());
        Assert.IsFalse(graphView.Window.JsonAsset.Data.Nodes.Contains(child)); // 不再在根级别
        
        // 断开操作
        var disconnectOp = NodeLocationChangeOperation.Disconnect(child, parent, "TestPort", graphView);
        Assert.IsTrue(disconnectOp.Execute());
        Assert.IsTrue(graphView.Window.JsonAsset.Data.Nodes.Contains(child)); // 回到根级别
        
        // 测试撤销
        Assert.IsTrue(disconnectOp.Undo()); // 撤销断开
        Assert.IsFalse(graphView.Window.JsonAsset.Data.Nodes.Contains(child)); // 重新连接
        
        Assert.IsTrue(connectOp.Undo()); // 撤销连接
        Assert.IsTrue(graphView.Window.JsonAsset.Data.Nodes.Contains(child)); // 回到根级别
    }
}
```

## 4. 激进重构的优势

### 4.1 代码简化
- **单一操作类型**: 消除多态调用开销
- **统一代码路径**: 所有位置变更使用相同逻辑
- **零概念负担**: 开发者只需理解"位置变更"一个概念

### 4.2 性能提升
- **编译时优化**: 移除接口调用，直接方法调用
- **内存效率**: 单一类型，减少对象多态存储开销
- **缓存友好**: 统一的数据结构布局

### 4.3 维护便利
- **代码量减少**: 移除约70%的操作相关代码
- **调试简化**: 单一入口点，统一的日志和错误处理
- **扩展便利**: 新功能只需修改一个类

## 5. 激进实施计划

### 第1天: 核心重写
- **上午 (4小时)**: 实现NodeLocationChangeOperation核心功能
- **下午 (4小时)**: 实现事务性位置管理器

### 第2天: API重设计  
- **上午 (4小时)**: 设计新的History扩展方法API
- **下午 (4小时)**: 重写TreeNodeGraphView调用接口

### 第3天: 全面替换
- **上午 (4小时)**: 批量搜索替换所有调用点
- **下午 (4小时)**: 移除所有旧操作类和相关代码

### 第4天: 优化测试
- **上午 (4小时)**: 性能优化和内联优化
- **下午 (4小时)**: 编写全面的测试用例

### 第5天: 验证部署
- **上午 (4小时)**: 集成测试和性能基准测试
- **下午 (4小时)**: 文档更新和部署验证

## 6. 破坏性变更清单

### 6.1 移除的类和接口
- ✅ `EdgeCreateOperation` - 完全移除
- ✅ `EdgeRemoveOperation` - 完全移除  
- ✅ 原有的 `NodeMoveOperation` - 完全重写
- ✅ 相关的工厂类和扩展方法

### 6.2 API变更
- ✅ 所有直接创建操作对象的代码需要修改
- ✅ History.RecordOperation调用需要更新为新的扩展方法
- ✅ 操作类型检查逻辑需要更新

### 6.3 配置和序列化
- ✅ 历史记录序列化格式变更（不支持旧格式）
- ✅ 配置文件中的操作类型引用需要更新

## 7. 风险与收益评估

### 7.1 风险
- **一次性破坏**: 所有现有代码需要同时更新
- **测试负担**: 需要重新测试所有相关功能
- **回滚困难**: 变更范围大，难以部分回滚

### 7.2 收益
- **性能提升**: 预期30-50%的操作执行性能提升
- **代码简化**: 减少70%的操作相关代码量
- **概念统一**: 完全消除概念歧义和学习成本
- **维护便利**: 单一代码路径，极大简化调试和扩展

## 8. 总结

激进重构方案通过完全移除旧的操作类型，采用统一的NodeLocationChangeOperation，实现了：

1. **概念极简化**: 所有操作都是位置变更
2. **性能最大化**: 移除多态开销，统一代码路径  
3. **维护最优化**: 单一操作类型，最小代码量
4. **API现代化**: 语义化的扩展方法，更直观的调用方式

虽然破坏性较大，但收益显著，适合在大版本更新中实施。重构完成后，系统将具备更好的性能、更低的维护成本和更清晰的架构。