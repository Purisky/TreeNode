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
    public partial class TreeNodeGraphView//Rendering
    {
        // 异步渲染状态管理
        private bool _isRenderingAsync = false;
        private readonly object _renderLock = new();
        private CancellationTokenSource _renderCancellationSource;

        // 渲染任务队列和并发控制
        private readonly ConcurrentQueue<Func<Task>> _renderTasks = new();
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
        /// 并行准备ViewNode数据 - 使用逻辑层优化的排序
        /// </summary>
        private async Task<List<ViewNodeCreationTask>> PrepareViewNodesAsync(CancellationToken cancellationToken)
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
        /// 并行创建Edge连接 - 基于逻辑层的层次结构
        /// </summary>
        private async Task CreateEdgesAsync(System.Threading.CancellationToken cancellationToken)
        {
            // 在主线程中收集所有需要创建边的元数据
            var edgeMetadataList = new List<JsonNodeTree.NodeMetadata>();

            await ExecuteOnMainThreadAsync(() =>
            {
                foreach (var metadata in _nodeTree.GetSortedNodes())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (metadata.Parent != null)
                    {
                        edgeMetadataList.Add(metadata);
                    }
                }
            });

            // 现在可以安全地并行创建边连接
            var edgeCreationTasks = new List<Task>();

            foreach (var metadata in edgeMetadataList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 直接在主线程上创建边连接，避免线程切换开销
                var task = CreateEdgeForNodeAsync(metadata, cancellationToken);
                edgeCreationTasks.Add(task);
            }

            await Task.WhenAll(edgeCreationTasks);
        }

        /// <summary>
        /// 为指定节点创建边连接
        /// </summary>
        private async Task CreateEdgeForNodeAsync(JsonNodeTree.NodeMetadata childMetadata, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 在主线程执行UI操作
            await ExecuteOnMainThreadAsync(() =>
            {
                if (NodeDic.TryGetValue(childMetadata.Node, out var childViewNode) &&
                    NodeDic.TryGetValue(childMetadata.Parent.Node, out var parentViewNode))
                {
                    CreateEdgeConnection(parentViewNode, childViewNode, childMetadata);
                }
            });
        }

        /// <summary>
        /// 创建具体的边连接
        /// </summary>
        private void CreateEdgeConnection(ViewNode parentViewNode, ViewNode childViewNode, JsonNodeTree.NodeMetadata childMetadata)
        {
            // 查找对应的ChildPort
            var childPort = FindChildPortByName(parentViewNode, childMetadata.PortName, childMetadata.IsMultiPort, childMetadata.ListIndex);
            if (childPort != null && childViewNode.ParentPort != null)
            {
                var edge = childPort.ConnectTo(childViewNode.ParentPort);
                AddElement(edge);

                // 设置多端口索引
                if (childMetadata.IsMultiPort)
                {
                    childViewNode.ParentPort.SetIndex(childMetadata.ListIndex);
                }
            }
        }

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
