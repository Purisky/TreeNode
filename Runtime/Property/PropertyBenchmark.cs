using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Runtime.Property.Extensions;
using Unity.Properties;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// PropertyAccessor 性能基准测试工具
/// 专注于PropertyAccessor和PropertyContainer之间的性能对比测试
/// </summary>
public static class PropertyBenchmark
{
    #region 测试配置

    private const int WarmupCount = 1000;
    private const int TestCount = 10000;
    private const int MinDepth = 1;
    private const int MaxDepth = 5;
    private const int HeavyTestCount = 100000; // 用于重型测试

    #endregion

    #region 测试状态跟踪

    private static int totalTests = 0;
    private static int successfulTests = 0;
    private static int failedTests = 0;
    private static List<string> testErrors = new List<string>();

    #endregion

    #region 测试数据结构

    private struct TestStruct
    {
        public int StructValue;
        public string StructName;
        public Vector3 Position;
    }

    private class TestClass
    {
        public TestClass Child { get; set; }
        public int PropertyValue { get; set; }
        public string Name { get; set; }
        public int FieldValue;
        public TestStruct StructField;
        public List<TestClass> Items { get; set; }
        public int[] Array { get; set; }
        public TestClass[] ClassArray { get; set; }
        public Dictionary<string, TestClass> Dictionary { get; set; }
        public Dictionary<int, string> SimpleDict { get; set; }
    }

    #endregion

    #region 公共API

    /// <summary>
    /// 运行完整的性能基准测试套件
    /// </summary>
    public static void RunAllPerformanceTests()
    {
        InitializeTestSession();
        Debug.Log("=== PropertyAccessor vs PropertyContainer 性能基准测试 ===");
        
        try
        {
            // 1. 基础性能测试
            SafeExecuteTest("基础性能对比", RunBasicPerformanceComparison);
            
            // 2. 嵌套深度性能测试
            SafeExecuteTest("嵌套深度性能对比", RunNestedDepthComparison);
            
            // 3. 不同访问类型性能测试
            SafeExecuteTest("访问类型性能对比", RunAccessTypeComparison);
            
            // 4. 集合访问性能测试
            SafeExecuteTest("集合访问性能对比", RunCollectionPerformanceComparison);
            
            // 5. 缓存效果测试
            SafeExecuteTest("缓存效果测试", RunCachePerformanceTest);
            
            // 6. 大数据量测试
            SafeExecuteTest("大数据量测试", RunBulkOperationTest);
        }
        finally
        {
            LogTestSummary();
        }

        Debug.Log("=== 所有性能测试完成 ===");
    }

    /// <summary>
    /// 运行快速性能验证
    /// </summary>
    public static void RunQuickPerformanceTest()
    {
        InitializeTestSession();
        Debug.Log("=== 快速性能验证测试 ===");
        
        try
        {
            var testObj = SafeCreateTestObject();
            if (testObj == null)
            {
                Debug.LogError("无法创建测试对象，快速验证终止");
                return;
            }
            
            // 快速验证两个系统的基础功能
            SafeExecuteTest("基础功能验证", () => ValidateBasicFunctionality(testObj));
            
            // 简单性能对比
            SafeExecuteTest("简单性能对比", () => RunSimplePerformanceComparison(testObj));
        }
        finally
        {
            LogTestSummary();
        }
        
        Debug.Log("=== 快速验证完成 ===");
    }

    /// <summary>
    /// 运行指定类型的性能测试
    /// </summary>
    public static void RunSpecificPerformanceTest(PerformanceTestType testType)
    {
        InitializeTestSession();
        
        try
        {
            string testName = testType.ToString();
            Action testAction = testType switch
            {
                PerformanceTestType.BasicComparison => RunBasicPerformanceComparison,
                PerformanceTestType.NestedDepth => RunNestedDepthComparison,
                PerformanceTestType.AccessTypes => RunAccessTypeComparison,
                PerformanceTestType.Collections => RunCollectionPerformanceComparison,
                PerformanceTestType.Cache => RunCachePerformanceTest,
                PerformanceTestType.BulkOperations => RunBulkOperationTest,
                PerformanceTestType.ReadOnly => RunReadOnlyPerformanceTests,
                PerformanceTestType.WriteOnly => RunWriteOnlyPerformanceTests,
                PerformanceTestType.MixedReadWrite => RunMixedReadWritePerformanceTests,
                _ => () => Debug.LogWarning($"未知的测试类型: {testType}")
            };
            
            SafeExecuteTest(testName, testAction);
        }
        finally
        {
            LogTestSummary();
        }
    }

    /// <summary>
    /// 运行综合分析测试（包括路径支持性验证和优化的性能对比）
    /// </summary>
    public static void RunComprehensiveAnalysis()
    {
        InitializeTestSession();
        Debug.Log("=== 综合分析测试 ===");
        
        try
        {
            // 1. 基础性能对比
            SafeExecuteTest("基础性能对比", RunBasicPerformanceComparison);
            
            // 2. 优化的嵌套深度性能对比
            SafeExecuteTest("优化嵌套深度对比", RunNestedDepthComparison);
            
            // 3. 路径支持性验证将在方法末尾单独添加
            Debug.Log("提示: 可单独调用 RunPathSupportValidation() 进行路径支持性分析");
        }
        finally
        {
            LogTestSummary();
        }
        
        Debug.Log("=== 综合分析完成 ===");
    }

    public enum PerformanceTestType
    {
        BasicComparison,
        NestedDepth,
        AccessTypes,
        Collections,
        Cache,
        BulkOperations,
        ReadOnly,
        WriteOnly,
        MixedReadWrite
    }

    #endregion

    #region 安全执行包装器

    /// <summary>
    /// 初始化测试会话
    /// </summary>
    private static void InitializeTestSession()
    {
        totalTests = 0;
        successfulTests = 0;
        failedTests = 0;
        testErrors.Clear();
    }

