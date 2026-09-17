using System.Collections.ObjectModel;
using System.Globalization;

namespace SadrApp.ViewModels;

/// <summary>Tree node for the projects page: project + its sub-projects.</summary>
public class ProjectNode : ObservableBase
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Manager { get; set; } = "";
    public int Progress { get; set; }
    public int Status { get; set; }
    public string StartDate { get; set; } = "";
    public decimal Price { get; set; }
    public int ChildrenCount { get; set; }
    public ObservableCollection<ProjectNode> Children { get; set; } = new();

    public string StatusText => Status switch
    {
        0 => "تعریف شده",
        1 => "در حال اجرا",
        2 => "متوقف شده",
        3 => "لغو شده",
        4 => "خاتمه یافته",
        _ => "نامشخص"
    };

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (ChildrenCount > 0) parts.Add($"{ChildrenCount} زیرپروژه");
            if (!string.IsNullOrWhiteSpace(Manager)) parts.Add($"مدیر: {Manager}");
            if (Price > 0) parts.Add($"مبلغ: {Price:N0}");
            if (Progress > 0) parts.Add($"پیشرفت: {Progress}٪");
            if (!string.IsNullOrWhiteSpace(StartDate)) parts.Add($"شروع: {StartDate}");
            return string.Join(" | ", parts);
        }
    }
}
