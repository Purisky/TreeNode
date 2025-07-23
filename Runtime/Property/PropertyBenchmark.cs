using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using TreeNode.Runtime;
using TreeNode.Runtime.Property.Extensions;
using Unity.Properties;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// PropertyAccessor 性能基准测试工具
/// 提供静态方法进行各种PropertyAccessor和PropertyContainer功能的性能测试
/// </summary>
public static class PropertyBenchmark
{
    private const int WarmupCount = 100;
    private const int TestCount = 1000;
    private const int MinDepth = 3; // 降低最小深度避免过深路径问题
    private const int MaxDepth = 6;  // 降低最大深度

    // 复杂测试数据结构
    private struct TestStruct
    {
        public int StructValue;
        public string StructName;
        public Vector3 Position;
        public TestClass NestedClass;
    }

    private class TestClass
    {
        // 属性 (Property)
        public TestClass Child { get; set; }
        public int PropertyValue { get; set; }
        public string Name { get; set; }
        
        // 字段 (Field)
        public int FieldValue;
        public TestStruct StructField;
        
        // 集合类型
        public List<TestClass> Items { get; set; }
        public int[] Array { get; set; }
        public TestClass[] ClassArray { get; set; }
        public Dictionary<string, TestClass> Dictionary { get; set; }
        public Dictionary<int, string> SimpleDict { get; set; }
        
        // 嵌套复杂类型
        public TestComplexData ComplexData { get; set; }
        
        // 只读属性
        public int ReadOnlyProperty => PropertyValue * 2;
        
        // 私有字段
        private string privateField = "private";
        public string GetPrivateField() => privateField;
        public void SetPrivateField(string value) => privateField = value;
    }

    private class TestComplexData
    {
        public Matrix4x4 Matrix { get; set; }
        public Color[] Colors { get; set; }
        public Dictionary<string, Dictionary<int, TestStruct>> NestedDict { get; set; }
        public List<TestStruct[]> StructArrayList { get; set; }
    }

    /// <summary>
    /// 运行完整的性能基准测试套件
    /// </summary>
    public static void RunAllTests()
    {
        Debug.Log("=== PropertyAccessor 全面性能基准测试开始 ===");
        
        // 预热
        var testObj = CreateComplexTestObject();
        Debug.Log("\n--- 功能预验证阶段 ---");
        PreValidateFeatures(testObj);
        
        RunBasicPerformanceTest(MinDepth, true);
        
        // 基础性能测试
        Debug.Log("\n--- 基础嵌套深度性能测试 ---");
        for (int depth = MinDepth; depth <= MaxDepth; depth++)
        {
            RunBasicPerformanceTest(depth);
        }

        // 字段 vs 属性性能测试
        Debug.Log("\n--- 字段 vs 属性性能对比 ---");
        RunFieldVsPropertyTest();

        // 结构体性能测试
        Debug.Log("\n--- 结构体访问性能测试 ---");
        RunStructTest();

        // 集合类型性能测试
        Debug.Log("\n--- 集合类型性能测试 ---");
        RunCollectionTests();

        // 字典性能测试
        Debug.Log("\n--- 字典访问性能测试 ---");
        RunDictionaryTests();

        // 复杂路径性能测试
        Debug.Log("\n--- 复杂路径性能测试 ---");
        RunComplexPathTests();

        // PropertyContainer专项测试
        Debug.Log("\n--- PropertyContainer专项测试 ---");
        RunPropertyContainerTests();

        // 扩展方法功能测试
        Debug.Log("\n--- 扩展方法功能测试 ---");
        TestNewExtensions();

        // 缓存效果测试
        Debug.Log("\n--- 缓存效果测试 ---");
        RunCacheEffectivenessTest();

        // 异常处理性能测试
        Debug.Log("\n--- 异常路径性能测试 ---");
        RunExceptionPathTests();

        Debug.Log("\n=== 所有性能测试完成 ===");
    }

    /// <summary>
    /// 运行快速验证测试（仅验证功能可用性）
    /// </summary>
    public static void RunQuickValidation()
    {
        Debug.Log("=== PropertyAccessor 快速功能验证 ===");
        var testObj = CreateComplexTestObject();
        PreValidateFeatures(testObj);
        Debug.Log("=== 快速验证完成 ===");
    }

    /// <summary>
    /// 运行特定的性能测试
    /// </summary>
    /// <param name="testType">测试类型</param>
    public static void RunSpecificTest(BenchmarkTestType testType)
    {
        var testObj = CreateComplexTestObject();
        
        switch (testType)
        {
            case BenchmarkTestType.BasicPerformance:
                Debug.Log("--- 基础性能测试 ---");
                for (int depth = MinDepth; depth <= MaxDepth; depth++)
                {
                    RunBasicPerformanceTest(depth);
                }
                break;
                
            case BenchmarkTestType.FieldVsProperty:
                Debug.Log("--- 字段 vs 属性测试 ---");
                RunFieldVsPropertyTest();
                break;
                
            case BenchmarkTestType.StructAccess:
                Debug.Log("--- 结构体访问测试 ---");
                RunStructTest();
                break;
                
            case BenchmarkTestType.Collections:
                Debug.Log("--- 集合访问测试 ---");
                RunCollectionTests();
                break;
                
            case BenchmarkTestType.Dictionary:
                Debug.Log("--- 字典访问测试 ---");
                RunDictionaryTests();
                break;
                
            case BenchmarkTestType.PropertyContainer:
                Debug.Log("--- PropertyContainer测试 ---");
                RunPropertyContainerTests();
                break;
                
            case BenchmarkTestType.Extensions:
                Debug.Log("--- 扩展方法测试 ---");
                TestNewExtensions();
                break;
                
            case BenchmarkTestType.Cache:
                Debug.Log("--- 缓存效果测试 ---");
                RunCacheEffectivenessTest();
                break;
                
            case BenchmarkTestType.Validation:
                Debug.Log("--- 功能验证测试 ---");
                PreValidateFeatures(testObj);
                break;
        }
    }

