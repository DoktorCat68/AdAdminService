namespace AdAdminPortal.Models
{
    public class CreateGroupViewModel
    {
        public string Sam { get; set; } = string.Empty;
        public string? Description { get; set; }

        // Выбранный OU (DN)
        public string? SelectedOu { get; set; }

        // Дерево OU (как у пользователей)
        public List<OuTreeNode> OuTree { get; set; } = new();
    }
}
