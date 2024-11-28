
using System;
using System.Collections.Generic;
using MiniProjectDesigner.Models;

namespace MiniProjectDesigner.ViewModels
{
    public class ProjectTypeViewModel : IViewModel
    {
        [ViewPrimaryKey]
        public int Id { get; set; }

        public string TypeNameEn { get; set; }

        public string TypeNameAr { get; set; }

        public bool? IsActive { get; set; }

        public DateTime? CreatedDate { get; set; }
        public Type EntityType => typeof(ProjectType);
    }
}
