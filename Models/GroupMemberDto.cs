namespace AdAdminPortal.Models
{
    public enum GroupMemberType
    {
        User = 0,
        Group = 1
    }

    public class GroupMemberDto
    {
        public string Sam { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public GroupMemberType Type { get; set; }
    }
}
