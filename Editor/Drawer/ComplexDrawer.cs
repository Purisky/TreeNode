using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections.Concurrent;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = TreeNode.Utility.Debug;

namespace TreeNode.Editor
{
    // ComplexDrawer元数据缓存结构
    public class ComplexDrawerMetadata
    {
        public List<ComplexDrawer.MemberGroup> Groups { get; set; }
        public int Height { get; set; }
        public bool HasPort { get; set; }
        public MemberInfo TitlePortMember { get; set; }
        public HashSet<ComplexDrawer> ChildDrawers { get; set; }
    }

    public abstract class ComplexDrawer : BaseDrawer
    {
        // 静态缓存，避免重复计算
        private static readonly ConcurrentDictionary<Type, ComplexDrawerMetadata> _metadataCache = new();
        
        List<MemberGroup> Groups;
        public int Height;
        public bool HasPort;
        HashSet<ComplexDrawer> ChildDrawers = new();
        MemberInfo TitlePortMember;
        
        public ComplexDrawer()
        {
            // 使用缓存的元数据
            var metadata = _metadataCache.GetOrAdd(DrawType, BuildMetadata);
            Groups = metadata.Groups;
            Height = metadata.Height;
            HasPort = metadata.HasPort;
            TitlePortMember = metadata.TitlePortMember;
            ChildDrawers = metadata.ChildDrawers;
        }

        // 优化的元数据构建，一次性构建所有元数据
        private static ComplexDrawerMetadata BuildMetadata(Type drawType)
        {
            var metadata = new ComplexDrawerMetadata
            {
                Groups = new List<MemberGroup>(),
                ChildDrawers = new HashSet<ComplexDrawer>()
            };

            // 使用反射缓存替代直接调用DrawType.GetAll<ShowInNodeAttribute>()
            List<MemberInfo> members = ReflectionCache.GetCachedMembers<ShowInNodeAttribute>(drawType).ToList();
            bool rootDrawer = drawType.Inherited(typeof(JsonNode));

            for (int i = 0; i < members.Count; i++)
            {
                MemberInfo member = members[i];
                if (rootDrawer && metadata.TitlePortMember == null)
                {
                    // 使用缓存的属性获取
                    if (ReflectionCache.GetCachedAttribute<TitlePortAttribute>(member) != null && 
                        ReflectionCache.GetCachedAttribute<ChildAttribute>(member) != null && 
                        member.GetValueType() != typeof(NumValue))
                    {
                        metadata.TitlePortMember = member;
                        continue;
                    }
                }
                // 使用缓存的属性获取
                GroupAttribute groupAttribute = ReflectionCache.GetCachedAttribute<GroupAttribute>(member);
                string groupName = member.Name;
                if (groupAttribute != null && groupAttribute.Name != null)
                {
                    groupName = groupAttribute.Name;
                }
                MemberGroup tempGroup = GetTempGroup(metadata.Groups, groupName);
                tempGroup.ShowIf ??= groupAttribute?.ShowIf;
                if (tempGroup.Add(member))
                {
                    metadata.HasPort = true;
                }
                else
                {
                    Type type = member.GetValueType();
                    if (type.Inherited(typeof(IList)))
                    {
                        type = type.GetGenericArguments()[0];
                    }
                    if (type.IsComplex() && DrawerManager.TryGet(type, out BaseDrawer drawer) && drawer is ComplexDrawer complex)
                    {
                        metadata.ChildDrawers.Add(complex);
                    }
                }
            }
            metadata.Groups.Sort((a, b) => a.MinOrder.CompareTo(b.MinOrder));
            metadata.Height = metadata.Groups.Sum(n => Mathf.Max(1, n.PortMembers.Count)) * 24;
            if (!metadata.HasPort && metadata.ChildDrawers.Any())
            {
                metadata.HasPort = CheckPortRecursive(metadata.ChildDrawers, new HashSet<ComplexDrawer> { });
            }
            
            return metadata;
        }

