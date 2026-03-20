using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AppDesigner;

public sealed class DesignElement : INotifyPropertyChanged
{
    private string _kindKey = "note";
    private string _elementType = string.Empty;
    private string _markerText = string.Empty;
    private string _previewText = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private double _left;
    private double _top;
    private double _width = 260;
    private double _height = 160;
    private int _zIndex;
    private bool _isSelected;
    private Brush _fillBrush = Brushes.WhiteSmoke;
    private Brush _accentBrush = Brushes.SteelBlue;
    private Brush _borderBrush = Brushes.LightSteelBlue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string KindKey
    {
        get => _kindKey;
        set => SetField(ref _kindKey, value);
    }

    public string ElementType
    {
        get => _elementType;
        set => SetField(ref _elementType, value);
    }

    public string MarkerText
    {
        get => _markerText;
        set => SetField(ref _markerText, value);
    }

    public string PreviewText
    {
        get => _previewText;
        set => SetField(ref _previewText, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public double Left
    {
        get => _left;
        set => SetField(ref _left, Math.Max(0, value));
    }

    public double Top
    {
        get => _top;
        set => SetField(ref _top, Math.Max(0, value));
    }

    public double Width
    {
        get => _width;
        set => SetField(ref _width, Math.Max(120, value));
    }

    public double Height
    {
        get => _height;
        set => SetField(ref _height, Math.Max(90, value));
    }

    public int ZIndex
    {
        get => _zIndex;
        set => SetField(ref _zIndex, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public Brush FillBrush
    {
        get => _fillBrush;
        set => SetField(ref _fillBrush, value);
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        set => SetField(ref _accentBrush, value);
    }

    public Brush BorderBrush
    {
        get => _borderBrush;
        set => SetField(ref _borderBrush, value);
    }

    public string PositionText => $"X {Left:0} / Y {Top:0}";

    public string SizeText => $"{Width:0} x {Height:0}";

    public DesignElement CloneWithOffset(double offset)
    {
        return new DesignElement
        {
            KindKey = KindKey,
            ElementType = ElementType,
            MarkerText = MarkerText,
            PreviewText = PreviewText,
            Title = $"{Title} 복사본",
            Description = Description,
            Left = Left + offset,
            Top = Top + offset,
            Width = Width,
            Height = Height,
            ZIndex = ZIndex + 1,
            FillBrush = FillBrush,
            AccentBrush = AccentBrush,
            BorderBrush = BorderBrush,
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);

        if (propertyName is nameof(Left) or nameof(Top))
        {
            OnPropertyChanged(nameof(PositionText));
        }

        if (propertyName is nameof(Width) or nameof(Height))
        {
            OnPropertyChanged(nameof(SizeText));
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
