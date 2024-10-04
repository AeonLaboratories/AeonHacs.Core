using System;

namespace AeonHacs;

public class StandardValuesSourceAttribute : Attribute
{
    public string PropertyName { get; }

    public StandardValuesSourceAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }
}
