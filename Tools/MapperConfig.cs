
using AutoMapper;

namespace MiniProjectDesigner.Tools
{
    public static class MapperConfig
    {
        private static IMapper _mapper;

        public static IMapper InitializeAutoMapper()
        {
            if (_mapper == null)
            {
                var config = new MapperConfiguration(cfg =>
                {
                    cfg.AddProfile<AutoMapperProfile>();
                });
                _mapper = config.CreateMapper();
            }
            return _mapper;
        }
    }
}
