
using System;
using System.Collections.Generic;

namespace MiniProjectDesigner.ViewModels
{
    public class ProjectTypeViewModel : IViewModel
    {
        [ViewPrimaryKey]
        public int Id { get; set; }
        public string TypeName { get; set; }
        public bool? IsActive { get; set; }
        public Type EntityType => typeof(ProjectType);
    }
}
