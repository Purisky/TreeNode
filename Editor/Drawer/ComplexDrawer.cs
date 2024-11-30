using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public abstract class ComplexDrawer : BaseDrawer
    {
        List<MemberGroup> Groups;
        public int Height;
        public bool HasPort;
        HashSet<ComplexDrawer> ChildDrawers = new();
        MemberInfo TitlePortMember;
        public ComplexDrawer()
        {
            List<MemberInfo> members = DrawType.GetAll<ShowInNodeAttribute>();
            bool rootDrawer = DrawType.Inherited(typeof(JsonNode));

            Groups = new();
            for (int i = 0; i < members.Count; i++)
            {
                MemberInfo member = members[i];
                if (rootDrawer && TitlePortMember == null)
                {
                    if (member.GetCustomAttribute<TitlePortAttribute>() != null&& member.GetCustomAttribute<ChildAttribute>() != null&& member.GetValueType()!= typeof(NumValue))
                    {
                        TitlePortMember = member;
                        continue;
                    }
                }
                GroupAttribute groupAttribute = member.GetCustomAttribute<GroupAttribute>();
                string groupName = member.Name;
                if (groupAttribute != null && groupAttribute.Name != null)
                {
                    groupName = groupAttribute.Name;
                }
                MemberGroup tempGroup = getTempGroup(groupName);
                tempGroup.ShowIf ??= groupAttribute?.ShowIf;
                if (tempGroup.Add(member))
                {
                    HasPort = true;
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
                        ChildDrawers.Add(complex);
                    }
                }
            }
            Groups.Sort((a, b) => a.MinOrder.CompareTo(b.MinOrder));
            Height = Groups.Sum(n => Mathf.Max(1, n.PortMembers.Count)) * 24;
            if (!HasPort && ChildDrawers.Any())
            {
                HasPort = CheckPort(new() { this });
            }
            MemberGroup getTempGroup(string name)
            {
                for (int i = 0; i < Groups.Count; i++)
                {
                    if (Groups[i].Name == name)
                    {
                        return Groups[i];
                    }
                }
                MemberGroup tempGroup = new(name);
                Groups.Add(tempGroup);
                return tempGroup;
            }
        }

        public bool CheckPort(HashSet<ComplexDrawer> visited)
        {
            foreach (var item in ChildDrawers)
            {
                if (visited.Contains(item))
                {
                    continue;
                }
                if (item.HasPort) { return true; }
                visited.Add(item);
                if (item.CheckPort(visited))
                {
                    return true;
                }
            }
            return false;


        }
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PropertyPath path, Action action)
        {
            PropertyElement propertyElement = new(memberMeta, node, path, this);
            
            object parent = node.Data.GetParent(path);
            action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
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
                PropertyPath propertyPath = PropertyPath.AppendName(path, TitlePortMember.Name);
                MemberMeta meta = new(TitlePortMember, propertyPath);
                meta.LabelInfo.Hide = true;
                bool multi = TitlePortMember.GetValueType().Inherited(typeof(IList));
                ChildPort port = multi ? MultiPort.Create(meta) : SinglePort.Create(meta);
                port.Q<Label>().style.marginLeft = 0;
                port.Q<Label>().style.marginRight = 0;
                PropertyElement titlePropertyElement = new(meta, node, propertyPath, null, port);
                titlePropertyElement.style.position = Position.Absolute;
                titlePropertyElement.style.right = 0;
                titlePropertyElement.style.alignSelf = Align.Center;
                node.titleContainer.Add(titlePropertyElement);
                node.ChildPorts.Add(port);
            }
            for (int i = 0; i < Groups.Count; i++)
            {
                content.Add(Groups[i].Draw(node, path, action));
            }
            return propertyElement;
        }
        class MemberGroup
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
                ShowInNodeAttribute showInNodeAttribute = memberInfo.GetCustomAttribute<ShowInNodeAttribute>();
                MinOrder = Math.Min(MinOrder, showInNodeAttribute.Order);
                if (showInNodeAttribute is ChildAttribute || memberInfo.GetValueType() == typeof(NumValue))
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

            public ShowIfElement Draw(ViewNode node, PropertyPath path, Action action)
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
                    groupVE.Add(lineVE);
                    if (PortMembers.Count > i)
                    {
                        MemberInfo member = PortMembers[i];
                        PropertyPath propertyPath = PropertyPath.AppendName(path, member.Name);
                        MemberMeta meta = new(member, propertyPath);
                        //Debug.Log(meta.ShowInNode);
                        if (member.GetValueType() == typeof(NumValue))
                        {
                            if (!DrawerManager.TryGet(member, out BaseDrawer baseDrawer))
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
                            node.ChildPorts.Add(port);
                        }
                        else
                        {
                            bool multi = member.GetValueType().Inherited(typeof(IList));
                            //Debug.Log(Name);
                            ChildPort port = multi ? MultiPort.Create(meta) : SinglePort.Create(meta);
                            PropertyElement propertyElement = new(meta, node, propertyPath,null, port);
                            GroupAttribute groupAttribute = member.GetCustomAttribute<GroupAttribute>();
                            if (groupAttribute != null)
                            {
                                propertyElement.SetWidth(groupAttribute.Width);
                            }
                            lineVE.Add(propertyElement);
                            node.ChildPorts.Add(port);
                        }
                    }
                    for (int j = Members.Count - 1; j >= 0; j--)
                    {
                        if (!DrawerManager.TryGet(Members[j], out BaseDrawer baseDrawer))
                        {
                            Debug.LogError($"this value type drawer not exist");
                            continue;
                        }
                        PropertyPath propertyPath = PropertyPath.AppendName(path, Members[j].Name);
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
            return type.GetAll<ShowInNodeAttribute>().Any();
        }
    }
}
