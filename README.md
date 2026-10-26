# EntMap
#### Source generator and runtime infrastructure for high-performance mapping

This library is designed to generate data mapping classes. Because it relies on compile-time code generation rather than runtime processing, it delivers exceptionally high performance. Additionally, it is incredibly straightforward and easy to use.


## Usage Example
Here is a quick example showing how to directly map a source object to a target class using `EntMapper`.


### 1. Define Your Classes
```csharp
// Source DTO
public class UserDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
}
// Target Domain Model
public class UserModel
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }

    public override string ToString()
    {
        return $"{Id} {Name} {Email}";
    }
}
```

### 2. Map Object Directly
```csharp
var source = new UserDto()
{
    Id = 1,
    Name = "Test1",
    Email = "test1@mail.com"
};

var dest = EntMapper.Map<UserModel>(source);

Console.WriteLine(dest);

// Result: 
// 1 Test1 test1@mail.com
```

### 3. Map List Objects Directly
```csharp
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

// Results:
// 1 Test1 test1@mail.com
// 2 Test2 test2@mail.com
// 3 Test3 test3@mail.com
```

## Usage Example with Profiles

If you need custom mapping rules (e.g., when property names do not match), you can define a **Mapping Profile**. Since everything is processed via compile-time code generation, these profiles are resolved with maximum performance and zero runtime overhead.

### 1. Create a Mapping Profile

Inherit from `MappingProfile` to configure explicit member matching rules.

```csharp
public class UserProfile : MappingProfile
{
    public UserProfile()
    {
        CreateMap<UserDto, UserModel>()
            .ForMember(x => x.Email, x => x.Email)
            .ForMember(x => x.Name, x => $"{x.Name} New Name");
    }
}
```

### 3. Map Objects Directly

```csharp
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

// Results:
// 1 Test1 New Name test1@mail.com
// 2 Test2 New Name test2@mail.com
// 3 Test3 New Name test3@mail.com
```

## Usage Example with Extension Methods
#### Without Mapping Profiles - WIP

You can perform data mapping directly on the objects using **extension methods**. Thanks to compile-time code generation, these methods transform your data with maximum execution speed and zero reflection.

### 1. Create Extension

```csharp
    public static partial class UserExtensions
    {
        // Important
        [GenerateMap]
        public static partial UserModel ToModel(this UserDto data);

        [GenerateMap]
        public static partial List<UserModel> ToModel(this List<UserDto> data);
    }
```

### 2. Map Objects Directly

```csharp
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

// Results:
// 1 Test1 test1@mail.com
// 2 Test2 test2@mail.com
// 3 Test3 test3@mail.com 
```