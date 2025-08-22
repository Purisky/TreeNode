using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// PropertyAccessor 性能基准测试工具
/// 专注于PropertyAccessor和PropertyContainer之间的性能对比测试
/// 针对PropertyContainer优化：主要使用字段而非属性
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

    /// <summary>
    /// 测试结构体 - 使用字段以确保PropertyContainer兼容性
    /// </summary>
    private struct TestStruct
    {
        public int StructValue;
        public string StructName;
        public Vector3 Position;
    }

    /// <summary>
    /// 测试类 - 主要使用字段以提高PropertyContainer兼容性
    /// 保留部分属性用于兼容性测试
    /// </summary>
    private class TestClass
    {
        // 字段 - PropertyContainer主要支持
        public TestClass ChildField;
        public int IntField;
        public string StringField;
        public float FloatField;
        public bool BoolField;
        public TestStruct StructField;
        public int[] ArrayField;
        public TestClass[] ClassArrayField;
        
        // 集合字段
        public List<TestClass> ItemsField;
        
        // 保留少量属性用于对比测试
        public TestClass Child { get; set; }
        public int PropertyValue { get; set; }
        public string Name { get; set; }
        public int FieldValue;
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
        Debug.Log("=== 主要测试字段访问以提高PropertyContainer兼容性 ===");
        
        try
        {
            // 1. 基础性能测试（优先字段）
            SafeExecuteTest("基础性能对比", RunBasicPerformanceComparison);
            
            // 2. 字段嵌套深度性能测试
            SafeExecuteTest("字段嵌套深度性能对比", RunFieldNestedDepthComparison);
            
            // 3. 字段vs属性访问类型性能测试
            SafeExecuteTest("字段vs属性访问类型性能对比", RunFieldVsPropertyAccessComparison);
            
            // 4. 字段集合访问性能测试
            SafeExecuteTest("字段集合访问性能对比", RunFieldCollectionPerformanceComparison);
            
            // 5. 字段缓存效果测试
            SafeExecuteTest("字段缓存效果性能测试", RunFieldCachePerformanceTest);
            
            // 6. 字段大数据量测试
            SafeExecuteTest("字段大数据量测试", RunFieldBulkOperationTest);
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
        Debug.Log("=== 快速性能验证测试（优先字段） ===");
        
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
            
            // 简单性能对比（优先字段）
            SafeExecuteTest("简单性能对比", () => RunSimpleFieldPerformanceComparison(testObj));
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
                PerformanceTestType.NestedDepth => RunFieldNestedDepthComparison,
                PerformanceTestType.AccessTypes => RunFieldVsPropertyAccessComparison,
                PerformanceTestType.Collections => RunFieldCollectionPerformanceComparison,
                PerformanceTestType.Cache => RunFieldCachePerformanceTest,
                PerformanceTestType.BulkOperations => RunFieldBulkOperationTest,
                PerformanceTestType.ReadOnly => RunFieldReadOnlyPerformanceTests,
                PerformanceTestType.WriteOnly => RunFieldWriteOnlyPerformanceTests,
                PerformanceTestType.MixedReadWrite => RunFieldMixedReadWritePerformanceTests,
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
        Debug.Log("=== 综合分析测试（字段优先） ===");
        
        try
        {
            // 1. 基础性能对比
            SafeExecuteTest("基础性能对比", RunBasicPerformanceComparison);
            
            // 2. 优化的嵌套深度性能对比
            SafeExecuteTest("优化嵌套深度对比", RunFieldNestedDepthComparison);
            
            // 3. 路径支持性验证
            SafeExecuteTest("路径支持性验证", RunFieldPathSupportValidation);
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

    #endregion

    #region 核心性能测试 - 字段优先

    /// <summary>
    /// 基础性能对比测试（优先字段）
    /// </summary>
    private static void RunBasicPerformanceComparison()
    {
        Debug.Log("--- 基础性能对比测试（字段优先） ---");
        var testObj = SafeCreateTestObject();
        if (testObj == null) return;
        
        var testScenarios = new[]
        {
            ("字段int访问", "IntField"),
            ("字段string访问", "StringField"),
            ("字段float访问", "FloatField"),
            ("字段bool访问", "BoolField"),
            ("对比-属性访问", "PropertyValue"),
            ("对比-旧字段访问", "FieldValue")
        };

        foreach (var (testName, path) in testScenarios)
        {
            SafeExecuteTest($"基础性能-{testName}", () => PerformAccessorVsContainerTest(testObj, path, testName));
        }
    }

    /// <summary>
    /// 字段嵌套深度性能对比
    /// </summary>
    private static void RunFieldNestedDepthComparison()
    {
        Debug.Log("--- 字段嵌套深度性能对比 ---");
        
        for (int depth = MinDepth; depth <= MaxDepth; depth++)
        {
            int currentDepth = depth; // 避免闭包问题
            SafeExecuteTest($"字段嵌套深度-{currentDepth}", () => 
            {
                var testObj = CreateFieldNestedObjectForComparison(currentDepth);
                var supportedPaths = GetFieldMutuallySupportedPaths(testObj, currentDepth);
                
                if (supportedPaths.Count == 0)
                {
                    Debug.LogWarning($"深度{currentDepth}没有找到两个系统都支持的字段路径");
                    return;
                }

                Debug.Log($"深度{currentDepth}字段测试 - 共同支持的路径数: {supportedPaths.Count}");
                
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
    /// 字段vs属性访问类型性能对比
    /// </summary>
    private static void RunFieldVsPropertyAccessComparison()
    {
        Debug.Log("--- 字段vs属性访问类型性能对比 ---");
        var testObj = SafeCreateTestObject();
        if (testObj == null) return;
        
        var accessTypes = new Dictionary<string, string>
        {
            ["字段int"] = "IntField",
            ["字段string"] = "StringField", 
            ["字段struct.int"] = "StructField.StructValue",
            ["字段struct.string"] = "StructField.StructName",
            ["对比-属性int"] = "PropertyValue",
            ["对比-属性string"] = "Name"
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
    /// 字段集合访问性能对比
    /// </summary>
    private static void RunFieldCollectionPerformanceComparison()
    {
        Debug.Log("--- 字段集合访问性能对比 ---");
        var testObj = SafeCreateTestObject();
        if (testObj == null) return;
        
        var collectionTests = new Dictionary<string, string>
        {
            ["字段数组索引"] = "ArrayField[0]",
            ["字段对象数组"] = "ClassArrayField[0].IntField",
            ["对比-属性List"] = "Items[0].PropertyValue",
            ["对比-属性数组"] = "Array[0]"
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
    /// 字段缓存效果性能测试
    /// </summary>
    private static void RunFieldCachePerformanceTest()
    {
        Debug.Log("--- 字段缓存效果性能测试 ---");
        var testObj = SafeCreateTestObject();
        if (testObj == null) return;
        
        var testPaths = new[] { "IntField", "StringField", "ChildField.IntField" }
            .Where(path => SafeIsPathValid(testObj, path))
            .ToArray();

        if (testPaths.Length == 0)
        {
            Debug.LogWarning("没有有效字段路径进行缓存测试");
            return;
        }

        foreach (var path in testPaths)
        {
            Debug.Log($"  字段缓存测试 - 路径: {path} - 运行次数: {TestCount}");
            
            // PropertyAccessor 缓存测试
            var paFirstRun = SafeRunCacheTest($"PropertyAccessor首次[字段读取×{TestCount}]", new[] { path }, (testPath) =>
            {
                for (int i = 0; i < TestCount; i++)
                {
                    PropertyAccessor.GetValue<object>(testObj, testPath);
                }
            });

            var paSecondRun = SafeRunCacheTest($"PropertyAccessor再次[字段读取×{TestCount}]", new[] { path }, (testPath) =>
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
                pcFirstRun = SafeRunCacheTest($"PropertyContainer首次[字段读取×{TestCount}]", new[] { path }, (testPath) =>
                {
                    var propertyPath = new PropertyPath(testPath);
                    for (int i = 0; i < TestCount; i++)
                    {
                        PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                    }
                });

                pcSecondRun = SafeRunCacheTest($"PropertyContainer再次[字段读取×{TestCount}]", new[] { path }, (testPath) =>
                {
                    var propertyPath = new PropertyPath(testPath);
                    for (int i = 0; i < TestCount; i++)
                    {
                        PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                    }
                });
            }

            // 输出缓存测试结果
            Debug.Log($"    【PropertyAccessor字段缓存效果】路径: {path}");
            if (paFirstRun > 0 && paSecondRun > 0)
            {
                Debug.Log($"      首次[读取×{TestCount}]: {paFirstRun}ticks");
                Debug.Log($"      再次[读取×{TestCount}]: {paSecondRun}ticks");
                Debug.Log($"      缓存改善率: {(float)paFirstRun/paSecondRun:F2}x");
            }
            
            Debug.Log($"    【PropertyContainer字段缓存效果】路径: {path}");
            if (containerSupported && pcFirstRun > 0 && pcSecondRun > 0)
            {
                Debug.Log($"      首次[读取×{TestCount}]: {pcFirstRun}ticks");
                Debug.Log($"      再次[读取×{TestCount}]: {pcSecondRun}ticks");
                Debug.Log($"      缓存改善率: {(float)pcFirstRun/pcSecondRun:F2}x");
            }
            else if (containerSupported)
            {
                Debug.Log($"      PropertyContainer字段缓存测试失败");
            }
            else
            {
                Debug.Log($"      PropertyContainer不支持此字段路径");
            }
        }
    }

    /// <summary>
    /// 字段大数据量操作性能测试
    /// </summary>
    private static void RunFieldBulkOperationTest()
    {
        Debug.Log("--- 字段大数据量操作性能测试 ---");
        var testObj = SafeCreateLargeTestObject();
        if (testObj == null) return;
        
        var testPath = "IntField";
        Debug.Log($"字段大数据量测试 - 路径: {testPath} - 运行次数: {HeavyTestCount}");
        
        // PropertyAccessor 大数据量测试
        long paHeavyTime = SafeRunBulkTest($"PropertyAccessor字段大数据量[读写×{HeavyTestCount}]", testObj, testPath, (obj, path) =>
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
            pcHeavyTime = SafeRunBulkTest($"PropertyContainer字段大数据量[读写×{HeavyTestCount}]", testObj, testPath, (obj, path) =>
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
        Debug.Log($"  【字段大数据量测试结果】路径: {testPath}");
        if (paHeavyTime > 0)
        {
            Debug.Log($"    PropertyAccessor[读写×{HeavyTestCount}]: {paHeavyTime}ticks ({paHeavyTime/(float)HeavyTestCount:F2}ticks/op)");
        }
        if (containerSupported && pcHeavyTime > 0)
        {
            Debug.Log($"    PropertyContainer[读写×{HeavyTestCount}]: {pcHeavyTime}ticks ({pcHeavyTime/(float)HeavyTestCount:F2}ticks/op)");
            if (paHeavyTime > 0)
            {
                Debug.Log($"    字段大数据量性能比 PA/PC: {(float)paHeavyTime/pcHeavyTime:F2}x");
            }
        }
        else if (containerSupported)
        {
            Debug.Log($"    PropertyContainer[读写×{HeavyTestCount}]: 测试失败");
        }
        else
        {
            Debug.Log($"    PropertyContainer[读写×{HeavyTestCount}]: 不支持此字段路径");
        }
    }

    #endregion

    #region 管道性能测试

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
    /// 验证基础功能可用性（优先字段）
    /// </summary>
    private static void ValidateBasicFunctionality(TestClass testObj)
    {
        bool paWorking = false;
        bool pcWorking = false;

        // PropertyAccessor基础功能验证（字段）
        try
        {
            int value = PropertyAccessor.GetValue<int>(testObj, "IntField");
            PropertyAccessor.SetValue(testObj, "IntField", value + 1);
            int newValue = PropertyAccessor.GetValue<int>(testObj, "IntField");
            paWorking = newValue == value + 1;
        }
        catch (Exception ex)
        {
            Debug.LogError($"PropertyAccessor字段功能验证失败: {ex.Message}");
        }

        // PropertyContainer基础功能验证（字段）
        try
        {
            var propertyPath = new PropertyPath("IntField");
            int pcValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
            PropertyContainer.SetValue(testObj, propertyPath, pcValue + 1);
            int pcNewValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
            pcWorking = pcNewValue == pcValue + 1;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PropertyContainer字段功能验证失败: {ex.Message}");
        }

        Debug.Log($"字段功能验证: PropertyAccessor={(paWorking ? "✓" : "✗")}, PropertyContainer={(pcWorking ? "✓" : "✗")}");

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
    /// 安全检查路径是否有效
    /// </summary>
    private static bool SafeIsPathValid(object obj, string path)
    {
        try
        {
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
        catch(Exception e)
        {
            Debug.LogWarning($"PropertyContainer不支持此路径: {path} - 异常: {e.Message}");
            return false;
        }
    }

    #endregion

    #region 字段嵌套支持方法

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
    /// 获取两个系统都支持的字段路径列表
    /// </summary>
    private static List<PathInfo> GetFieldMutuallySupportedPaths(TestClass testObj, int depth)
    {
        var supportedPaths = new List<PathInfo>();
        
        // 定义候选路径 - 优先使用字段链
        var candidatePaths = new List<PathInfo>();
        
        if (depth == 1)
        {
            candidatePaths.AddRange(new[]
            {
                new PathInfo("IntField", "根int字段"),
                new PathInfo("StringField", "根string字段"),
                new PathInfo("FloatField", "根float字段"),
                new PathInfo("BoolField", "根bool字段")
            });
        }
        else
        {
            // 构建基于ChildField字段的嵌套路径
            var pathBuilder = "ChildField";
            var descBuilder = "ChildField";
            
            for (int i = 2; i <= depth; i++)
            {
                if (i == depth)
                {
                    // 最后一层测试不同类型的字段
                    candidatePaths.AddRange(new[]
                    {
                        new PathInfo($"{pathBuilder}.IntField", $"{descBuilder}.int字段"),
                        new PathInfo($"{pathBuilder}.StringField", $"{descBuilder}.string字段"),
                        new PathInfo($"{pathBuilder}.FloatField", $"{descBuilder}.float字段")
                    });
                }
                else
                {
                    pathBuilder += ".ChildField";
                    descBuilder += ".ChildField";
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
                Debug.Log($"  ✓ 字段路径支持: {pathInfo.Path} - {pathInfo.Description}");
            }
            else
            {
                Debug.Log($"  ✗ 字段路径不支持: {pathInfo.Path} - PA:{paSupported}, PC:{pcSupported}");
            }
        }
        
        return supportedPaths;
    }

    /// <summary>
    /// 创建用于比较测试的字段嵌套对象
    /// 确保有足够的嵌套ChildField字段供PropertyContainer访问
    /// </summary>
    private static TestClass CreateFieldNestedObjectForComparison(int depth)
    {
        var root = new TestClass
        {
            IntField = 0,
            StringField = "Root",
            FloatField = 0.0f,
            BoolField = false,
            ArrayField = new int[] { 0, 1, 2 },
            ItemsField = new List<TestClass>()
        };

        var current = root;
        for (int i = 1; i < depth; i++)
        {
            var child = new TestClass
            {
                IntField = i * 10,
                StringField = $"Child_{i}",
                FloatField = i * 1.5f,
                BoolField = i % 2 == 0,
                ArrayField = new int[] { i * 10, i * 10 + 1, i * 10 + 2 },
                ItemsField = new List<TestClass>()
            };

            current.ChildField = child;
            current = child;
        }
        
        return root;
    }

    /// <summary>
    /// 运行字段路径支持性验证测试
    /// </summary>
    public static void RunFieldPathSupportValidation()
    {
        Debug.Log("=== 字段路径支持性验证测试 ===");
        
        var testObj = SafeCreateTestObject();
        if (testObj == null)
        {
            Debug.LogError("无法创建测试对象");
            return;
        }

        var testPaths = new[]
        {
            // 字段路径
            "IntField",
            "StringField", 
            "FloatField",
            "BoolField",
            "ChildField.IntField",
            "ChildField.StringField",
            "ArrayField[0]",
            "ClassArrayField[0].IntField",
            "StructField.StructValue",
            // 对比属性路径
            "PropertyValue",
            "Name",
            "Child.PropertyValue",
            "Array[0]",
            "Items[0].PropertyValue"
        };

        Debug.Log("字段vs属性路径支持性分析:");
        Debug.Log("路径\t\t\t\tPropertyAccessor\tPropertyContainer\t类型");
        Debug.Log("".PadRight(90, '-'));

        foreach (var path in testPaths)
        {
            bool paSupported = SafeIsPathValid(testObj, path);
            bool pcSupported = SafeIsPropertyContainerSupported(testObj, path);
            
            string paStatus = paSupported ? "✓" : "✗";
            string pcStatus = pcSupported ? "✓" : "✗";
            string pathType = path.Contains("Field") ? "字段" : "属性";
            string mutualStatus = (paSupported && pcSupported) ? "[共同支持]" : "";
            
            Debug.Log($"{path.PadRight(25)}\t{paStatus}\t\t{pcStatus}\t\t{pathType}\t{mutualStatus}");
        }
        
        Debug.Log("=== 字段路径验证完成 ===");
    }

    #endregion

    #region 数据创建方法

    /// <summary>
    /// 创建标准测试对象（优先字段）
    /// </summary>
    private static TestClass CreateTestObject()
    {
        var result = new TestClass
        {
            // 新字段
            IntField = 42,
            StringField = "TestObjectField",
            FloatField = 3.14f,
            BoolField = true,
            ArrayField = new int[] { 1, 2, 3, 4, 5 },
            ClassArrayField = new TestClass[3],
            ItemsField = new List<TestClass>(),
            StructField = new TestStruct
            {
                StructValue = 100,
                StructName = "TestStructField",
                Position = new Vector3(1, 2, 3)
            },
            
            // 保留属性用于对比
            PropertyValue = 42,
            FieldValue = 24,
            Name = "TestObject",
            Items = new List<TestClass>(),
            Array = new int[] { 1, 2, 3, 4, 5 },
            ClassArray = new TestClass[3],
            Dictionary = new Dictionary<string, TestClass>(),
            SimpleDict = new Dictionary<int, string> { { 1, "One" }, { 2, "Two" } }
        };

        // 创建子对象（字段和属性）
        result.ChildField = new TestClass
        {
            IntField = 84,
            StringField = "ChildObjectField",
            FloatField = 6.28f,
            BoolField = false,
            ArrayField = new int[] { 10, 20, 30 },
            ItemsField = new List<TestClass>()
        };

        result.Child = new TestClass
        {
            PropertyValue = 84,
            FieldValue = 48,
            Name = "ChildObject",
            Items = new List<TestClass>(),
            Array = new int[] { 10, 20, 30 }
        };

        // 填充字段数组
        for (int i = 0; i < result.ClassArrayField.Length; i++)
        {
            result.ClassArrayField[i] = new TestClass
            {
                IntField = i * 20,
                StringField = $"ArrayFieldItem{i}",
                FloatField = i * 2.5f,
                BoolField = i % 2 == 0,
                ItemsField = new List<TestClass>()
            };
        }

        // 填充字段列表
        for (int i = 0; i < 3; i++)
        {
            var item = new TestClass
            {
                IntField = i * 10,
                StringField = $"ItemField{i}",
                FloatField = i * 1.1f,
                BoolField = i % 2 == 1,
                ArrayField = new int[] { i, i + 1, i + 2 },
                ItemsField = new List<TestClass>()
            };
            result.ItemsField.Add(item);
        }

        // 填充属性Items列表（保持兼容）
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

        // 填充属性ClassArray（保持兼容）
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
    /// 创建大数据量测试对象（优先字段）
    /// </summary>
    private static TestClass CreateLargeTestObject()
    {
        var result = CreateTestObject();
        
        // 扩展字段Items列表
        for (int i = result.ItemsField.Count; i < 100; i++)
        {
            result.ItemsField.Add(new TestClass
            {
                IntField = i,
                StringField = $"LargeItemField{i}",
                FloatField = i * 0.1f,
                BoolField = i % 3 == 0,
                ArrayField = new int[] { i, i + 1, i + 2 }
            });
        }
        
        // 扩展属性Items列表（保持兼容）
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

    #region 字段性能测试方法

    /// <summary>
    /// 运行纯字段读取性能测试
    /// </summary>
    public static void RunFieldReadOnlyPerformanceTests()
    {
        InitializeTestSession();
        Debug.Log("=== 纯字段读取性能基准测试 ===");
        
        try
        {
            var testObj = SafeCreateTestObject();
            if (testObj == null) return;
            
            var testScenarios = new[]
            {
                ("字段int访问", "IntField"),
                ("字段string访问", "StringField"),
                ("字段float访问", "FloatField"),
                ("嵌套字段访问", "ChildField.IntField")
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

        Debug.Log("=== 纯字段读取性能测试完成 ===");
    }

    /// <summary>
    /// 运行纯字段写入性能测试
    /// </summary>
    public static void RunFieldWriteOnlyPerformanceTests()
    {
        InitializeTestSession();
        Debug.Log("=== 纯字段写入性能基准测试 ===");
        
        try
        {
            var testObj = SafeCreateTestObject();
            if (testObj == null) return;
            
            var testScenarios = new[]
            {
                ("字段int访问", "IntField"),
                ("字段string访问", "StringField"),
                ("字段float访问", "FloatField"),
                ("嵌套字段访问", "ChildField.IntField")
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

        Debug.Log("=== 纯字段写入性能测试完成 ===");
    }

    /// <summary>
    /// 运行混合字段读写性能测试
    /// </summary>
    public static void RunFieldMixedReadWritePerformanceTests()
    {
        InitializeTestSession();
        Debug.Log("=== 混合字段读写性能基准测试 ===");
        
        try
        {
            var testObj = SafeCreateTestObject();
            if (testObj == null) return;
            
            var testScenarios = new[]
            {
                ("字段int访问", "IntField"),
                ("字段string访问", "StringField"),
                ("字段float访问", "FloatField"),
                ("嵌套字段访问", "ChildField.IntField")
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

        Debug.Log("=== 混合字段读写性能测试完成 ===");
    }

    /// <summary>
    /// 简单字段性能对比
    /// </summary>
    private static void RunSimpleFieldPerformanceComparison(TestClass testObj)
    {
        const int quickTestCount = 1000;
        const string testPath = "IntField";
        
        Debug.Log($"简单字段性能对比 - 路径: {testPath} - 运行次数: {quickTestCount}");

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
            Debug.LogError($"PropertyAccessor字段读取测试失败: {ex.Message}");
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
            Debug.LogError($"PropertyAccessor字段写入测试失败: {ex.Message}");
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
            Debug.LogWarning($"PropertyContainer字段读取测试失败: {ex.Message}");
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
            Debug.LogWarning($"PropertyContainer字段写入测试失败: {ex.Message}");
        }

        // 输出结果
        Debug.Log($"  【字段读取性能对比】({quickTestCount}次):");
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

        Debug.Log($"  【字段写入性能对比】({quickTestCount}次):");
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

    #endregion
}
