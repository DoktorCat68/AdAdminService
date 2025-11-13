using Microsoft.AspNetCore.Mvc.Rendering;

namespace AdAdminPortal.Models
{
    public class CreateAdUserViewModel
    {
        // учетная запись
        public string Sam { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;

        // ФИО
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }

        // Дополнительно
        public string? Email { get; set; }
        public string? JobTitle { get; set; }
        public string? Telephone { get; set; }

        // Выбранный OU (DN)
        public string? SelectedOu { get; set; }

        // Старый список можно оставить, но он уже не нужен для дерева
        public List<SelectListItem> Ous { get; set; } = new();

        // Новое: дерево OU
        public List<OuTreeNode> OuTree { get; set; } = new();
        public string? TemplateSam { get; set; }   // с кого копируем
        public bool CloneGroups { get; set; } = true; // копировать ли группы
    }
}
