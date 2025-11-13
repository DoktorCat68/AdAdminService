namespace AdAdminPortal.Models
{
    public class AdUserDto
    {
        public string? Sam { get; set; }          // samAccountName
        public string? DisplayName { get; set; }  // CN / имя
        public string? Email { get; set; }
        public bool Enabled { get; set; }
    }
}
