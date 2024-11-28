using System;
using System.Collections.Generic;

namespace MiniProjectDesigner.Models;

public partial class ProjectType
{
    public int Id { get; set; }

    public string TypeNameEn { get; set; }

    public string TypeNameAr { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedDate { get; set; }

}
