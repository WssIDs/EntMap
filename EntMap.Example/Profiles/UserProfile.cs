using EntMap.Example.Models;
using EntMap.Example.Profile;
using EntMap.Exmaple.Dto;

namespace EntMap.Example.Profiles
{
    internal class UserProfile : EntMappingProfile
    {
        public UserProfile()
        {
            CreateMap<UserDto, UserModel>()
                .ForMember(x => x.Email, x => x.Email)
                .ForMember(x => x.Name, x => $"{x.Name} New Name");
        }
    }
}
