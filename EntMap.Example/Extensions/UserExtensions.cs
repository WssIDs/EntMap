using EntMap.Example.Models;
using EntMap.Exmaple.Dto;

namespace EntMap.Example.Extensions
{
    public static partial class UserExtensions
    {
        // Important
        [GenerateMap]
        public static partial UserModel ToModel(this UserDto data);

        [GenerateMap]
        public static partial List<UserModel> ToModel(this List<UserDto> data);
    }
}
