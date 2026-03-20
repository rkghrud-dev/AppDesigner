using System.Collections.ObjectModel;

namespace AppDesigner;

public class UiComponentCategory
{
    public string Name { get; set; } = string.Empty;
    public ObservableCollection<UiComponentItem> Items { get; set; } = new();
}
