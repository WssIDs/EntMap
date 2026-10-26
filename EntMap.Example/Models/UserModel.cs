namespace EntMap.Example.Models
{
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
}
