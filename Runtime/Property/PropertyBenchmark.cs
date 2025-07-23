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
                _ => () => Debug.LogWarning($"未知的测试类型: {testType}")
            };
            
            SafeExecuteTest(testName, testAction);
        }
        finally
        {
            LogTestSummary();
        }
    }

    public enum PerformanceTestType
    {
        BasicComparison,
        NestedDepth,
        AccessTypes,
        Collections,
        Cache,
        BulkOperations
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
                var testObj = CreateNestedObject(currentDepth);
                string path = GetPropertyPath(currentDepth);
                
                if (SafeIsPathValid(testObj, path))
                {
                    PerformAccessorVsContainerTest(testObj, path, $"深度{currentDepth}");
                }
                else
                {
                    Debug.LogWarning($"深度{currentDepth}路径无效: {path}");
                }
            });
        }
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
        
        var testPaths = new[] { "PropertyValue", "FieldValue", "Child.PropertyValue", "Array[0]" }
            .Where(path => SafeIsPathValid(testObj, path))
            .ToArray();

        if (testPaths.Length == 0)
        {
            Debug.LogWarning("没有有效路径进行缓存测试");
            return;
        }

        // PropertyAccessor 缓存测试
        var paFirstRun = SafeRunCacheTest("PropertyAccessor首次", testPaths, (path) =>
        {
            for (int i = 0; i < TestCount; i++)
            {
                PropertyAccessor.GetValue<object>(testObj, path);
            }
        });

        var paSecondRun = SafeRunCacheTest("PropertyAccessor再次", testPaths, (path) =>
        {
            for (int i = 0; i < TestCount; i++)
            {
                PropertyAccessor.GetValue<object>(testObj, path);
            }
        });

        // PropertyContainer 缓存测试
        var supportedPaths = testPaths.Where(path => SafeIsPropertyContainerSupported(testObj, path)).ToArray();
        
        long pcFirstRun = 0, pcSecondRun = 0;
        if (supportedPaths.Length > 0)
        {
            pcFirstRun = SafeRunCacheTest("PropertyContainer首次", supportedPaths, (path) =>
            {
                var propertyPath = new PropertyPath(path);
                for (int i = 0; i < TestCount; i++)
                {
                    PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                }
            });

            pcSecondRun = SafeRunCacheTest("PropertyContainer再次", supportedPaths, (path) =>
            {
                var propertyPath = new PropertyPath(path);
                for (int i = 0; i < TestCount; i++)
                {
                    PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                }
            });
        }

        // 输出缓存测试结果
        if (paFirstRun > 0 && paSecondRun > 0)
        {
            Debug.Log($"PropertyAccessor缓存效果: 首次={paFirstRun}ticks, 再次={paSecondRun}ticks, 改善率={(float)paFirstRun/paSecondRun:F2}x");
        }
        if (pcFirstRun > 0 && pcSecondRun > 0)
        {
            Debug.Log($"PropertyContainer缓存效果: 首次={pcFirstRun}ticks, 再次={pcSecondRun}ticks, 改善率={(float)pcFirstRun/pcSecondRun:F2}x");
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
        
        // PropertyAccessor 大数据量测试
        long paHeavyTime = SafeRunBulkTest("PropertyAccessor大数据量", testObj, testPath, (obj, path) =>
        {
            for (int i = 0; i < HeavyTestCount; i++)
            {
                var value = PropertyAccessor.GetValue<int>(obj, path);
                PropertyAccessor.SetValue(obj, path, value + 1);
            }
        });

        // PropertyContainer 大数据量测试（如果支持）
        long pcHeavyTime = 0;
        if (SafeIsPropertyContainerSupported(testObj, testPath))
        {
            pcHeavyTime = SafeRunBulkTest("PropertyContainer大数据量", testObj, testPath, (obj, path) =>
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
        Debug.Log($"大数据量测试({HeavyTestCount}次操作):");
        if (paHeavyTime > 0)
        {
            Debug.Log($"  PropertyAccessor: {paHeavyTime}ticks ({paHeavyTime/(float)HeavyTestCount:F2}ticks/op)");
        }
        if (pcHeavyTime > 0)
        {
            Debug.Log($"  PropertyContainer: {pcHeavyTime}ticks ({pcHeavyTime/(float)HeavyTestCount:F2}ticks/op)");
            if (paHeavyTime > 0)
            {
                Debug.Log($"  性能比: {(float)paHeavyTime/pcHeavyTime:F2}x");
            }
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

    #region 核心测试方法

    /// <summary>
    /// 执行PropertyAccessor vs PropertyContainer性能对比
    /// </summary>
    private static void PerformAccessorVsContainerTest(object testObj, string path, string testName)
    {
        // 预热
        SafeWarmupBothSystems(testObj, path);

        // PropertyAccessor测试
        long accessorTime = SafeRunPerformanceTest("PropertyAccessor", testObj, path, (obj, pth) =>
        {
            for (int i = 0; i < TestCount; i++)
            {
                try
                {
                    var value = PropertyAccessor.GetValue<object>(obj, pth);
                    if (value is int intVal)
                    {
                        PropertyAccessor.SetValue(obj, pth, intVal + 1);
                    }
                    else if (value is float floatVal)
                    {
                        PropertyAccessor.SetValue(obj, pth, floatVal + 1.0f);
                    }
                    else if (value is double doubleVal)
                    {
                        PropertyAccessor.SetValue(obj, pth, doubleVal + 1.0);
                    }
                    else if (value is string stringVal)
                    {
                        PropertyAccessor.SetValue(obj, pth, stringVal + ".");
                    }
                    else if (value != null)
                    {
                        // 对于其他类型，尝试简单的重新设置相同值
                        PropertyAccessor.SetValue<object>(obj, pth, value);
                    }
                }
                catch (Exception ex)
                {
                    // 记录但不中断测试
                    if (i == 0) // 只在第一次失败时记录，避免日志泛滥
                    {
                        Debug.LogWarning($"PropertyAccessor写入失败 (路径: {pth}, 迭代: {i}): {ex.Message}");
                    }
                }
            }
        });

        // PropertyContainer测试
        long containerTime = 0;
        bool containerSupported = SafeIsPropertyContainerSupported(testObj, path);
        
        if (containerSupported)
        {
            containerTime = SafeRunPerformanceTest("PropertyContainer", testObj, path, (obj, pth) =>
            {
                var propertyPath = new PropertyPath(pth);
                for (int i = 0; i < TestCount; i++)
                {
                    try
                    {
                        var value = PropertyContainer.GetValue<TestClass, object>((TestClass)obj, propertyPath);
                        if (value is int intVal)
                        {
                            PropertyContainer.SetValue((TestClass)obj, propertyPath, intVal + 1);
                        }
                        else if (value is float floatVal)
                        {
                            PropertyContainer.SetValue((TestClass)obj, propertyPath, floatVal + 1.0f);
                        }
                        else if (value is double doubleVal)
                        {
                            PropertyContainer.SetValue((TestClass)obj, propertyPath, doubleVal + 1.0);
                        }
                        else if (value is string stringVal)
                        {
                            PropertyContainer.SetValue((TestClass)obj, propertyPath, stringVal + ".");
                        }
                        else if (value != null)
                        {
                            PropertyContainer.SetValue((TestClass)obj, propertyPath, value);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (i == 0)
                        {
                            Debug.LogWarning($"PropertyContainer写入失败 (路径: {pth}, 迭代: {i}): {ex.Message}");
                        }
                    }
                }
            });
        }

        // 扩展方法测试（PropertyAccessor的封装）
        long extensionTime = SafeRunPerformanceTest("扩展方法", testObj, path, (obj, pth) =>
        {
            for (int i = 0; i < TestCount; i++)
            {
                try
                {
                    var value = ((TestClass)obj).GetValueOrDefault<object>(pth, null);
                    if (value is int intVal)
                    {
                        ((TestClass)obj).TrySetValue(pth, intVal + 1);
                    }
                    else if (value is float floatVal)
                    {
                        ((TestClass)obj).TrySetValue(pth, floatVal + 1.0f);
                    }
                    else if (value is double doubleVal)
                    {
                        ((TestClass)obj).TrySetValue(pth, doubleVal + 1.0);
                    }
                    else if (value is string stringVal)
                    {
                        ((TestClass)obj).TrySetValue(pth, stringVal + ".");
                    }
                    else if (value != null)
                    {
                        ((TestClass)obj).TrySetValue(pth, value);
                    }
                }
                catch (Exception ex)
                {
                    if (i == 0)
                    {
                        Debug.LogWarning($"扩展方法写入失败 (路径: {pth}, 迭代: {i}): {ex.Message}");
                    }
                }
            }
        });

        // 输出结果
        LogPerformanceResults(testName, accessorTime, containerTime, extensionTime, containerSupported);
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

    /// <summary>
    /// 记录性能测试结果
    /// </summary>
    private static void LogPerformanceResults(string testName, long accessorTime, long containerTime, 
        long extensionTime, bool containerSupported)
    {
        Debug.Log($"{testName} 性能测试结果:");
        
        if (accessorTime > 0)
        {
            Debug.Log($"  PropertyAccessor: {accessorTime}ticks ({accessorTime/(float)TestCount:F2}ticks/op)");
        }
        else
        {
            Debug.Log($"  PropertyAccessor: 测试失败");
        }
        
        if (containerSupported && containerTime > 0)
        {
            float ratio = (float)accessorTime / containerTime;
            Debug.Log($"  PropertyContainer: {containerTime}ticks ({containerTime/(float)TestCount:F2}ticks/op)");
            string comparisonText = ratio > 1 ? "(PA较慢)" : "(PA较快)";
            Debug.Log($"  PA/PC性能比: {ratio:F2}x {comparisonText}");
        }
        else if (containerSupported)
        {
            Debug.Log($"  PropertyContainer: 测试失败");
        }
        else
        {
            Debug.Log($"  PropertyContainer: 不支持此路径");
        }
        
        if (extensionTime > 0 && accessorTime > 0)
        {
            float extRatio = (float)extensionTime / accessorTime;
            Debug.Log($"  扩展方法: {extensionTime}ticks ({extensionTime/(float)TestCount:F2}ticks/op)");
            Debug.Log($"  扩展/PA性能比: {extRatio:F2}x");
        }
        else if (extensionTime > 0)
        {
            Debug.Log($"  扩展方法: {extensionTime}ticks ({extensionTime/(float)TestCount:F2}ticks/op)");
        }
        else
        {
            Debug.Log($"  扩展方法: 测试失败");
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
    }

    /// <summary>
    /// 简单性能对比
    /// </summary>
    private static void RunSimplePerformanceComparison(TestClass testObj)
    {
        const int quickTestCount = 1000;
        
        // PropertyAccessor简单测试
        long paTime = 0;
        try
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < quickTestCount; i++)
            {
                PropertyAccessor.GetValue<int>(testObj, "PropertyValue");
            }
            sw.Stop();
            paTime = sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogError($"PropertyAccessor简单测试失败: {ex.Message}");
        }

        // PropertyContainer简单测试
        long pcTime = 0;
        try
        {
            var propertyPath = new PropertyPath("PropertyValue");
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < quickTestCount; i++)
            {
                PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
            }
            sw.Stop();
            pcTime = sw.ElapsedTicks;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PropertyContainer简单测试失败: {ex.Message}");
        }

        string result = $"简单性能对比({quickTestCount}次):";
        if (paTime > 0)
        {
            result += $" PropertyAccessor={paTime}ticks";
        }
        if (pcTime > 0)
        {
            result += $", PropertyContainer={pcTime}ticks, 比率={(float)paTime/pcTime:F2}x";
        }
        else
        {
            result += ", PropertyContainer=不支持或失败";
        }
        
        Debug.Log(result);
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
}