    /// <summary>
    /// 测试类型枚举
    /// </summary>
    public enum BenchmarkTestType
    {
        BasicPerformance,
        FieldVsProperty,
        StructAccess,
        Collections,
        Dictionary,
        PropertyContainer,
        Extensions,
        Cache,
        Validation
    }

    /// <summary>
    /// 预验证各种功能的可用性
    /// </summary>
    private static void PreValidateFeatures(TestClass testObj)
    {
        var validationResults = new List<(string feature, bool supported, string reason)>();

        // 验证PropertyAccessor基础功能
        validationResults.Add(ValidateBasicAccess(testObj));
        validationResults.Add(ValidateStructAccess(testObj));
        validationResults.Add(ValidateCollectionAccess(testObj));
        validationResults.Add(ValidateDictionaryAccess(testObj));
        validationResults.Add(ValidateExtensionMethods(testObj));
        
        // 验证PropertyContainer功能
        validationResults.Add(ValidatePropertyContainer(testObj));
        validationResults.Add(ValidatePropertyContainerAdvanced(testObj));

        // 输出验证结果
        Debug.Log("\n=== 功能支持检测结果 ===");
        foreach (var (feature, supported, reason) in validationResults)
        {
            string status = supported ? "✓ 支持" : "✗ 不支持";
            Debug.Log($"{status} - {feature}: {reason}");
        }
        Debug.Log("========================\n");
    }

