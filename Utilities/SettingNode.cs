using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AeonHacs.Utilities;

public class SettingNode
{
    public SettingNode Parent { get; set; }
    public List<SettingNode> Children { get; set; }

    public string Name { get; set; }

    public bool IsNamed
    {
        get { return !string.IsNullOrEmpty(Name); }
    }

    public bool HasSiblings
    {
        get { return Parent != null && Parent.Children != null && Parent.Children.Count > 1; }
    }

    public SettingNode() { }

    public SettingNode(object source)
    {
        Children = fetchChildren(source);
    }

    public SettingNode(SettingNode parent, string name, object source)
    {
        Parent = parent;
        Children = fetchChildren(source);

        Name = name;
    }

    List<SettingNode> fetchChildren(object source)
    {
        Type sourceType = source.GetType();
        var children = new List<SettingNode>();
        if (typeof(IList).IsAssignableFrom(sourceType))
        {
            IList List = source as IList;
            for (int i = 0; i < List.Count; i++)
            {
                object v = List[i];
                if (v.GetType().IsAtomic())
                    children.Add(new IndexedLeaf(this, source, i));
                else
                    children.Add(new SettingNode(this, getSourceName(v), v));
            }
        }
        else
        {
            PropertyInfo[] properties = sourceType.GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.GetGetMethod().IsStatic)
                    continue;
                object v = property.GetValue(source, null);
                if (property.CanWrite && property.JsonProperty() && v != null)
                {
                    if (property.PropertyType.IsAtomic())
                        children.Add(new PropertyLeaf(this, source, property));
                    else
                        children.Add(new SettingNode(this, property.Name, v));
                }
            }
        }
        return children;
    }

    string getSourceName(object source)
    {
        Type nodeType = source.GetType();
        PropertyInfo[] properties = nodeType.GetProperties();
        try
        {
            foreach (PropertyInfo property in properties)
            {
                if (property.Name.Equals("Name"))
                    return property.GetValue(source, null).ToString();
            }
        }
        catch { return null; }
        return nodeType.Name;
    }
}

public class Leaf : SettingNode
{
    public object Source { get; set; }
    public object Value { get; set; }
    public Type Type { get; set; }

    protected object parseValue(string valueString)
    {
        if (Type.IsEnum)
            return Enum.Parse(Type, valueString);
        else
        {
            try
            {
                return Convert.ChangeType(valueString, Type);
            }
            catch
            {
                if (Type == typeof(bool))
                {
                    return valueString.ToLower() == "yes";
                }
            }
        }
        return null;
    }

    public virtual void SetValue(string valueString)
    {
        Value = Convert.ChangeType(valueString, Type);
    }
}

public class IndexedLeaf : Leaf
{
    public int Index { get; set; }

    public IndexedLeaf(SettingNode parent, object source, int index)
    {
        Parent = parent;

        Name = "[" + index + "]";

        Source = source;
        Value = (source as IList)[index];
        Type = Value.GetType();

        Index = index;
    }

    public override void SetValue(string valueString)
    {
        (Source as IList)[Index] = parseValue(valueString);
    }
}

public class PropertyLeaf : Leaf
{
    public PropertyInfo Property { get; set; }

    public PropertyLeaf(SettingNode parent, object source, PropertyInfo property)
    {
        Parent = parent;

        Name = property.Name;

        Source = source;
        Value = property.GetValue(source, null);
        Type = property.PropertyType;

        Property = property;
    }

    public override void SetValue(string valueString)
    {
        Property.SetValue(Source, parseValue(valueString), null);
    }
}
