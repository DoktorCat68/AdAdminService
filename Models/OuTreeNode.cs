namespace AdAdminPortal.Models
{
    public class OuTreeNode
    {
        public string Name { get; set; } = string.Empty;               // Просто имя OU (без "OU=")
        public string DistinguishedName { get; set; } = string.Empty;  // Полный DN
        public List<OuTreeNode> Children { get; set; } = new();        // Вложенные OU
    }
}