        private static MemberGroup GetTempGroup(List<MemberGroup> groups, string name)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Name == name)
                {
                    return groups[i];
                }
            }
            MemberGroup tempGroup = new(name);
            groups.Add(tempGroup);
            return tempGroup;
        }

        // 优化的端口检查，使用静态方法避免实例方法调用
        private static bool CheckPortRecursive(HashSet<ComplexDrawer> childDrawers, HashSet<ComplexDrawer> visited)
        {
            foreach (var item in childDrawers)
            {
                if (visited.Contains(item))
                {
                    continue;
                }
                if (item.HasPort) { return true; }
                visited.Add(item);
                if (CheckPortRecursive(item.ChildDrawers, visited))
                {
                    return true;
                }
            }
            return false;
        }

        public bool CheckPort(HashSet<ComplexDrawer> visited)
        {
            return CheckPortRecursive(ChildDrawers, visited);
        }

        // 添加清理缓存的方法
        public static void ClearMetadataCache()
        {
            _metadataCache.Clear();
        }

        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PAPath path, Action action)
        {
            PropertyElement propertyElement = new(memberMeta, node, path, this);

            if (!string.IsNullOrEmpty(path))
            {
                object parent = node.Data.GetParent(path);
                action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            }
            Label label = CreateLabel(memberMeta.LabelInfo);
            label.style.marginLeft = 4;
            label.style.width = 70;




            propertyElement.style.flexDirection = FlexDirection.Row;
            propertyElement.Add(label);
            VisualElement content = new();
            content.style.flexGrow = 1;
            propertyElement.Add(content);
            if (TitlePortMember != null)
            {
                string propertyPath = $"{path}{TitlePortMember.Name}";
                MemberMeta meta = new(TitlePortMember, propertyPath);
                meta.LabelInfo.Hide = true;
                bool multi = TitlePortMember.GetValueType().Inherited(typeof(IList));
                ChildPort port = multi ? MultiPort.Create(node, meta) : SinglePort.Create(node,meta);
                port.Q<Label>().style.marginLeft = 0;
                port.Q<Label>().style.marginRight = 0;
                PropertyElement titlePropertyElement = new(meta, node, propertyPath, null, port);
                titlePropertyElement.style.position = Position.Absolute;
                titlePropertyElement.style.right = 0;
                titlePropertyElement.style.alignSelf = Align.Center;
                node.titleContainer.Add(titlePropertyElement);

                node.ChildPorts.Add(propertyPath,port);
            }
            for (int i = 0; i < Groups.Count; i++)
            {
                content.Add(Groups[i].Draw(node, path, action));
            }
            return propertyElement;
        }
        
        public class MemberGroup
        {
            public int MinOrder;
            public string Name;
            public List<MemberInfo> Members;
            public List<MemberInfo> PortMembers;
            public int LineCount;
            public string ShowIf;
            public MemberGroup(string name)
            {
                Name = name;
                Members = new();
                PortMembers = new();
            }
            public bool Add(MemberInfo memberInfo)
            {
                // 使用缓存的属性获取
                ShowInNodeAttribute showInNodeAttribute = ReflectionCache.GetCachedAttribute<ShowInNodeAttribute>(memberInfo);
                MinOrder = Math.Min(MinOrder, showInNodeAttribute.Order);
                if (showInNodeAttribute is ChildAttribute || memberInfo.GetValueType() == typeof(NumValue) || memberInfo.GetValueType().Inherited(typeof(NumValue)))
                {
                    PortMembers.Add(memberInfo);
                    return true;
                }
                else
                {
                    Members.Add(memberInfo);
                    return false;
                }
            }

            public ShowIfElement Draw(ViewNode node, string path, Action action)
            {
                ShowIfElement groupVE = new()
                {
                    name = $"Group_{Name}",
                };
                if (!string.IsNullOrEmpty(ShowIf))
                {
                    object parent = node.Data.GetParent(path);
                    MemberInfo memberInfo = parent.GetType().GetMember(ShowIf)[0];
                    if (memberInfo != null)
                    {
                        switch (memberInfo.MemberType)
                        {
                            case MemberTypes.Field:
                                groupVE.ShowIf = () => (bool)((FieldInfo)memberInfo).GetValue(parent);
                                break;
                            case MemberTypes.Method:
                                groupVE.ShowIf = ((MethodInfo)memberInfo).CreateDelegate(typeof(Func<bool>), parent) as Func<bool>;
                                break;
                            case MemberTypes.Property:
                                groupVE.ShowIf = ((PropertyInfo)memberInfo).GetGetMethod().CreateDelegate(typeof(Func<bool>), parent) as Func<bool>;
                                break;
                        }
                        if (ShowIf != null)
                        {
                            node.ShowIfElements.Add(groupVE);
                        }
                    }
                }
                groupVE.style.flexGrow = 1;
                LineCount = Mathf.Max(1, PortMembers.Count);
                for (int i = 0; i < LineCount; i++)
                {
                    VisualElement lineVE = new()
                    {
                        name = $"Group_{Name}_{i}",
                    };
                    lineVE.style.flexGrow = 1;
                    lineVE.style.flexDirection = FlexDirection.RowReverse;
                    lineVE.style.justifyContent = Justify.SpaceBetween;
                    groupVE.Add(lineVE);
                    if (PortMembers.Count > i)
                    {
                        MemberInfo member = PortMembers[i];

                        string propertyPath = string.IsNullOrEmpty(path)? member.Name: $"{path}.{member.Name}";




                        MemberMeta meta = new(member, propertyPath);
                        if (meta.Type== typeof(NumValue)||meta.Type.Inherited(typeof(NumValue)))
                        {
                            if (!DrawerManager.TryGet(typeof(NumValue), out BaseDrawer baseDrawer))
                            {
                                Debug.LogError($"this value type drawer not exist");
                                continue;
                            }
                            PropertyElement propertyElement = baseDrawer.Create(meta, node, propertyPath, action);
                            GroupAttribute groupAttribute = member.GetCustomAttribute<GroupAttribute>();
                            if (groupAttribute != null)
                            {
                                propertyElement.SetWidth(groupAttribute.Width);
                            }
                            NumPort port = propertyElement.Q<NumPort>();
                            lineVE.Add(propertyElement);
                            //Debug.Log(port.LocalPath);
                            node.ChildPorts.Add(port.LocalPath, port);
                        }
                        else
                        {
                            bool multi = meta.Type.Inherited(typeof(IList));
                            //Debug.Log(Name);
                            ChildPort port = multi ? MultiPort.Create(node, meta) : SinglePort.Create(node,meta);
                            PropertyElement propertyElement = new(meta, node, propertyPath, null, port);
                            GroupAttribute groupAttribute = member.GetCustomAttribute<GroupAttribute>();
                            if (groupAttribute != null)
                            {
                                propertyElement.SetWidth(groupAttribute.Width);
                            }
                            lineVE.Add(propertyElement);
                            node.ChildPorts.Add(propertyPath, port);
                        }




                    }
                    for (int j = Members.Count - 1; j >= 0; j--)
                    {
                        if (!DrawerManager.TryGet(Members[j], out BaseDrawer baseDrawer))
                        {
                            Debug.LogError($"this value type drawer not exist [{Members[j].GetValueType()}]");
                            continue;
                        }
                        string propertyPath = string.IsNullOrEmpty(path) ? Members[j].Name : $"{path}.{Members[j].Name}";
                        MemberMeta meta = new(Members[j], propertyPath);
                        PropertyElement propertyElement = baseDrawer.Create(meta, node, propertyPath, action);
                        GroupAttribute groupAttribute = Members[j].GetCustomAttribute<GroupAttribute>();
                        if (groupAttribute != null)
                        {
                            propertyElement.SetWidth(groupAttribute.Width);
                        }
                        lineVE.Add(propertyElement);

                    }



                }
                return groupVE;
            }


        }



    }


    public class ComplexDrawer<T> : ComplexDrawer
    {
        public override Type DrawType => typeof(T);
    }


    public static class ComplexDrawerExtensions
    { 
        public static bool IsComplex(this Type type)
        {
            return ReflectionCache.GetCachedMembers<ShowInNodeAttribute>(type).Any();
        }
    }
}
