
using System;

namespace MiniProjectDesigner.ViewModels
{
    public interface IViewModel
    {
        Type EntityType { get; }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ViewPrimaryKeyAttribute : Attribute
    {
    }
}
