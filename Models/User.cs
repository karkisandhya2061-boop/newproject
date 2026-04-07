using System.Globalization;

namespace WebApplication1.Models
{
    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; }   
        public string LastName { get; set; }    
        public string Email { get; set; }   
        
        public string Phone { get; set; }

        public string Address { get; set; }

        public StringInfo Country { get; set; }
        public string Password { get; set; }    
        public string Role { get; set; } = "user";
    }
}
