
using AutoMapper;
using MiniProjectDesigner.ViewModels;
using MiniProjectDesigner.Models;

namespace MiniProjectDesigner.Tools
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            CreateMap<ProjectType, ProjectTypeViewModel>().ReverseMap();
        }
    }
}
