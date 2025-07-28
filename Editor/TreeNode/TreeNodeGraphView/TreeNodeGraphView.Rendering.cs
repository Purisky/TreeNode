using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView//异步渲染系统
    {

        // 异步渲染状态管理
        private bool _isRenderingAsync = false;
        private readonly object _renderLock = new ();
        private CancellationTokenSource _renderCancellationSource;
        /// <summary>
        /// 异步渲染所有节点和边
        /// </summary>
        private async Task DrawNodesAsync()
        {
            lock (_renderLock)
            {
                if (_isRenderingAsync)
                {
                    _renderCancellationSource?.Cancel();
                }
                _isRenderingAsync = true;
                _renderCancellationSource = new System.Threading.CancellationTokenSource();
            }

            var cancellationToken = _renderCancellationSource.Token;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 阶段1: 在主线程准备ViewNode数据（避免Unity API线程问题）
                var viewNodeCreationTasks = await PrepareViewNodesAsync(cancellationToken);

                // 阶段2: 批量添加ViewNode到UI（在主线程执行）
                await AddViewNodesToUIAsync(viewNodeCreationTasks, cancellationToken);

                // 阶段3: 创建Edge连接（在主线程执行）
                await CreateEdgesAsync(cancellationToken);

                stopwatch.Stop();
                //Debug.Log($"Async rendering completed in {stopwatch.ElapsedMilliseconds}ms for {ViewNodes.Count} nodes");
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Async rendering was cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError($"Async rendering failed: {e.Message}\n{e.StackTrace}");

                // 在异常情况下，回退到同步渲染
                try
                {
                    Debug.Log("Falling back to synchronous rendering...");
                    DrawNodesSynchronously();
                }
                catch (Exception fallbackException)
                {
                    Debug.LogError($"Fallback synchronous rendering also failed: {fallbackException.Message}");
                }
            }
            finally
            {
                lock (_renderLock)
                {
                    _isRenderingAsync = false;
                    _renderCancellationSource?.Dispose();
                    _renderCancellationSource = null;
                }
            }
        }

        /// <summary>
        /// 同步渲染备用方案
        /// </summary>
        private void DrawNodesSynchronously()
        {
            Debug.Log("Using synchronous rendering as fallback");

            var sortedMetadata = _nodeTree.GetSortedNodes();

            // 创建ViewNode
            foreach (var metadata in sortedMetadata)
            {
                if (!NodeDic.ContainsKey(metadata.Node))
                {
                    var creationTask = PrepareViewNodeCreation(metadata);
                    if (creationTask != null)
                    {
                        CreateAndAddViewNode(creationTask);
                    }
                }
            }

            // 创建Edge连接
            foreach (var metadata in sortedMetadata)
            {
                if (metadata.Parent != null)
                {
                    if (NodeDic.TryGetValue(metadata.Node, out var childViewNode) &&
                        NodeDic.TryGetValue(metadata.Parent.Node, out var parentViewNode))
                    {
                        CreateEdgeConnection(parentViewNode, childViewNode, metadata);
                    }
                }
            }
        }

        /// <summary>
        /// 并行准备ViewNode数据
        /// </summary>
        private async Task<List<ViewNodeCreationTask>> PrepareViewNodesAsync(System.Threading.CancellationToken cancellationToken)
        {
            var sortedMetadata = _nodeTree.GetSortedNodes();
            var creationTasks = new List<ViewNodeCreationTask>();

            // 在主线程中预处理所有ViewNode创建数据，避免在后台线程中调用Unity API
            await ExecuteOnMainThreadAsync(() =>
            {
                foreach (var metadata in sortedMetadata)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var creationTask = PrepareViewNodeCreation(metadata);
                    if (creationTask != null)
                    {
                        creationTasks.Add(creationTask);
                    }
                }
            });

            return creationTasks;
        }

        /// <summary>
        /// 准备单个ViewNode的创建数据
        /// </summary>
        private ViewNodeCreationTask PrepareViewNodeCreation(JsonNodeTree.NodeMetadata metadata)
        {
            var node = metadata.Node;

            // 跳过已存在的节点
            if (NodeDic.ContainsKey(node))
                return null;

            // 预处理节点类型信息
            var nodeType = node.GetType();
            var nodeInfo = nodeType.GetCustomAttribute<NodeInfoAttribute>();

            return new ViewNodeCreationTask
            {
                Node = node,
                Metadata = metadata,
                NodeType = nodeType,
                NodeInfo = nodeInfo,
                Position = new Rect(node.Position, Vector2.zero)
            };
        }

        /// <summary>
        /// 批量添加ViewNode到UI
        /// </summary>
        private async Task AddViewNodesToUIAsync(List<ViewNodeCreationTask> creationTasks, System.Threading.CancellationToken cancellationToken)
        {
            const int batchSize = 25; // 减少批次大小以提高响应性
            const int maxBatchesPerFrame = 2; // 每帧最多处理2个批次

            for (int i = 0; i < creationTasks.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = creationTasks.Skip(i).Take(batchSize);

                // 直接在主线程执行UI操作，避免不必要的线程切换
                foreach (var task in batch)
                {
                    CreateAndAddViewNode(task);
                }

                // 定期让出控制权，保持编辑器响应性
                if ((i / batchSize) % maxBatchesPerFrame == 0)
                {
                    // 使用Unity编辑器友好的延时方式，避免Unity API调用
                    await Task.Yield();

                    // 给编辑器一些处理时间，不使用Unity Time API
                    await Task.Delay(16, cancellationToken); // 约1帧的时间（60fps = 16.67ms）
                }
            }
        }

        /// <summary>
        /// 创建并添加ViewNode到图表
        /// </summary>
        private void CreateAndAddViewNode(ViewNodeCreationTask creationTask)
        {
            if (NodeDic.ContainsKey(creationTask.Node))
                return;

            ViewNode viewNode;
            if (creationTask.Node.PrefabData != null)
            {
                viewNode = new PrefabViewNode(creationTask.Node, this);
            }
            else
            {
                viewNode = new ViewNode(creationTask.Node, this);
            }

            viewNode.SetPosition(creationTask.Position);
            ViewNodes.Add(viewNode);
            NodeDic.Add(creationTask.Node, viewNode);
            AddElement(viewNode);

            // 使用同步方式初始化子节点以提升性能
            viewNode.AddChildNodesSynchronously();
        }

        /// <summary>
        /// 在主线程执行操作
        /// </summary>
        private async Task ExecuteOnMainThreadAsync(System.Action action)
        {
            // 简化主线程检测，避免复杂的线程ID判断
            // 在Unity编辑器环境中，大多数情况下我们已经在主线程
            try
            {
                // 直接执行操作，如果不在主线程会抛出异常
                action();
                return;
            }
            catch (System.InvalidOperationException)
            {
                // 如果不在主线程，使用调度器
            }

            var tcs = new TaskCompletionSource<bool>();

            // 使用Unity编辑器的调度器确保在主线程执行
            schedule.Execute(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            await tcs.Task;
        }

        /// <summary>
        /// ViewNode创建任务数据结构
        /// </summary>
        internal class ViewNodeCreationTask
        {
            public JsonNode Node { get; set; }
            public JsonNodeTree.NodeMetadata Metadata { get; set; }
            public Type NodeType { get; set; }
            public NodeInfoAttribute NodeInfo { get; set; }
            public Rect Position { get; set; }
        }

    }
}