    /// <summary>
    /// 验证基础访问功能
    /// </summary>
    private static (string, bool, string) ValidateBasicAccess(TestClass testObj)
    {
        try
        {
            // 测试读取
            int value = PropertyAccessor.GetValue<int>(testObj, "PropertyValue");
            
            // 测试写入
            PropertyAccessor.SetValue(testObj, "PropertyValue", value + 1);
            
            // 验证写入成功
            int newValue = PropertyAccessor.GetValue<int>(testObj, "PropertyValue");
            
            if (newValue == value + 1)
            {
                return ("PropertyAccessor基础访问", true, "读写操作正常");
            }
            else
            {
                return ("PropertyAccessor基础访问", false, "写入操作失败");
            }
        }
        catch (Exception ex)
        {
            return ("PropertyAccessor基础访问", false, $"异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证结构体访问功能
    /// </summary>
    private static (string, bool, string) ValidateStructAccess(TestClass testObj)
    {
        try
        {
            // 测试读取结构体字段
            int value = PropertyAccessor.GetValue<int>(testObj, "StructField.StructValue");
            
            // 测试写入结构体字段
            PropertyAccessor.SetValue(testObj, "StructField.StructValue", value + 1);
            
            // 验证写入成功
            int newValue = PropertyAccessor.GetValue<int>(testObj, "StructField.StructValue");
            
            if (newValue == value + 1)
            {
                return ("PropertyAccessor结构体访问", true, "结构体字段读写正常");
            }
            else
            {
                return ("PropertyAccessor结构体访问", false, "结构体字段写入失败");
            }
        }
        catch (Exception ex)
        {
            return ("PropertyAccessor结构体访问", false, $"异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证集合访问功能
    /// </summary>
    private static (string, bool, string) ValidateCollectionAccess(TestClass testObj)
    {
        try
        {
            // 测试List索引访问
            if (testObj.Items.Count > 0)
            {
                int value = PropertyAccessor.GetValue<int>(testObj, "Items[0].PropertyValue");
                PropertyAccessor.SetValue(testObj, "Items[0].PropertyValue", value + 1);
                int newValue = PropertyAccessor.GetValue<int>(testObj, "Items[0].PropertyValue");
                
                if (newValue != value + 1)
                {
                    return ("PropertyAccessor集合访问", false, "List索引写入失败");
                }
            }
            
            // 测试数组索引访问
            if (testObj.Array.Length > 0)
            {
                int value = PropertyAccessor.GetValue<int>(testObj, "Array[0]");
                PropertyAccessor.SetValue(testObj, "Array[0]", value + 1);
                int newValue = PropertyAccessor.GetValue<int>(testObj, "Array[0]");
                
                if (newValue != value + 1)
                {
                    return ("PropertyAccessor集合访问", false, "数组索引写入失败");
                }
            }
            
            return ("PropertyAccessor集合访问", true, "List和数组索引读写正常");
        }
        catch (Exception ex)
        {
            return ("PropertyAccessor集合访问", false, $"异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证字典访问功能
    /// </summary>
    private static (string, bool, string) ValidateDictionaryAccess(TestClass testObj)
    {
        try
        {
            // 测试字典访问
            string value = PropertyAccessor.GetValue<string>(testObj, "SimpleDict[1]");
            PropertyAccessor.SetValue(testObj, "SimpleDict[1]", "test_validation");
            string newValue = PropertyAccessor.GetValue<string>(testObj, "SimpleDict[1]");
            
            if (newValue == "test_validation")
            {
                return ("PropertyAccessor字典访问", true, "字典读写正常");
            }
            else
            {
                return ("PropertyAccessor字典访问", false, "字典写入失败");
            }
        }
        catch (Exception ex)
        {
            return ("PropertyAccessor字典访问", false, $"不支持或异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证扩展方法功能
    /// </summary>
    private static (string, bool, string) ValidateExtensionMethods(TestClass testObj)
    {
        try
        {
            // 测试安全访问方法
            int value = testObj.GetValueOrDefault<int>("PropertyValue", -1);
            bool setSuccess = testObj.TrySetValue("PropertyValue", value + 1);
            bool hasPath = testObj.HasPath("PropertyValue");
            
            if (value != -1 && setSuccess && hasPath)
            {
                return ("PropertyAccessor扩展方法", true, "安全访问、设置、路径检查正常");
            }
            else
            {
                return ("PropertyAccessor扩展方法", false, $"部分功能失败: value={value}, setSuccess={setSuccess}, hasPath={hasPath}");
            }
        }
        catch (Exception ex)
        {
            return ("PropertyAccessor扩展方法", false, $"异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证PropertyContainer基础功能
    /// </summary>
    private static (string, bool, string) ValidatePropertyContainer(TestClass testObj)
    {
        try
        {
            PropertyPath propertyPath = new PropertyPath("PropertyValue");
            int value = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
            PropertyContainer.SetValue(testObj, propertyPath, value + 1);
            int newValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
            
            if (newValue == value + 1)
            {
                return ("PropertyContainer基础访问", true, "Unity PropertyContainer读写正常");
            }
            else
            {
                return ("PropertyContainer基础访问", false, "PropertyContainer写入失败");
            }
        }
        catch (Exception ex)
        {
            return ("PropertyContainer基础访问", false, $"异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证PropertyContainer高级功能
    /// </summary>
    private static (string, bool, string) ValidatePropertyContainerAdvanced(TestClass testObj)
    {
        var supportedFeatures = new List<string>();
        var failedFeatures = new List<string>();

        // 测试嵌套路径访问
        try
        {
            PropertyPath nestedPath = new PropertyPath("Child.PropertyValue");
            int value = PropertyContainer.GetValue<TestClass, int>(testObj, nestedPath);
            PropertyContainer.SetValue(testObj, nestedPath, value + 1);
            int newValue = PropertyContainer.GetValue<TestClass, int>(testObj, nestedPath);
            
            if (newValue == value + 1)
            {
                supportedFeatures.Add("嵌套路径");
            }
            else
            {
                failedFeatures.Add("嵌套路径写入失败");
            }
        }
        catch
        {
            failedFeatures.Add("嵌套路径异常");
        }

        // 测试字段访问
        try
        {
            PropertyPath fieldPath = new PropertyPath("FieldValue");
            int value = PropertyContainer.GetValue<TestClass, int>(testObj, fieldPath);
            PropertyContainer.SetValue(testObj, fieldPath, value + 1);
            int newValue = PropertyContainer.GetValue<TestClass, int>(testObj, fieldPath);
            
            if (newValue == value + 1)
            {
                supportedFeatures.Add("字段访问");
            }
            else
            {
                failedFeatures.Add("字段访问写入失败");
            }
        }
        catch
        {
            failedFeatures.Add("字段访问异常");
        }

        // 测试结构体访问
        try
        {
            PropertyPath structPath = new PropertyPath("StructField.StructValue");
            int value = PropertyContainer.GetValue<TestClass, int>(testObj, structPath);
            PropertyContainer.SetValue(testObj, structPath, value + 1);
            int newValue = PropertyContainer.GetValue<TestClass, int>(testObj, structPath);
            
            if (newValue == value + 1)
            {
                supportedFeatures.Add("结构体字段");
            }
            else
            {
                failedFeatures.Add("结构体字段写入失败");
            }
        }
        catch
        {
            failedFeatures.Add("结构体字段异常");
        }

        // 测试集合访问
        try
        {
            PropertyPath collectionPath = new PropertyPath("Items[0].PropertyValue");
            int value = PropertyContainer.GetValue<TestClass, int>(testObj, collectionPath);
            PropertyContainer.SetValue(testObj, collectionPath, value + 1);
            int newValue = PropertyContainer.GetValue<TestClass, int>(testObj, collectionPath);
            
            if (newValue == value + 1)
            {
                supportedFeatures.Add("集合索引");
            }
            else
            {
                failedFeatures.Add("集合索引写入失败");
            }
        }
        catch
        {
            failedFeatures.Add("集合索引异常");
        }

        // 测试数组访问
        try
        {
            PropertyPath arrayPath = new PropertyPath("Array[0]");
            int value = PropertyContainer.GetValue<TestClass, int>(testObj, arrayPath);
            PropertyContainer.SetValue(testObj, arrayPath, value + 1);
            int newValue = PropertyContainer.GetValue<TestClass, int>(testObj, arrayPath);
            
            if (newValue == value + 1)
            {
                supportedFeatures.Add("数组索引");
            }
            else
            {
                failedFeatures.Add("数组索引写入失败");
            }
        }
        catch
        {
            failedFeatures.Add("数组索引异常");
        }

        // 汇总结果
        bool hasAnySupport = supportedFeatures.Count > 0;
        string details = "";
        
        if (supportedFeatures.Count > 0)
        {
            details += $"支持: {string.Join(", ", supportedFeatures)}";
        }
        
        if (failedFeatures.Count > 0)
        {
            if (details.Length > 0) details += "; ";
            details += $"不支持: {string.Join(", ", failedFeatures)}";
        }

        return ("PropertyContainer高级功能", hasAnySupport, details);
    }

    /// <summary>
    /// 基础性能测试 - 添加路径验证和PropertyContainer检测
    /// </summary>
    private static void RunBasicPerformanceTest(int depth, bool warmup = false)
    {
        var root = CreateNestedObject(depth);
        string path = GetPropertyPath(depth);
        
        // 先验证路径是否有效
        if (!root.HasPath(path))
        {
            Debug.LogWarning($"深度{depth}的路径无效，跳过测试: {path}");
            return;
        }

        // 检测PropertyContainer对该路径的支持
        bool propertyContainerSupported = false;
        try
        {
            PropertyPath propertyPath = new PropertyPath(path);
            int testValue = PropertyContainer.GetValue<TestClass, int>(root, propertyPath);
            PropertyContainer.SetValue(root, propertyPath, testValue + 1);
            int newTestValue = PropertyContainer.GetValue<TestClass, int>(root, propertyPath);
            propertyContainerSupported = (newTestValue == testValue + 1);
            
            // 恢复原值
            PropertyContainer.SetValue(root, propertyPath, testValue);
        }
        catch (Exception ex)
        {
            if (!warmup)
            {
                Debug.LogWarning($"PropertyContainer不支持深度{depth}路径: {ex.Message}");
            }
        }

        try
        {
            // 测试PropertyAccessor
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < (warmup ? WarmupCount : TestCount); i++)
            {
                int value = PropertyAccessor.GetValue<int>(root, path);
                value++;
                PropertyAccessor.SetValue(root, path, value);
            }
            PropertyAccessor.SetValue(root, path, 0);
            sw.Stop();
            var accessorTime = sw.ElapsedTicks;

            long containerTime = 0;
            // 测试PropertyContainer（仅在支持时）
            if (propertyContainerSupported)
            {
                sw.Restart();
                PropertyPath propertyPath = new PropertyPath(path);
                for (int i = 0; i < (warmup ? WarmupCount : TestCount); i++)
                {
                    int value = PropertyContainer.GetValue<TestClass,int>(root, propertyPath);
                    value++;
                    PropertyContainer.SetValue(root, propertyPath, value);
                }
                sw.Stop();
                containerTime = sw.ElapsedTicks;
            }

            // 测试扩展方法性能
            sw.Restart();
            for (int i = 0; i < (warmup ? WarmupCount : TestCount); i++)
            {
                int value = root.GetValueOrDefault<int>(path, 0);
                value++;
                root.TrySetValue(path, value);
            }
            sw.Stop();
            var extensionTime = sw.ElapsedTicks;

            if (!warmup)
            {
                if (propertyContainerSupported)
                {
                    float rate1 = (float)accessorTime / containerTime;
                    float rate2 = (float)extensionTime / accessorTime;
                    Debug.Log($"深度{depth}: PropertyAccessor={accessorTime}ticks, PropertyContainer={containerTime}ticks, 扩展方法={extensionTime}ticks");
                    Debug.Log($"  比率 -> PA/PC: {rate1:F2}x, 扩展/PA: {rate2:F2}x");
                }
                else
                {
                    float rate2 = (float)extensionTime / accessorTime;
                    Debug.Log($"深度{depth}: PropertyAccessor={accessorTime}ticks, PropertyContainer=不支持, 扩展方法={extensionTime}ticks");
                    Debug.Log($"  比率 -> 扩展/PA: {rate2:F2}x");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"深度{depth}测试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 字段 vs 属性性能对比测试 - 添加预验证
    /// </summary>
    private static void RunFieldVsPropertyTest()
    {
        var testObj = CreateComplexTestObject();
        
        // 预验证路径
        var testPaths = new[] { "PropertyValue", "FieldValue", "Child.PropertyValue", "Child.FieldValue" };
        var validPaths = testPaths.Where(path => testObj.HasPath(path)).ToList();
        
        Debug.Log($"有效测试路径: {string.Join(", ", validPaths)}");
        
        if (!validPaths.Contains("PropertyValue"))
        {
            Debug.LogWarning("属性访问测试路径无效，跳过属性测试");
            return;
        }

        try
        {
            // 测试属性访问
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < TestCount; i++)
            {
                int value = PropertyAccessor.GetValue<int>(testObj, "PropertyValue");
                PropertyAccessor.SetValue(testObj, "PropertyValue", value + 1);
            }
            sw.Stop();
            var propertyTime = sw.ElapsedTicks;

            // 测试字段访问
            sw.Restart();
            for (int i = 0; i < TestCount; i++)
            {
                int value = PropertyAccessor.GetValue<int>(testObj, "FieldValue");
                PropertyAccessor.SetValue(testObj, "FieldValue", value + 1);
            }
            sw.Stop();
            var fieldTime = sw.ElapsedTicks;

            // 测试嵌套属性访问
            long nestedPropertyTime = 0;
            if (validPaths.Contains("Child.PropertyValue"))
            {
                sw.Restart();
                for (int i = 0; i < TestCount; i++)
                {
                    int value = PropertyAccessor.GetValue<int>(testObj, "Child.PropertyValue");
                    PropertyAccessor.SetValue(testObj, "Child.PropertyValue", value + 1);
                }
                sw.Stop();
                nestedPropertyTime = sw.ElapsedTicks;
            }

            // 测试嵌套字段访问
            long nestedFieldTime = 0;
            if (validPaths.Contains("Child.FieldValue"))
            {
                sw.Restart();
                for (int i = 0; i < TestCount; i++)
                {
                    int value = PropertyAccessor.GetValue<int>(testObj, "Child.FieldValue");
                    PropertyAccessor.SetValue(testObj, "Child.FieldValue", value + 1);
                }
                sw.Stop();
                nestedFieldTime = sw.ElapsedTicks;
            }

            Debug.Log($"属性访问: {propertyTime}ticks, 字段访问: {fieldTime}ticks (字段/属性: {(float)fieldTime/propertyTime:F2}x)");
            
            if (nestedPropertyTime > 0 && nestedFieldTime > 0)
            {
                Debug.Log($"嵌套属性: {nestedPropertyTime}ticks, 嵌套字段: {nestedFieldTime}ticks (嵌套字段/嵌套属性: {(float)nestedFieldTime/nestedPropertyTime:F2}x)");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"字段vs属性测试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 结构体访问性能测试 - 添加预验证
    /// </summary>
    private static void RunStructTest()
    {
        var testObj = CreateComplexTestObject();
        
        var testPaths = new[] { "StructField.StructValue", "StructField.StructName", "StructField.Position.x" };
        var validPaths = testPaths.Where(path => testObj.HasPath(path)).ToList();
        
        Debug.Log($"结构体测试有效路径: {string.Join(", ", validPaths)}");

        try
        {
            // 测试结构体字段访问
            if (validPaths.Contains("StructField.StructValue"))
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount; i++)
                {
                    int value = PropertyAccessor.GetValue<int>(testObj, "StructField.StructValue");
                    PropertyAccessor.SetValue(testObj, "StructField.StructValue", value + 1);
                }
                sw.Stop();
                Debug.Log($"结构体int字段: {sw.ElapsedTicks}ticks");
            }

            // 测试结构体内字符串字段
            if (validPaths.Contains("StructField.StructName"))
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount; i++)
                {
                    string value = PropertyAccessor.GetValue<string>(testObj, "StructField.StructName");
                    PropertyAccessor.SetValue(testObj, "StructField.StructName", "test" + i);
                }
                sw.Stop();
                Debug.Log($"结构体string字段: {sw.ElapsedTicks}ticks");
            }

            // 测试Vector3结构体访问
            if (validPaths.Contains("StructField.Position.x"))
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount; i++)
                {
                    float x = PropertyAccessor.GetValue<float>(testObj, "StructField.Position.x");
                    PropertyAccessor.SetValue(testObj, "StructField.Position.x", x + 0.1f);
                }
                sw.Stop();
                Debug.Log($"Vector3.x字段: {sw.ElapsedTicks}ticks");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"结构体测试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 集合类型性能测试 - 添加预验证
    /// </summary>
    private static void RunCollectionTests()
    {
        var testObj = CreateComplexTestObject();
        
        var testPaths = new[] { 
            "Items[0].PropertyValue", 
            "Array[0]", 
            "ClassArray[0].PropertyValue",
            "Items[0].Array[1]"
        };
        var validPaths = testPaths.Where(path => testObj.HasPath(path)).ToList();
        
        Debug.Log($"集合测试有效路径: {string.Join(", ", validPaths)}");

        try
        {
            // List索引访问
            if (validPaths.Contains("Items[0].PropertyValue"))
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount; i++)
                {
                    int value = PropertyAccessor.GetValue<int>(testObj, "Items[0].PropertyValue");
                    PropertyAccessor.SetValue(testObj, "Items[0].PropertyValue", value + 1);
                }
                sw.Stop();
                Debug.Log($"List[0]访问: {sw.ElapsedTicks}ticks");
            }

            // 数组索引访问
            if (validPaths.Contains("Array[0]"))
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount; i++)
                {
                    int value = PropertyAccessor.GetValue<int>(testObj, "Array[0]");
                    PropertyAccessor.SetValue(testObj, "Array[0]", value + 1);
                }
                sw.Stop();
                Debug.Log($"Array[0]访问: {sw.ElapsedTicks}ticks");
            }

            // 对象数组访问
            if (validPaths.Contains("ClassArray[0].PropertyValue"))
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount; i++)
                {
                    int value = PropertyAccessor.GetValue<int>(testObj, "ClassArray[0].PropertyValue");
                    PropertyAccessor.SetValue(testObj, "ClassArray[0].PropertyValue", value + 1);
                }
                sw.Stop();
                Debug.Log($"ClassArray[0]访问: {sw.ElapsedTicks}ticks");
            }

            // 多维数组访问模拟
            if (validPaths.Contains("Items[0].Array[1]"))
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount; i++)
                {
                    int value = PropertyAccessor.GetValue<int>(testObj, "Items[0].Array[1]");
                    PropertyAccessor.SetValue(testObj, "Items[0].Array[1]", value + 1);
                }
                sw.Stop();
                Debug.Log($"嵌套数组访问: {sw.ElapsedTicks}ticks");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"集合测试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 字典访问性能测试 - 添加预验证
    /// </summary>
    private static void RunDictionaryTests()
    {
        var testObj = CreateComplexTestObject();
        
        // 测试字典访问（预验证）
        bool simpleDictSupported = false;
        try
        {
            string testValue = PropertyAccessor.GetValue<string>(testObj, "SimpleDict[1]");
            PropertyAccessor.SetValue(testObj, "SimpleDict[1]", "test_check");
            string newValue = PropertyAccessor.GetValue<string>(testObj, "SimpleDict[1]");
            simpleDictSupported = newValue == "test_check";
        }
        catch (Exception ex)
        {
            Debug.Log($"字典访问不支持: {ex.Message}");
        }

        if (simpleDictSupported)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount / 10; i++)
                {
                    string value = PropertyAccessor.GetValue<string>(testObj, "SimpleDict[1]");
                    PropertyAccessor.SetValue(testObj, "SimpleDict[1]", "test" + i);
                }
                sw.Stop();
                Debug.Log($"简单字典[int]访问: {sw.ElapsedTicks}ticks");
            }
            catch (Exception ex)
            {
                Debug.LogError($"字典性能测试失败: {ex.Message}");
            }
        }
        else
        {
            Debug.Log("字典索引访问功能不支持，跳过字典性能测试");
        }

        // 测试字典相关的扩展方法
        bool hasSimpleDict = testObj.HasPath("SimpleDict");
        bool hasNestedDict = testObj.HasPath("ComplexData.NestedDict");
        Debug.Log($"字典路径检测 - SimpleDict: {hasSimpleDict}, NestedDict: {hasNestedDict}");
    }

    /// <summary>
    /// 复杂路径性能测试 - 添加路径预验证
    /// </summary>
    private static void RunComplexPathTests()
    {
        var testObj = CreateComplexTestObject();
        
        var testPaths = new[] {
            "Child.Items[0].StructField.StructValue",
            "ComplexData.Matrix.m00",
            "Child.Items[0].PropertyValue"  // 简化超复杂路径
        };

        foreach (var path in testPaths)
        {
            bool pathValid = testObj.HasPath(path);
            Debug.Log($"路径 '{path}' 有效性: {pathValid}");
            
            if (!pathValid) continue;

            try
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < TestCount / 10; i++)
                {
                    var result = testObj.GetValueOrDefault<object>(path, null);
                    if (result != null)
                    {
                        // 尝试写入测试（只对支持的类型）
                        if (result is int intVal)
                        {
                            testObj.TrySetValue(path, intVal + 1);
                        }
                        else if (result is float floatVal)
                        {
                            testObj.TrySetValue(path, floatVal + 0.1f);
                        }
                    }
                }
                sw.Stop();
                Debug.Log($"路径 '{path}': {sw.ElapsedTicks}ticks");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"路径 '{path}' 测试异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 缓存效果测试 - 添加路径预验证
    /// </summary>
    private static void RunCacheEffectivenessTest()
    {
        var testObj = CreateComplexTestObject();
        string[] testPaths = {
            "PropertyValue",
            "FieldValue",
            "Child.PropertyValue",
            "Items[0].Name",
            "Array[0]",
            "StructField.StructValue"
        };

        // 过滤有效路径
        var validPaths = testPaths.Where(path => testObj.HasPath(path)).ToArray();
        Debug.Log($"缓存测试有效路径: {string.Join(", ", validPaths)}");

        if (validPaths.Length == 0)
        {
            Debug.LogWarning("没有有效路径进行缓存测试");
            return;
        }

        try
        {
            // 第一次运行（冷缓存)
            var sw = Stopwatch.StartNew();
            foreach (var path in validPaths)
            {
                for (int i = 0; i < TestCount / 10; i++)
                {
                    PropertyAccessor.GetValue<object>(testObj, path);
                }
            }
            sw.Stop();
            var coldCacheTime = sw.ElapsedTicks;

            // 第二次运行（热缓存)
            sw.Restart();
            foreach (var path in validPaths)
            {
                for (int i = 0; i < TestCount / 10; i++)
                {
                    PropertyAccessor.GetValue<object>(testObj, path);
                }
            }
            sw.Stop();
            var hotCacheTime = sw.ElapsedTicks;

            float cacheRatio = (float)coldCacheTime / hotCacheTime;
            Debug.Log($"冷缓存: {coldCacheTime}ticks, 热缓存: {hotCacheTime}ticks");
            Debug.Log($"缓存效果: {cacheRatio:F2}x 性能提升");
        }
        catch (Exception ex)
        {
            Debug.LogError($"缓存效果测试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 异常路径性能测试 - 增强异常处理
    /// </summary>
    private static void RunExceptionPathTests()
    {
        var testObj = CreateComplexTestObject();
        
        try
        {
            // 测试不存在路径的性能
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < TestCount / 10; i++)
            {
                testObj.GetValueOrDefault<int>("NonExistent.Path.Here", -1);
            }
            sw.Stop();
            var safeAccessTime = sw.ElapsedTicks;

            // 测试路径验证性能
            sw.Restart();
            for (int i = 0; i < TestCount / 10; i++)
            {
                testObj.ValidatePropertyPath("Items[999].NonExistent");
            }
            sw.Stop();
            var validationTime = sw.ElapsedTicks;

            // 测试HasPath性能
            sw.Restart();
            for (int i = 0; i < TestCount; i++)
            {
                testObj.HasPath("PropertyValue");
                testObj.HasPath("NonExistent.Path");
            }
            sw.Stop();
            var hasPathTime = sw.ElapsedTicks;

            Debug.Log($"安全访问不存在路径: {safeAccessTime}ticks");
            Debug.Log($"路径验证: {validationTime}ticks");
            Debug.Log($"HasPath检测: {hasPathTime}ticks");
        }
        catch (Exception ex)
        {
            Debug.LogError($"异常路径测试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 扩展方法功能测试 - 增强错误处理
    /// </summary>
    private static void TestNewExtensions()
    {
        var testObj = CreateComplexTestObject();
        
        try
        {
            // 测试路径验证
            var validationResult = testObj.ValidatePropertyPath("Items[0].PropertyValue");
            Debug.Log($"路径验证结果: {validationResult.IsValid}, 错误: {validationResult.GetAllMessages()}");
            
            // 测试批量操作
            var sw = Stopwatch.StartNew();
            var paths = new[] { "PropertyValue", "FieldValue", "Name" };
            var validPathsForBatch = paths.Where(path => testObj.HasPath(path)).ToArray();
            
            if (validPathsForBatch.Length > 0)
            {
                var values = testObj.GetValues<object>(validPathsForBatch);
                sw.Stop();
                Debug.Log($"批量获取{validPathsForBatch.Length}个值耗时: {sw.ElapsedTicks}ticks, 结果: [{string.Join(", ", values)}]");
                
                // 测试批量设置
                sw.Restart();
                var setOperations = new List<(string, object)>();
                if (testObj.HasPath("PropertyValue")) setOperations.Add(("PropertyValue", 100));
                if (testObj.HasPath("Name")) setOperations.Add(("Name", "NewName"));
                
                if (setOperations.Count > 0)
                {
                    testObj.SetValues<object>(setOperations.ToArray());
                    sw.Stop();
                    Debug.Log($"批量设置{setOperations.Count}个值耗时: {sw.ElapsedTicks}ticks");
                }
            }
            else
            {
                Debug.LogWarning("没有有效路径进行批量操作测试");
            }
            
            // 测试路径发现
            sw.Restart();
            var allPaths = testObj.GetAllPaths(2);
            sw.Stop();
            Debug.Log($"发现所有路径（深度2）耗时: {sw.ElapsedTicks}ticks, 共找到 {allPaths.Count} 个路径");
            if (allPaths.Count > 0)
            {
                Debug.Log($"前10个路径: {string.Join(", ", allPaths.Take(10))}");
            }
            
            // 测试类型兼容性验证
            if (testObj.HasPath("PropertyValue"))
            {
                var typeValidation = testObj.ValidateTypeCompatibility<int>("PropertyValue");
                Debug.Log($"类型兼容性验证: {typeValidation.IsValid}");
            }
            
            // 测试集合索引验证
            if (testObj.HasPath("Items"))
            {
                var indexValidation = testObj.ValidateCollectionIndex("Items", 0);
                Debug.Log($"集合索引验证: {indexValidation.IsValid}");
            }
            
            // 测试对象完整性验证
            var integrityPaths = new[] { "PropertyValue", "Name", "Items" }.Where(path => testObj.HasPath(path)).ToArray();
            if (integrityPaths.Length > 0)
            {
                var integrityResult = testObj.ValidateObjectIntegrity(integrityPaths);
                Debug.Log($"对象完整性验证: {integrityResult.IsValid}, 警告数: {integrityResult.Warnings.Count}");
            }
            
            // 测试安全操作
            sw.Restart();
            bool setSuccess = testObj.TrySetValue("NonExistent.Path", 123);
            var safeValue = testObj.GetValueOrDefault<int>("NonExistent.Path", 999);
            sw.Stop();
            Debug.Log($"安全操作测试耗时: {sw.ElapsedTicks}ticks, 设置成功: {setSuccess}, 安全获取值: {safeValue}");
            
            // 测试路径存在性检查
            bool hasValidPath = testObj.HasPath("PropertyValue");
            bool hasInvalidPath = testObj.HasPath("NonExistent");
            Debug.Log($"路径存在性检查 - 有效路径: {hasValidPath}, 无效路径: {hasInvalidPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"扩展方法测试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// PropertyContainer专项测试
    /// </summary>
    private static void RunPropertyContainerTests()
    {
        var testObj = CreateComplexTestObject();
        
        var testPaths = new[] {
            "PropertyValue",
            "FieldValue", 
            "Name",
            "Child.PropertyValue",
            "Child.FieldValue",
            "StructField.StructValue",
            "StructField.StructName",
            "Items[0].PropertyValue",
            "Array[0]",
            "ClassArray[0].PropertyValue"
        };

        var supportedPaths = new List<string>();
        var unsupportedPaths = new List<string>();

        Debug.Log("PropertyContainer路径支持性检测:");

        foreach (var path in testPaths)
        {
            try
            {
                PropertyPath propertyPath = new PropertyPath(path);
                
                // 尝试读取
                var value = PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                
                // 尝试写入（对于支持的类型）
                bool writeSuccess = false;
                if (value is int intVal)
                {
                    PropertyContainer.SetValue(testObj, propertyPath, intVal + 1);
                    var newValue = PropertyContainer.GetValue<TestClass, int>(testObj, propertyPath);
                    writeSuccess = (newValue == intVal + 1);
                    // 恢复原值
                    PropertyContainer.SetValue(testObj, propertyPath, intVal);
                }
                else if (value is string strVal)
                {
                    PropertyContainer.SetValue(testObj, propertyPath, strVal + "_test");
                    var newValue = PropertyContainer.GetValue<TestClass, string>(testObj, propertyPath);
                    writeSuccess = (newValue == strVal + "_test");
                    // 恢复原值
                    PropertyContainer.SetValue(testObj, propertyPath, strVal);
                }
                else
                {
                    writeSuccess = true; // 对于其他类型，只要能读取就算支持
                }

                if (writeSuccess)
                {
                    supportedPaths.Add(path);
                    Debug.Log($"  ✓ {path} (类型: {value?.GetType().Name ?? "null"})");
                }
                else
                {
                    unsupportedPaths.Add($"{path} (写入失败)");
                    Debug.Log($"  ⚠ {path} (读取成功，写入失败)");
                }
            }
            catch (Exception ex)
            {
                unsupportedPaths.Add($"{path} ({ex.GetType().Name})");
                Debug.Log($"  ✗ {path} (异常: {ex.GetType().Name})");
            }
        }

        Debug.Log($"\nPropertyContainer支持汇总:");
        Debug.Log($"  支持的路径: {supportedPaths.Count}/{testPaths.Length}");
        Debug.Log($"  不支持的路径: {unsupportedPaths.Count}/{testPaths.Length}");

        // 对支持的路径进行性能测试
        if (supportedPaths.Count > 0)
        {
            Debug.Log("\nPropertyContainer性能测试 (支持的路径):");
            
            var sw = Stopwatch.StartNew();
            foreach (var path in supportedPaths.Take(3)) // 只测试前3个以节省时间
            {
                try
                {
                    PropertyPath propertyPath = new PropertyPath(path);
                    for (int i = 0; i < TestCount / 10; i++)
                    {
                        var value = PropertyContainer.GetValue<TestClass, object>(testObj, propertyPath);
                        // 简单的写入测试
                        if (value is int intVal)
                        {
                            PropertyContainer.SetValue(testObj, propertyPath, intVal + 1);
                        }
                    }
                }
                catch
                {
                    // 忽略性能测试中的异常
                }
            }
            sw.Stop();
            
            Debug.Log($"  PropertyContainer批量操作耗时: {sw.ElapsedTicks}ticks");
            
            // 与PropertyAccessor对比
            sw.Restart();
            foreach (var path in supportedPaths.Take(3))
            {
                try
                {
                    for (int i = 0; i < TestCount / 10; i++)
                    {
                        var value = PropertyAccessor.GetValue<object>(testObj, path);
                        if (value is int intVal)
                        {
                            PropertyAccessor.SetValue(testObj, path, intVal + 1);
                        }
                    }
                }
                catch
                {
                    // 忽略性能测试中的异常
                }
            }
            sw.Stop();
            
            Debug.Log($"  PropertyAccessor批量操作耗时: {sw.ElapsedTicks}ticks");
        }
        else
        {
            Debug.LogWarning("PropertyContainer无支持的路径，跳过性能测试");
        }
    }

    /// <summary>
    /// 创建复杂的测试对象
    /// </summary>
    private static TestClass CreateComplexTestObject()
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
            SimpleDict = new Dictionary<int, string>
            {
                { 1, "One" },
                { 2, "Two" },
                { 3, "Three" }
            },
            StructField = new TestStruct
            {
                StructValue = 100,
                StructName = "TestStruct",
                Position = new Vector3(1, 2, 3)
            },
            ComplexData = new TestComplexData
            {
                Matrix = Matrix4x4.identity,
                Colors = new Color[] { Color.red, Color.green, Color.blue },
                NestedDict = new Dictionary<string, Dictionary<int, TestStruct>>(),
                StructArrayList = new List<TestStruct[]>()
            }
        };

        // 创建子对象
        result.Child = new TestClass
        {
            PropertyValue = 84,
            FieldValue = 48,
            Name = "ChildObject",
            Items = new List<TestClass>(),
            Array = new int[] { 10, 20, 30 },
            StructField = new TestStruct
            {
                StructValue = 200,
                StructName = "ChildStruct",
                Position = new Vector3(4, 5, 6)
            }
        };

        // 填充Items列表
        for (int i = 0; i < 3; i++)
        {
            var item = new TestClass
            {
                PropertyValue = i * 10,
                FieldValue = i * 5,
                Name = $"Item{i}",
                Array = new int[] { i, i+1, i+2 },
                StructField = new TestStruct
                {
                    StructValue = i * 100,
                    StructName = $"ItemStruct{i}",
                    Position = new Vector3(i, i+1, i+2)
                },
                Items = new List<TestClass>()  // 确保嵌套的Items也有初始化
            };
            result.Items.Add(item);
            if (result.Child.Items != null)
                result.Child.Items.Add(item);
        }

        // 填充ClassArray
        for (int i = 0; i < result.ClassArray.Length; i++)
        {
            result.ClassArray[i] = new TestClass
            {
                PropertyValue = i * 20,
                FieldValue = i * 10,
                Name = $"ArrayItem{i}",
                Items = new List<TestClass>()  // 确保数组中的对象也有初始化的Items
            };
        }

        // 填充Dictionary
        if (result.Items.Count > 1)
        {
            result.Dictionary["first"] = result.Items[0];
            result.Dictionary["second"] = result.Items[1];
        }

        // 填充复杂的嵌套字典
        result.ComplexData.NestedDict["group1"] = new Dictionary<int, TestStruct>
        {
            { 1, result.StructField },
            { 2, result.Child.StructField }
        };

        // 填充结构体数组列表
        result.ComplexData.StructArrayList.Add(new TestStruct[]
        {
            result.StructField,
            result.Child.StructField
        });

        return result;
    }

    /// <summary>
    /// 创建嵌套对象（优化深度控制，避免过深嵌套）
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
            // 确保Items列表有内容
            var itemToAdd = new TestClass
            {
                PropertyValue = i * 10,
                FieldValue = i * 10,
                Items = new List<TestClass>(),
                Array = new int[] { i*10, i*10+1, i*10+2 }
            };
            
            current.Items.Add(itemToAdd);
            
            // 只有在不是最后一层时才继续嵌套
            if (i < depth - 1)
            {
                current = current.Items[0];
            }
        }
        return root;
    }

    /// <summary>
    /// 获取属性路径（优化路径生成，避免过深嵌套）
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
}
