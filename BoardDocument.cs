using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppDesigner;

public sealed class BoardDocument : INotifyPropertyChanged
{
    private string _title = "새 설계 보드";
    private string _boardKind = "blank";
    private string? _referenceImageBase64;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DesignElement> Elements { get; } = new();

    public string Title
    {
        get => _title;
        set => SetField(ref _title, string.IsNullOrWhiteSpace(value) ? "새 설계 보드" : value.Trim());
    }

    public string BoardKind
    {
        get => _boardKind;
        set => SetField(ref _boardKind, value);
    }

    public string? ReferenceImageBase64
    {
        get => _referenceImageBase64;
        set => SetField(ref _referenceImageBase64, value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
