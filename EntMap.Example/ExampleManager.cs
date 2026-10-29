using EntMap.Example.Extensions;
using EntMap.Example.Models;
using EntMap.Exmaple.Dto;

namespace EntMap.Example
{
    internal static class ExampleManager
    {
        public static void SingleObjectMapping()
        {
            var source = new UserDto()
            {
                Id = 1,
                Name = "Test1",
                Email = "test1@mail.com"
            };

            var dest = EntMapper.Map<UserModel>(source);

            var dd = source.ToModel();

            Console.WriteLine(dest);
        }

        public static void ListObjectsMapping()
        {
            var source = new List<UserDto>
            {
                new() {
                    Id = 1,
                    Name = "Test1",
                    Email = "test1@mail.com"
                },
                new() {
                    Id = 2,
                    Name = "Test2",
                    Email = "test2@mail.com"
                },
                new() {
                    Id = 3,
                    Name = "Test3",
                    Email = "test3@mail.com"
                }
            };

            var dest = EntMapper.Map<List<UserModel>>(source);

            foreach (var item in dest)
            {
                Console.WriteLine(item);
            }
        }

        public static void ListObjectsMappingExtensions()
        {
            var source = new List<UserDto>
            {
                new() {
                    Id = 1,
                    Name = "Test1",
                    Email = "test1@mail.com"
                },
                new() {
                    Id = 2,
                    Name = "Test2",
                    Email = "test2@mail.com"
                },
                new() {
                    Id = 3,
                    Name = "Test3",
                    Email = "test3@mail.com"
                }
            };

            var dest = source.ToModel();

            foreach (var item in dest)
            {
                Console.WriteLine(item);
            }
        }
    }
}