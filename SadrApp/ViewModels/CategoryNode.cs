using System.Collections.ObjectModel;

namespace SadrApp.ViewModels;

public class CategoryNode : ObservableBase
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public ObservableCollection<CategoryNode> Children { get; set; } = new();
}