    /// <summary>
    /// 安全执行测试
    /// </summary>
    private static void SafeExecuteTest(string testName, Action testAction)
    {
        totalTests++;
        try
        {
            testAction?.Invoke();
            successfulTests++;
        }
        catch (Exception ex)
        {
            failedTests++;
            string errorMsg = $"{testName}测试失败: {ex.Message}";
            testErrors.Add(errorMsg);
            Debug.LogError(errorMsg);
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// 安全创建测试对象
    /// </summary>
    private static TestClass SafeCreateTestObject()
    {
        try
        {
            return CreateTestObject();
        }
        catch (Exception ex)
        {
            Debug.LogError($"创建测试对象失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 记录测试总结
    /// </summary>
    private static void LogTestSummary()
    {
        Debug.Log($"=== 测试会话总结 ===");
        Debug.Log($"总测试数: {totalTests}, 成功: {successfulTests}, 失败: {failedTests}");
        
        if (testErrors.Count > 0)
        {
            Debug.LogWarning($"失败的测试错误列表:");
            foreach (var error in testErrors)
            {
                Debug.LogWarning($"  - {error}");
            }
        }
    }

    /// <summary>
    /// 安全运行性能测试
    /// </summary>
    private static long SafeRunPerformanceTest(string testType, object testObj, string path, Action<object, string> testAction)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            testAction(testObj, path);
            sw.Stop();
            return sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogError($"{testType}性能测试失败 (路径: {path}): {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// 安全预热两个系统
    /// </summary>
    private static void SafeWarmupBothSystems(object testObj, string path)
    {
        try
        {
            // PropertyAccessor预热
            for (int i = 0; i < WarmupCount; i++)
            {
                PropertyAccessor.GetValue<object>(testObj, path);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PropertyAccessor预热失败 (路径: {path}): {ex.Message}");
        }

        try
        {
            // PropertyContainer预热（如果支持）
            if (SafeIsPropertyContainerSupported(testObj, path))
            {
                var propertyPath = new PropertyPath(path);
                for (int i = 0; i < WarmupCount; i++)
                {
                    PropertyContainer.GetValue<TestClass, object>((TestClass)testObj, propertyPath);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PropertyContainer预热失败 (路径: {path}): {ex.Message}");
        }
    }

    #endregion

    #region 核心性能测试

    /// <summary>
    /// 基础性能对比测试
    /// </summary>
    private static void RunBasicPerformanceComparison()
    {
        Debug.Log("--- 基础性能对比测试 ---");
        var testObj = SafeCreateTestObject();
        if (testObj == null) return;
        
        var testScenarios = new[]
        {
            ("简单属性访问", "PropertyValue"),
            ("字段访问", "FieldValue"),
            ("字符串属性", "Name"),
            ("嵌套属性", "Child.PropertyValue")
        };

        foreach (var (testName, path) in testScenarios)
        {
            SafeExecuteTest($"基础性能-{testName}", () => PerformAccessorVsContainerTest(testObj, path, testName));
        }
    }

    /// <summary>
    /// 嵌套深度性能对比
    /// </summary>
    private static void RunNestedDepthComparison()
    {
        Debug.Log("--- 嵌套深度性能对比 ---");
        
        for (int depth = MinDepth; depth <= MaxDepth; depth++)
        {
            int currentDepth = depth; // 避免闭包问题
            SafeExecuteTest($"嵌套深度-{currentDepth}", () => 
            {
                var testObj = CreateNestedObjectForComparison(currentDepth);
                var supportedPaths = GetMutuallySupportedPaths(testObj, currentDepth);
                
                if (supportedPaths.Count == 0)
                {
                    Debug.LogWarning($"深度{currentDepth}没有找到两个系统都支持的路径");
                    return;
                }

                Debug.Log($"深度{currentDepth}测试 - 共同支持的路径数: {supportedPaths.Count}");
                
                // 对每个支持的路径进行测试
                foreach (var pathInfo in supportedPaths)
                {
                    SafeExecuteTest($"深度{currentDepth}-{pathInfo.Description}", () =>
                    {
                        PerformAccessorVsContainerTest(testObj, pathInfo.Path, $"深度{currentDepth}-{pathInfo.Description}");
                    });
                }
            });
        }
    }

    /// <summary>
    /// 路径信息结构
    /// </summary>
    private struct PathInfo
    {
        public string Path { get; set; }
        public string Description { get; set; }

        public PathInfo(string path, string description)
        {
            Path = path;
            Description = description;
        }
    }

    /// <summary>
    /// 获取两个系统都支持的路径列表
    /// </summary>
    private static List<PathInfo> GetMutuallySupportedPaths(TestClass testObj, int depth)
    {
        var supportedPaths = new List<PathInfo>();
        
        // 定义候选路径 - 避免使用集合索引，优先使用属性链
        var candidatePaths = new List<PathInfo>();
        
        if (depth == 1)
        {
            candidatePaths.AddRange(new[]
            {
                new PathInfo("PropertyValue", "根属性"),
                new PathInfo("Name", "根字符串属性"),
                new PathInfo("FieldValue", "根字段")
            });
        }
        else
        {
            // 构建基于Child属性的嵌套路径（PropertyContainer更容易支持）
            var pathBuilder = "Child";
            var descBuilder = "Child";
            
            for (int i = 2; i <= depth; i++)
            {
                if (i == depth)
                {
                    // 最后一层测试不同类型的属性
                    candidatePaths.AddRange(new[]
                    {
                        new PathInfo($"{pathBuilder}.PropertyValue", $"{descBuilder}.属性"),
                        new PathInfo($"{pathBuilder}.Name", $"{descBuilder}.字符串"),
                        new PathInfo($"{pathBuilder}.FieldValue", $"{descBuilder}.字段")
                    });
                }
                else
                {
                    pathBuilder += ".Child";
                    descBuilder += ".Child";
                }
            }
        }
        
        // 验证每个候选路径是否被两个系统都支持
        foreach (var pathInfo in candidatePaths)
        {
            bool paSupported = SafeIsPathValid(testObj, pathInfo.Path);
            bool pcSupported = SafeIsPropertyContainerSupported(testObj, pathInfo.Path);
            
            if (paSupported && pcSupported)
            {
                supportedPaths.Add(pathInfo);
                Debug.Log($"  ✓ 路径支持: {pathInfo.Path} - {pathInfo.Description}");
            }
            else
            {
                Debug.Log($"  ✗ 路径不支持: {pathInfo.Path} - PA:{paSupported}, PC:{pcSupported}");
            }
        }
        
        return supportedPaths;
    }

    /// <summary>
    /// 创建用于比较测试的嵌套对象
    /// 确保有足够的嵌套Child属性供PropertyContainer访问
    /// </summary>
    private static TestClass CreateNestedObjectForComparison(int depth)
    {
        var root = new TestClass
        {
            PropertyValue = 0,
            FieldValue = 0,
            Name = "Root",
            Items = new List<TestClass>(),
            Array = new int[] { 0, 1, 2 }
        };

        var current = root;
        for (int i = 1; i < depth; i++)
        {
            var child = new TestClass
            {
                PropertyValue = i * 10,
                FieldValue = i * 5,
                Name = $"Child_{i}",
                Items = new List<TestClass>(),
                Array = new int[] { i * 10, i * 10 + 1, i * 10 + 2 }
            };

            current.Child = child;
            current = child;
        }
        
        return root;
    }

    /// <summary>
    /// 访问类型性能对比
    /// </summary>
    private static void RunAccessTypeComparison()
    {
        Debug.Log("--- 访问类型性能对比 ---");
        var testObj = SafeCreateTestObject();
        if (testObj == null) return;
        
        var accessTypes = new Dictionary<string, string>
        {
            ["属性访问"] = "PropertyValue",
            ["字段访问"] = "FieldValue", 
            ["结构体字段"] = "StructField.StructValue",
            ["结构体字符串"] = "StructField.StructName"
        };

        foreach (var kvp in accessTypes)
        {
            SafeExecuteTest($"访问类型-{kvp.Key}", () =>
            {
                if (SafeIsPathValid(testObj, kvp.Value))
                {
                    PerformAccessorVsContainerTest(testObj, kvp.Value, kvp.Key);
                }
                else
                {
                    Debug.LogWarning($"{kvp.Key}路径无效: {kvp.Value}");
                }
            });
        }
    }

    /// <summary>
    /// 集合访问性能对比
    /// </summary>
    private static void RunCollectionPerformanceComparison()
    {
        Debug.Log("--- 集合访问性能对比 ---");
        var testObj = SafeCreateTestObject();
        if (testObj == null) return;
        
        var collectionTests = new Dictionary<string, string>
        {
            ["List索引"] = "Items[0].PropertyValue",
            ["数组索引"] = "Array[0]",
            ["对象数组"] = "ClassArray[0].PropertyValue"
        };

        foreach (var kvp in collectionTests)
        {
            SafeExecuteTest($"集合访问-{kvp.Key}", () =>
            {
                if (SafeIsPathValid(testObj, kvp.Value))
                {
                    PerformAccessorVsContainerTest(testObj, kvp.Value, kvp.Key);
                }
                else
                {
                    Debug.LogWarning($"{kvp.Key}路径无效: {kvp.Value}");
                }
            });
        }
    }

    /// <summary>
    /// 缓存效果性能测试
    /// </summary>
    private static void RunCachePerformanceTest()
    {
        Debug.Log("--- 缓存效果性能测试 ---");
        var testObj = SafeCreateTestObject();
        if (testObj == null) return;
        
        var testPaths = new[] { "PropertyValue", "FieldValue", "Child.PropertyValue" }
            .Where(path => SafeIsPathValid(testObj, path))
            .ToArray();

        if (testPaths.Length == 0)
        {
            Debug.LogWarning("没有有效路径进行缓存测试");
            return;
        }

        foreach (var path in testPaths)
        {
            Debug.Log($"  缓存测试 - 路径: {path} - 运行次数: {TestCount}");
            
            // PropertyAccessor 缓存测试
            var paFirstRun = SafeRunCacheTest($"PropertyAccessor首次[读取×{TestCount}]", new[] { path }, (testPath) =>
            {
                for (int i = 0; i < TestCount; i++)
                {
                    PropertyAccessor.GetValue<object>(testObj, testPath);
                }
            });

            var paSecondRun = SafeRunCacheTest($"PropertyAccessor再次[读取×{TestCount}]", new[] { path }, (testPath) =>
            {
                for (int i = 0; i < TestCount; i++)
                {
                    PropertyAccessor.GetValue<object>(testObj, testPath);
                }
            });

            // PropertyContainer 缓存测试
            long pcFirstRun = 0, pcSecondRun = 0;
            bool containerSupported = SafeIsPropertyContainerSupported(testObj, path);
            
            if (containerSupported)
            {
                pcFirstRun = SafeRunCacheTest($"PropertyContainer首次[读取×{TestCount}]", new[] { path }, (testPath) =>
                {
                    var propertyPath = new PropertyPath(testPath);
                    for (int i = 0; i < TestCount; i++)
                    {
                        PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                    }
                });

                pcSecondRun = SafeRunCacheTest($"PropertyContainer再次[读取×{TestCount}]", new[] { path }, (testPath) =>
                {
                    var propertyPath = new PropertyPath(testPath);
                    for (int i = 0; i < TestCount; i++)
                    {
                        PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                    }
                });
            }

            // 输出缓存测试结果
            Debug.Log($"    【PropertyAccessor缓存效果】路径: {path}");
            if (paFirstRun > 0 && paSecondRun > 0)
            {
                Debug.Log($"      首次[读取×{TestCount}]: {paFirstRun}ticks");
                Debug.Log($"      再次[读取×{TestCount}]: {paSecondRun}ticks");
                Debug.Log($"      缓存改善率: {(float)paFirstRun/paSecondRun:F2}x");
            }
            
            Debug.Log($"    【PropertyContainer缓存效果】路径: {path}");
            if (containerSupported && pcFirstRun > 0 && pcSecondRun > 0)
            {
                Debug.Log($"      首次[读取×{TestCount}]: {pcFirstRun}ticks");
                Debug.Log($"      再次[读取×{TestCount}]: {pcSecondRun}ticks");
                Debug.Log($"      缓存改善率: {(float)pcFirstRun/pcSecondRun:F2}x");
            }
            else if (containerSupported)
            {
                Debug.Log($"      PropertyContainer缓存测试失败");
            }
            else
            {
                Debug.Log($"      PropertyContainer不支持此路径");
            }
        }
    }

    /// <summary>
    /// 安全运行缓存测试
    /// </summary>
    private static long SafeRunCacheTest(string testName, string[] paths, Action<string> testAction)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            foreach (var path in paths)
            {
                testAction(path);
            }
            sw.Stop();
            return sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogError($"{testName}缓存测试失败: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// 大数据量操作性能测试
    /// </summary>
    private static void RunBulkOperationTest()
    {
        Debug.Log("--- 大数据量操作性能测试 ---");
        var testObj = SafeCreateLargeTestObject();
        if (testObj == null) return;
        
        var testPath = "PropertyValue";
        Debug.Log($"大数据量测试 - 路径: {testPath} - 运行次数: {HeavyTestCount}");
        
        // PropertyAccessor 大数据量测试
        long paHeavyTime = SafeRunBulkTest($"PropertyAccessor大数据量[读写×{HeavyTestCount}]", testObj, testPath, (obj, path) =>
        {
            for (int i = 0; i < HeavyTestCount; i++)
            {
                var value = PropertyAccessor.GetValue<int>(obj, path);
                PropertyAccessor.SetValue(obj, path, value + 1);
            }
        });

        // PropertyContainer 大数据量测试（如果支持）
        long pcHeavyTime = 0;
        bool containerSupported = SafeIsPropertyContainerSupported(testObj, testPath);
        
        if (containerSupported)
        {
            pcHeavyTime = SafeRunBulkTest($"PropertyContainer大数据量[读写×{HeavyTestCount}]", testObj, testPath, (obj, path) =>
            {
                var propertyPath = new PropertyPath(path);
                for (int i = 0; i < HeavyTestCount; i++)
                {
                    var value = PropertyContainer.GetValue<TestClass, int>((TestClass)obj, propertyPath);
                    PropertyContainer.SetValue((TestClass)obj, propertyPath, value + 1);
                }
            });
        }

        // 输出大数据量测试结果
        Debug.Log($"  【大数据量测试结果】路径: {testPath}");
        if (paHeavyTime > 0)
        {
            Debug.Log($"    PropertyAccessor[读写×{HeavyTestCount}]: {paHeavyTime}ticks ({paHeavyTime/(float)HeavyTestCount:F2}ticks/op)");
        }
        if (containerSupported && pcHeavyTime > 0)
        {
            Debug.Log($"    PropertyContainer[读写×{HeavyTestCount}]: {pcHeavyTime}ticks ({pcHeavyTime/(float)HeavyTestCount:F2}ticks/op)");
            if (paHeavyTime > 0)
            {
                Debug.Log($"    大数据量性能比 PA/PC: {(float)paHeavyTime/pcHeavyTime:F2}x");
            }
        }
        else if (containerSupported)
        {
            Debug.Log($"    PropertyContainer[读写×{HeavyTestCount}]: 测试失败");
        }
        else
        {
            Debug.Log($"    PropertyContainer[读写×{HeavyTestCount}]: 不支持此路径");
        }
    }

    /// <summary>
    /// 安全运行大数据量测试
    /// </summary>
    private static long SafeRunBulkTest(string testName, object testObj, string testPath, Action<object, string> testAction)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            testAction(testObj, testPath);
            sw.Stop();
            return sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogError($"{testName}测试失败: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// 安全创建大数据量测试对象
    /// </summary>
    private static TestClass SafeCreateLargeTestObject()
    {
        try
        {
            return CreateLargeTestObject();
        }
        catch (Exception ex)
        {
            Debug.LogError($"创建大数据量测试对象失败: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region 管道性能测试

    /// <summary>
    /// 执行PropertyAccessor vs PropertyContainer性能对比
    /// 分别测试读取和写入性能
    /// </summary>
    private static void PerformAccessorVsContainerTest(object testObj, string path, string testName)
    {
        Debug.Log($"路径测试: {path} ({testName})");
        
        // 预热
        SafeWarmupBothSystems(testObj, path);

        // 1. 读取性能测试
        PerformReadOnlyTest(testObj, path, testName);
        
        // 2. 写入性能测试
        PerformWriteOnlyTest(testObj, path, testName);
        
        // 3. 混合读写性能测试
        PerformReadWriteTest(testObj, path, testName);
    }

    /// <summary>
    /// 执行纯读取性能测试
    /// </summary>
    private static void PerformReadOnlyTest(object testObj, string path, string testName)
    {
        Debug.Log($"  === 读取测试 - 路径: {path} - 运行次数: {TestCount} ===");

        // PropertyAccessor读取测试
        long paReadTime = SafeRunPerformanceTest("PropertyAccessor", testObj, path, (obj, pth) =>
        {
            for (int i = 0; i < TestCount; i++)
            {
                try
                {
                    PropertyAccessor.GetValue<object>(obj, pth);
                }
                catch (Exception ex)
                {
                    if (i == 0)
                    {
                        Debug.LogWarning($"PropertyAccessor读取失败 (路径: {pth}): {ex.Message}");
                    }
                }
            }
        });

        // PropertyContainer读取测试
        long pcReadTime = 0;
        bool containerSupported = SafeIsPropertyContainerSupported(testObj, path);
        
        if (containerSupported)
        {
            pcReadTime = SafeRunPerformanceTest("PropertyContainer", testObj, path, (obj, pth) =>
            {
                var propertyPath = new PropertyPath(pth);
                for (int i = 0; i < TestCount; i++)
                {
                    try
                    {
                        PropertyContainer.GetValue<TestClass, object>((TestClass)obj, propertyPath);
                    }
                    catch (Exception ex)
                    {
                        if (i == 0)
                        {
                            Debug.LogWarning($"PropertyContainer读取失败 (路径: {pth}): {ex.Message}");
                        }
                    }
                }
            });
        }

        // 输出读取测试结果
        LogReadOnlyResults(testName, path, paReadTime, pcReadTime, containerSupported);
    }

    /// <summary>
    /// 执行纯写入性能测试
    /// </summary>
    private static void PerformWriteOnlyTest(object testObj, string path, string testName)
    {
        Debug.Log($"  === 写入测试 - 路径: {path} - 运行次数: {TestCount} ===");

        // PropertyAccessor写入测试
        long paWriteTime = SafeRunPerformanceTest("PropertyAccessor", testObj, path, (obj, pth) =>
        {
            for (int i = 0; i < TestCount; i++)
            {
                try
                {
                    var value = PropertyAccessor.GetValue<object>(obj, pth);
                    var newValue = GenerateNewValue(value);
                    PropertyAccessor.SetValue<object>(obj, pth, newValue);
                }
                catch (Exception ex)
                {
                    if (i == 0)
                    {
                        Debug.LogWarning($"PropertyAccessor写入失败 (路径: {pth}): {ex.Message}");
                    }
                }
            }
        });

        // PropertyContainer写入测试
        long pcWriteTime = 0;
        bool containerSupported = SafeIsPropertyContainerSupported(testObj, path);
        
        if (containerSupported)
        {
            pcWriteTime = SafeRunPerformanceTest("PropertyContainer", testObj, path, (obj, pth) =>
            {
                var propertyPath = new PropertyPath(pth);
                for (int i = 0; i < TestCount; i++)
                {
                    try
                    {
                        var value = PropertyContainer.GetValue<TestClass, object>((TestClass)obj, propertyPath);
                        var newValue = GenerateNewValue(value);
                        PropertyContainer.SetValue((TestClass)obj, propertyPath, newValue);
                    }
                    catch (Exception ex)
                    {
                        if (i == 0)
                        {
                            Debug.LogWarning($"PropertyContainer写入失败 (路径: {pth}): {ex.Message}");
                        }
                    }
                }
            });
        }

        // 输出写入测试结果
        LogWriteOnlyResults(testName, path, paWriteTime, pcWriteTime, containerSupported);
    }

    /// <summary>
    /// 执行混合读写性能测试
    /// </summary>
    private static void PerformReadWriteTest(object testObj, string path, string testName)
    {
        Debug.Log($"  === 混合读写测试 - 路径: {path} - 运行次数: {TestCount} ===");

        // PropertyAccessor混合测试
        long paMixedTime = SafeRunPerformanceTest("PropertyAccessor", testObj, path, (obj, pth) =>
        {
            for (int i = 0; i < TestCount; i++)
            {
                try
                {
                    var value = PropertyAccessor.GetValue<object>(obj, pth);
                    var newValue = GenerateNewValue(value);
                    PropertyAccessor.SetValue<object>(obj, pth, newValue);
                }
                catch (Exception ex)
                {
                    if (i == 0)
                    {
                        Debug.LogWarning($"PropertyAccessor混合操作失败 (路径: {pth}): {ex.Message}");
                    }
                }
            }
        });

        // PropertyContainer混合测试
        long pcMixedTime = 0;
        bool containerSupported = SafeIsPropertyContainerSupported(testObj, path);
        
        if (containerSupported)
        {
            pcMixedTime = SafeRunPerformanceTest("PropertyContainer", testObj, path, (obj, pth) =>
            {
                var propertyPath = new PropertyPath(pth);
                for (int i = 0; i < TestCount; i++)
                {
                    try
                    {
                        var value = PropertyContainer.GetValue<TestClass, object>((TestClass)obj, propertyPath);
                        var newValue = GenerateNewValue(value);
                        PropertyContainer.SetValue((TestClass)obj, propertyPath, newValue);
                    }
                    catch (Exception ex)
                    {
                        if (i == 0)
                        {
                            Debug.LogWarning($"PropertyContainer混合操作失败 (路径: {pth}): {ex.Message}");
                        }
                    }
                }
            });
        }

        // 输出混合测试结果
        LogMixedResults(testName, path, paMixedTime, pcMixedTime, containerSupported);
    }

    /// <summary>
    /// 生成测试用的新值
    /// </summary>
    private static object GenerateNewValue(object currentValue)
    {
        return currentValue switch
        {
            int intVal => intVal + 1,
            float floatVal => floatVal + 1.0f,
            double doubleVal => doubleVal + 1.0,
            string stringVal => stringVal + ".",
            bool boolVal => !boolVal,
            _ => currentValue
        };
    }

    /// <summary>
    /// 记录读取性能测试结果
    /// </summary>
    private static void LogReadOnlyResults(string testName, string path, long paTime, long pcTime, bool containerSupported)
    {
        Debug.Log($"    【读取性能】{testName} - 路径: {path}");
        
        if (paTime > 0)
        {
            Debug.Log($"      PropertyAccessor[读取×{TestCount}]: {paTime}ticks ({paTime/(float)TestCount:F2}ticks/op)");
        }
        else
        {
            Debug.Log($"      PropertyAccessor[读取×{TestCount}]: 测试失败");
        }
        
        if (containerSupported && pcTime > 0)
        {
            float ratio = (float)paTime / pcTime;
            Debug.Log($"      PropertyContainer[读取×{TestCount}]: {pcTime}ticks ({pcTime/(float)TestCount:F2}ticks/op)");
            string comparisonText = ratio > 1 ? "(PA较慢)" : "(PA较快)";
            Debug.Log($"      读取性能比 PA/PC: {ratio:F2}x {comparisonText}");
        }
        else if (containerSupported)
        {
            Debug.Log($"      PropertyContainer[读取×{TestCount}]: 测试失败");
        }
        else
        {
            Debug.Log($"      PropertyContainer[读取×{TestCount}]: 不支持此路径");
        }
    }

    /// <summary>
    /// 记录写入性能测试结果
    /// </summary>
    private static void LogWriteOnlyResults(string testName, string path, long paTime, long pcTime, bool containerSupported)
    {
        Debug.Log($"    【写入性能】{testName} - 路径: {path}");
        
        if (paTime > 0)
        {
            Debug.Log($"      PropertyAccessor[写入×{TestCount}]: {paTime}ticks ({paTime/(float)TestCount:F2}ticks/op)");
        }
        else
        {
            Debug.Log($"      PropertyAccessor[写入×{TestCount}]: 测试失败");
        }
        
        if (containerSupported && pcTime > 0)
        {
            float ratio = (float)paTime / pcTime;
            Debug.Log($"      PropertyContainer[写入×{TestCount}]: {pcTime}ticks ({pcTime/(float)TestCount:F2}ticks/op)");
            string comparisonText = ratio > 1 ? "(PA较慢)" : "(PA较快)";
            Debug.Log($"      写入性能比 PA/PC: {ratio:F2}x {comparisonText}");
        }
        else if (containerSupported)
        {
            Debug.Log($"      PropertyContainer[写入×{TestCount}]: 测试失败");
        }
        else
        {
            Debug.Log($"      PropertyContainer[写入×{TestCount}]: 不支持此路径");
        }
    }

    /// <summary>
    /// 记录混合读写性能测试结果
    /// </summary>
    private static void LogMixedResults(string testName, string path, long paTime, long pcTime, bool containerSupported)
    {
        Debug.Log($"    【混合读写性能】{testName} - 路径: {path}");
        
        if (paTime > 0)
        {
            Debug.Log($"      PropertyAccessor[读写×{TestCount}]: {paTime}ticks ({paTime/(float)TestCount:F2}ticks/op)");
        }
        else
        {
            Debug.Log($"      PropertyAccessor[读写×{TestCount}]: 测试失败");
        }
        
        if (containerSupported && pcTime > 0)
        {
            float ratio = (float)paTime / pcTime;
            Debug.Log($"      PropertyContainer[读写×{TestCount}]: {pcTime}ticks ({pcTime/(float)TestCount:F2}ticks/op)");
            string comparisonText = ratio > 1 ? "(PA较慢)" : "(PA较快)";
            Debug.Log($"      混合性能比 PA/PC: {ratio:F2}x {comparisonText}");
        }
        else if (containerSupported)
        {
            Debug.Log($"      PropertyContainer[读写×{TestCount}]: 测试失败");
        }
        else
        {
            Debug.Log($"      PropertyContainer[读写×{TestCount}]: 不支持此路径");
        }
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 验证基础功能可用性
    /// </summary>
    private static void ValidateBasicFunctionality(TestClass testObj)
    {
        bool paWorking = false;
        bool pcWorking = false;

        // PropertyAccessor基础功能验证
        try
        {
            int value = PropertyAccessor.GetValue<int>(testObj, "PropertyValue");
            PropertyAccessor.SetValue(testObj, "PropertyValue", value + 1);
            int newValue = PropertyAccessor.GetValue<int>(testObj, "PropertyValue");
            paWorking = newValue == value + 1;
        }
        catch (Exception ex)
        {
            Debug.LogError($"PropertyAccessor功能验证失败: {ex.Message}");
        }

        // PropertyContainer基础功能验证
        try
        {
            var propertyPath = new PropertyPath("PropertyValue");
            int pcValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
            PropertyContainer.SetValue(testObj, propertyPath, pcValue + 1);
            int pcNewValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
            pcWorking = pcNewValue == pcValue + 1;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PropertyContainer功能验证失败: {ex.Message}");
        }

        Debug.Log($"功能验证: PropertyAccessor={(paWorking ? "✓" : "✗")}, PropertyContainer={(pcWorking ? "✓" : "✗")}");

        // 补充验证Structure体字段
        try
        {
            var structValuePath = "StructField.StructValue";
            var structNamePath = "StructField.StructName";

            // PropertyAccessor 验证
            int structValue = PropertyAccessor.GetValue<int>(testObj, structValuePath);
            PropertyAccessor.SetValue(testObj, structValuePath, structValue + 1);
            int newStructValue = PropertyAccessor.GetValue<int>(testObj, structValuePath);

            string structName = PropertyAccessor.GetValue<string>(testObj, structNamePath);
            PropertyAccessor.SetValue(testObj, structNamePath, structName + "_updated");
            string newStructName = PropertyAccessor.GetValue<string>(testObj, structNamePath);

            // PropertyContainer 验证
            var propertyPathValue = new PropertyPath(structValuePath);
            var propertyPathName = new PropertyPath(structNamePath);

            int pcStructValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPathValue);
            PropertyContainer.SetValue(testObj, propertyPathValue, pcStructValue + 1);
            int newPcStructValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPathValue);

            string pcStructName = PropertyContainer.GetValue<TestClass, string>(testObj, propertyPathName);
            PropertyContainer.SetValue(testObj, propertyPathName, pcStructName + "_updated");
            string newPcStructName = PropertyContainer.GetValue<TestClass, string>(testObj, propertyPathName);

            // 输出验证结果
            Debug.Log($"结构体字段验证通过: StructValue={newStructValue}, StructName={newStructName}");
            Debug.Log($"PropertyContainer 结构体字段验证通过: StructValue={newPcStructValue}, StructName={newPcStructName}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"结构体字段验证失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 简单性能对比
    /// </summary>
    private static void RunSimplePerformanceComparison(TestClass testObj)
    {
        const int quickTestCount = 1000;
        const string testPath = "PropertyValue";
        
        Debug.Log($"简单性能对比 - 路径: {testPath} - 运行次数: {quickTestCount}");

        // PropertyAccessor读取测试
        long paReadTime = 0;
        try
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < quickTestCount; i++)
            {
                PropertyAccessor.GetValue<int>(testObj, testPath);
            }
            sw.Stop();
            paReadTime = sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogError($"PropertyAccessor读取测试失败: {ex.Message}");
        }

        // PropertyAccessor写入测试
        long paWriteTime = 0;
        try
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < quickTestCount; i++)
            {
                var currentValue = PropertyAccessor.GetValue<int>(testObj, testPath);
                PropertyAccessor.SetValue(testObj, testPath, currentValue + 1);
            }
            sw.Stop();
            paWriteTime = sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogError($"PropertyAccessor写入测试失败: {ex.Message}");
        }

        // PropertyContainer读取测试
        long pcReadTime = 0;
        try
        {
            var propertyPath = new PropertyPath(testPath);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < quickTestCount; i++)
            {
                PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
            }
            sw.Stop();
            pcReadTime = sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PropertyContainer读取测试失败: {ex.Message}");
        }

        // PropertyContainer写入测试
        long pcWriteTime = 0;
        try
        {
            var propertyPath = new PropertyPath(testPath);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < quickTestCount; i++)
            {
                var currentValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
                PropertyContainer.SetValue(testObj, propertyPath, currentValue + 1);
            }
            sw.Stop();
            pcWriteTime = sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PropertyContainer写入测试失败: {ex.Message}");
        }

        // 输出结果
        Debug.Log($"  【读取性能对比】({quickTestCount}次):");
        if (paReadTime > 0)
        {
            Debug.Log($"    PropertyAccessor[读取×{quickTestCount}]: {paReadTime}ticks");
        }
        if (pcReadTime > 0)
        {
            Debug.Log($"    PropertyContainer[读取×{quickTestCount}]: {pcReadTime}ticks, 比率: {(float)paReadTime/pcReadTime:F2}x");
        }
        else
        {
            Debug.Log($"    PropertyContainer[读取×{quickTestCount}]: 不支持或失败");
        }

        Debug.Log($"  【写入性能对比】({quickTestCount}次):");
        if (paWriteTime > 0)
        {
            Debug.Log($"    PropertyAccessor[写入×{quickTestCount}]: {paWriteTime}ticks");
        }
        if (pcWriteTime > 0)
        {
            Debug.Log($"    PropertyContainer[写入×{quickTestCount}]: {pcWriteTime}ticks, 比率: {(float)paWriteTime/pcWriteTime:F2}x");
        }
        else
        {
            Debug.Log($"    PropertyContainer[写入×{quickTestCount}]: 不支持或失败");
        }
    }

    /// <summary>
    /// 诊断写入问题的辅助方法
    /// </summary>
    public static void DiagnoseWriteIssues()
    {
        Debug.Log("=== PropertyAccessor 写入问题诊断 ===");
        
        try
        {
            var testObj = SafeCreateTestObject();
            if (testObj == null)
            {
                Debug.LogError("无法创建测试对象");
                return;
            }

            var testPaths = new[]
            {
                "PropertyValue",
                "FieldValue",
                "Name",
                "Array[0]",
                "Items[0].PropertyValue",
                "Child.PropertyValue",
                "StructField.StructValue"
            };

            foreach (var path in testPaths)
            {
                DiagnosePath(testObj, path);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"诊断过程中发生异常: {ex.Message}");
        }
        
        Debug.Log("=== 诊断完成 ===");
    }

    /// <summary>
    /// 诊断单个路径的写入问题
    /// </summary>
    private static void DiagnosePath(object testObj, string path)
    {
        Debug.Log($"--- 诊断路径: {path} ---");
        
        try
        {
            // 1. 检查路径有效性
            if (!SafeIsPathValid(testObj, path))
            {
                Debug.LogWarning($"  路径无效: {path}");
                return;
            }

            // 2. 尝试读取当前值
            object currentValue;
            try
            {
                currentValue = PropertyAccessor.GetValue<object>(testObj, path);
                Debug.Log($"  当前值: {currentValue} (类型: {currentValue?.GetType().Name ?? "null"})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"  读取失败: {ex.Message}");
                return;
            }

            // 3. 检查写入权限
            var parent = PropertyAccessor.GetParentObject(testObj, path, out var lastMember);
            var parentType = parent.GetType();
            
            if (IsIndexerAccess(lastMember))
            {
                var (collectionName, index) = ParseIndexerAccess(lastMember);
                if (!string.IsNullOrEmpty(collectionName))
                {
                    var collectionMember = parentType.GetProperty(collectionName) ?? 
                                          (MemberInfo)parentType.GetField(collectionName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (collectionMember == null)
                    {
                        Debug.LogWarning($"  集合成员不存在: {collectionName}");
                        return;
                    }
                    Debug.Log($"  集合成员: {collectionName} (类型: {collectionMember.GetValueType().Name})");
                }
            }
            else
            {
                var member = parentType.GetProperty(lastMember) ?? 
                           (MemberInfo)parentType.GetField(lastMember, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (member == null)
                {
                    Debug.LogWarning($"  成员不存在: {lastMember}");
                    return;
                }
                
                bool canWrite = member is PropertyInfo prop ? prop.CanWrite : 
                               member is FieldInfo field ? !field.IsInitOnly : false;
                Debug.Log($"  成员: {lastMember} (类型: {member.GetValueType().Name}, 可写: {canWrite})");
                
                if (!canWrite)
                {
                    Debug.LogWarning($"  成员为只读: {lastMember}");
                    return;
                }
            }

            // 4. 尝试写入测试
            try
            {
                object testValue = currentValue switch
                {
                    int intVal => intVal + 1,
                    float floatVal => floatVal + 1.0f,
                    double doubleVal => doubleVal + 1.0,
                    string stringVal => stringVal + "_test",
                    bool boolVal => !boolVal,
                    _ => currentValue
                };

                PropertyAccessor.SetValue<object>(testObj, path, testValue);
                Debug.Log($"  ✓ 写入成功: {testValue}");

                // 验证写入结果
                var newValue = PropertyAccessor.GetValue<object>(testObj, path);
                if (Equals(newValue, testValue))
                {
                    Debug.Log($"  ✓ 验证成功: 值已正确更新");
                }
                else
                {
                    Debug.LogWarning($"  ⚠ 验证失败: 期望{testValue}, 实际{newValue}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"  ✗ 写入失败: {ex.Message}");
                Debug.LogError($"  异常详情: {ex}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"  诊断异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析索引器访问（从PropertyAccessor复制）
    /// </summary>
    private static (string collectionName, int index) ParseIndexerAccess(string memberName)
    {
        int bracketStart = memberName.IndexOf('[');
        int bracketEnd = memberName.IndexOf(']');
        
        string collectionName = memberName[..bracketStart];
        int index = int.Parse(memberName.Substring(bracketStart + 1, bracketEnd - bracketStart - 1));
        
        return (collectionName, index);
    }

    /// <summary>
    /// 检查是否为索引器访问（从PropertyAccessor复制）
    /// </summary>
    private static bool IsIndexerAccess(string memberName) =>
        memberName.EndsWith("]");

    /// <summary>
    /// 获取成员的值类型
    /// </summary>
    private static Type GetValueType(this MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new ArgumentException($"不支持的成员类型: {member.GetType().Name}")
        };
    }

    /// <summary>
    /// 安全检查路径是否有效
    /// </summary>
    private static bool SafeIsPathValid(object obj, string path)
    {
        try
        {
            if (obj is TestClass testObj)
            {
                return testObj.HasPath(path);
            }
            PropertyAccessor.GetValue<object>(obj, path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 安全检查PropertyContainer是否支持指定路径
    /// </summary>
    private static bool SafeIsPropertyContainerSupported(object obj, string path)
    {
        try
        {
            if (obj is TestClass testObj)
            {
                var propertyPath = new PropertyPath(path);
                PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region 数据创建方法

    /// <summary>
    /// 创建标准测试对象
    /// </summary>
    private static TestClass CreateTestObject()
    {
        var result = new TestClass
        {
            PropertyValue = 42,
            FieldValue = 24,
            Name = "TestObject",
            Items = new List<TestClass>(),
            Array = new int[] { 1, 2, 3, 4, 5 },
            ClassArray = new TestClass[3],
            Dictionary = new Dictionary<string, TestClass>(),
            SimpleDict = new Dictionary<int, string> { { 1, "One" }, { 2, "Two" } },
            StructField = new TestStruct
            {
                StructValue = 100,
                StructName = "TestStruct",
                Position = new Vector3(1, 2, 3)
            }
        };

        // 创建子对象
        result.Child = new TestClass
        {
            PropertyValue = 84,
            FieldValue = 48,
            Name = "ChildObject",
            Items = new List<TestClass>(),
            Array = new int[] { 10, 20, 30 }
        };

        // 填充Items列表
        for (int i = 0; i < 3; i++)
        {
            var item = new TestClass
            {
                PropertyValue = i * 10,
                FieldValue = i * 5,
                Name = $"Item{i}",
                Array = new int[] { i, i + 1, i + 2 },
                Items = new List<TestClass>()
            };
            result.Items.Add(item);
        }

        // 填充ClassArray
        for (int i = 0; i < result.ClassArray.Length; i++)
        {
            result.ClassArray[i] = new TestClass
            {
                PropertyValue = i * 20,
                FieldValue = i * 10,
                Name = $"ArrayItem{i}",
                Items = new List<TestClass>()
            };
        }

        return result;
    }

    /// <summary>
    /// 创建大数据量测试对象
    /// </summary>
    private static TestClass CreateLargeTestObject()
    {
        var result = CreateTestObject();
        
        // 扩展Items列表
        for (int i = result.Items.Count; i < 100; i++)
        {
            result.Items.Add(new TestClass
            {
                PropertyValue = i,
                FieldValue = i * 2,
                Name = $"LargeItem{i}",
                Array = new int[] { i, i + 1, i + 2 }
            });
        }

        return result;
    }

    /// <summary>
    /// 创建嵌套对象
    /// </summary>
    private static TestClass CreateNestedObject(int depth)
    {
        var root = new TestClass
        {
            PropertyValue = 0,
            FieldValue = 0,
            Items = new List<TestClass>(),
            Array = new int[] { 0, 1, 2 }
        };

        var current = root;
        for (int i = 1; i < depth; i++)
        {
            var child = new TestClass
            {
                PropertyValue = i * 10,
                FieldValue = i * 10,
                Items = new List<TestClass>(),
                Array = new int[] { i * 10, i * 10 + 1, i * 10 + 2 }
            };

            current.Items.Add(child);
            if (i < depth - 1)
            {
                current = child;
            }
        }
        return root;
    }

    /// <summary>
    /// 获取指定深度的属性路径
    /// </summary>
    private static string GetPropertyPath(int depth)
    {
        if (depth <= 1) return "PropertyValue";

        var path = "Items[0]";
        for (int i = 2; i < depth; i++)
        {
            path += ".Items[0]";
        }
        return path + ".PropertyValue";
    }

    #endregion

    #region 新增的性能测试方法

    /// <summary>
    /// 运行纯读取性能测试
    /// </summary>
    public static void RunReadOnlyPerformanceTests()
    {
        InitializeTestSession();
        Debug.Log("=== 纯读取性能基准测试 ===");
        
        try
        {
            var testObj = SafeCreateTestObject();
            if (testObj == null) return;
            
            var testScenarios = new[]
            {
                ("简单属性访问", "PropertyValue"),
                ("字段访问", "FieldValue"),
                ("字符串属性", "Name"),
                ("嵌套属性", "Child.PropertyValue")
            };

            foreach (var (testName, path) in testScenarios)
            {
                SafeExecuteTest($"纯读取-{testName}", () => PerformReadOnlyTest(testObj, path, testName));
            }
        }
        finally
        {
            LogTestSummary();
        }

        Debug.Log("=== 纯读取性能测试完成 ===");
    }

    /// <summary>
    /// 运行纯写入性能测试
    /// </summary>
    public static void RunWriteOnlyPerformanceTests()
    {
        InitializeTestSession();
        Debug.Log("=== 纯写入性能基准测试 ===");
        
        try
        {
            var testObj = SafeCreateTestObject();
            if (testObj == null) return;
            
            var testScenarios = new[]
            {
                ("简单属性访问", "PropertyValue"),
                ("字段访问", "FieldValue"),
                ("字符串属性", "Name"),
                ("嵌套属性", "Child.PropertyValue")
            };

            foreach (var (testName, path) in testScenarios)
            {
                SafeExecuteTest($"纯写入-{testName}", () => PerformWriteOnlyTest(testObj, path, testName));
            }
        }
        finally
        {
            LogTestSummary();
        }

        Debug.Log("=== 纯写入性能测试完成 ===");
    }

    /// <summary>
    /// 运行混合读写性能测试
    /// </summary>
    public static void RunMixedReadWritePerformanceTests()
    {
        InitializeTestSession();
        Debug.Log("=== 混合读写性能基准测试 ===");
        
        try
        {
            var testObj = SafeCreateTestObject();
            if (testObj == null) return;
            
            var testScenarios = new[]
            {
                ("简单属性访问", "PropertyValue"),
                ("字段访问", "FieldValue"),
                ("字符串属性", "Name"),
                ("嵌套属性", "Child.PropertyValue")
            };

            foreach (var (testName, path) in testScenarios)
            {
                SafeExecuteTest($"混合读写-{testName}", () => PerformReadWriteTest(testObj, path, testName));
            }
        }
        finally
        {
            LogTestSummary();
        }

        Debug.Log("=== 混合读写性能测试完成 ===");
    }

    /// <summary>
    /// 运行双系统支持性验证测试
    /// </summary>
    public static void RunPathSupportValidation()
    {
        Debug.Log("=== 路径支持性验证测试 ===");
        
        var testObj = SafeCreateTestObject();
        if (testObj == null)
        {
            Debug.LogError("无法创建测试对象");
            return;
        }

        var testPaths = new[]
        {
            "PropertyValue",
            "FieldValue", 
            "Name",
            "Child.PropertyValue",
            "Child.Name",
            "Child.FieldValue",
            "Array[0]",
            "Items[0].PropertyValue",
            "StructField.StructValue",
            "ClassArray[0].PropertyValue"
        };

        Debug.Log("路径支持性分析:");
        Debug.Log("路径\t\t\t\tPropertyAccessor\tPropertyContainer");
        Debug.Log("".PadRight(80, '-'));

        foreach (var path in testPaths)
        {
            bool paSupported = SafeIsPathValid(testObj, path);
            bool pcSupported = SafeIsPropertyContainerSupported(testObj, path);
            
            string paStatus = paSupported ? "✓" : "✗";
            string pcStatus = pcSupported ? "✓" : "✗";
            string mutualStatus = (paSupported && pcSupported) ? "[共同支持]" : "";
            
            Debug.Log($"{path.PadRight(30)}\t{paStatus}\t\t{pcStatus}\t\t{mutualStatus}");
        }
        
        Debug.Log("=== 验证完成 ===");
    }

    /// <summary>
    /// 运行优化后的嵌套深度比较测试（只测试共同支持的路径）
    /// </summary>
    public static void RunOptimizedNestedDepthComparison()
    {
        InitializeTestSession();
        Debug.Log("=== 优化的嵌套深度性能对比（仅共同支持路径） ===");
        
        try
        {
            SafeExecuteTest("优化嵌套深度性能对比", RunNestedDepthComparison);
        }
        finally
        {
            LogTestSummary();
        }
        
        Debug.Log("=== 优化嵌套深度测试完成 ===");
    }

    #endregion
}
