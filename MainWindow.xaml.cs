using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AppDesigner;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double BoardWidth = 1600;
    private const double BoardHeight = 900;
    private const double SnapUnit = 24;
    private const double MinElementWidth = 120;
    private const double MinElementHeight = 90;
    private const double GuideTolerance = 10;

    private readonly ObservableCollection<DesignElement> _emptyElements = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<DesignElement, ElementBounds> _startBounds = new();

    private BoardDocument? _selectedBoard;
    private DesignElement? _selectedElement;
    private DesignElement? _activeElement;
    private FrameworkElement? _dragSource;
    private Point _interactionStartPoint;
    private InteractionMode _interactionMode = InteractionMode.None;
    private ResizeHandle _resizeHandle = ResizeHandle.None;
    private ImageSource? _currentReferenceImage;
    private string _statusText = "보드가 준비되었습니다.";
    private bool _showGrid = true;
    private bool _snapToGrid = true;
    private bool _showGuides = true;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        var board = CreateBoardTemplate("desktop");
        Boards.Add(board);
        SelectedBoard = board;
        UpdateBoardBackground();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BoardDocument> Boards { get; } = new();

    public BoardDocument? SelectedBoard
    {
        get => _selectedBoard;
        set
        {
            if (_selectedBoard == value)
            {
                return;
            }

            if (_selectedBoard is not null)
            {
                _selectedBoard.PropertyChanged -= SelectedBoard_OnPropertyChanged;
            }

            _selectedBoard = value;

            if (_selectedBoard is not null)
            {
                _selectedBoard.PropertyChanged += SelectedBoard_OnPropertyChanged;
            }

            HideGuideLines();
            _startBounds.Clear();
            _interactionMode = InteractionMode.None;
            _activeElement = null;
            SelectedElement = null;
            RefreshReferenceImage();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentElements));
            OnPropertyChanged(nameof(HasBoard));
            OnPropertyChanged(nameof(HasReferenceImage));
            UpdateBoardBackground();
            UpdateSelectionProperties();
        }
    }

    public ObservableCollection<DesignElement> CurrentElements => SelectedBoard?.Elements ?? _emptyElements;

    public bool HasBoard => SelectedBoard is not null;

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (SetField(ref _showGrid, value))
            {
                UpdateBoardBackground();
            }
        }
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => SetField(ref _snapToGrid, value);
    }

    public bool ShowGuides
    {
        get => _showGuides;
        set
        {
            if (SetField(ref _showGuides, value) && !value)
            {
                HideGuideLines();
            }
        }
    }

    public DesignElement? SelectedElement
    {
        get => _selectedElement;
        private set
        {
            if (_selectedElement == value)
            {
                return;
            }

            if (_selectedElement is not null)
            {
                _selectedElement.PropertyChanged -= SelectedElement_OnPropertyChanged;
            }

            _selectedElement = value;

            if (_selectedElement is not null)
            {
                _selectedElement.PropertyChanged += SelectedElement_OnPropertyChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(EditableElement));
            OnPropertyChanged(nameof(SelectedElementSummary));
        }
    }

    public DesignElement? EditableElement => SelectedCount == 1 ? SelectedElement : null;

    public bool HasSelection => SelectedCount > 0;

    public bool CanEditSingleElement => SelectedCount == 1;

    public int SelectedCount => CurrentElements.Count(item => item.IsSelected);

    public string SelectedElementSummary
    {
        get
        {
            return SelectedCount switch
            {
                0 => "선택된 칸 없음",
                1 when SelectedElement is not null => $"{SelectedElement.MarkerText} {SelectedElement.ElementType} · {SelectedElement.Title}",
                _ => $"{SelectedCount}개 칸 선택됨",
            };
        }
    }

    public ImageSource? CurrentReferenceImage => _currentReferenceImage;

    public bool HasReferenceImage => !string.IsNullOrWhiteSpace(SelectedBoard?.ReferenceImageBase64);

    private BoardDocument CreateBoardTemplate(string kind, string? title = null, string? referenceImageBase64 = null)
    {
        var board = new BoardDocument
        {
            Title = title ?? GetDefaultBoardTitle(kind),
            BoardKind = kind,
            ReferenceImageBase64 = referenceImageBase64,
        };

        switch (kind)
        {
            case "desktop":
                board.Elements.Add(BuildElement("quickbar", 60, 40, 280, 54, "빠른 실행줄", "저장, 실행취소, 다시실행 같은 자주 쓰는 작은 버튼 줄입니다."));
                board.Elements.Add(BuildElement("ribbontabs", 360, 40, 760, 58, "리본 탭줄", "파일, 홈, 삽입, 보기처럼 탭 제목이 가로로 놓이는 줄입니다."));
                board.Elements.Add(BuildElement("ribbonarea", 360, 110, 1120, 170, "리본 명령영역", "엑셀의 클립보드, 글꼴, 맞춤 같은 큰 명령 영역입니다."));
                board.Elements.Add(BuildElement("ribbongroup", 380, 138, 220, 118, "클립보드 그룹", "붙여넣기, 잘라내기, 복사처럼 하나의 그룹을 따로 설계합니다."));
                board.Elements.Add(BuildElement("ribbongroup", 620, 138, 230, 118, "글꼴 그룹", "글꼴, 크기, 굵게, 색상처럼 글자 관련 묶음입니다."));
                board.Elements.Add(BuildElement("ribbongroup", 870, 138, 240, 118, "맞춤 그룹", "가운데 맞춤, 줄바꿈, 병합처럼 정렬 관련 묶음입니다."));
                board.Elements.Add(BuildElement("section", 60, 310, 260, 520, "왼쪽 메뉴", "프로그램 메뉴와 상태 요약을 두는 세로 영역입니다."));
                board.Elements.Add(BuildElement("tabs", 360, 330, 820, 300, "주요 작업 화면", "탭으로 화면을 나누고 가운데 큰 작업 영역을 배치합니다."));
                board.Elements.Add(BuildElement("buttons", 360, 670, 520, 150, "하단 실행 버튼", "저장, 취소, 적용 버튼을 모아 두는 구역입니다."));
                board.Elements.Add(BuildElement("note", 1210, 330, 260, 180, "설계 메모", "주의사항이나 연결 구조를 적어 두는 보조 메모입니다."));
                break;
            case "landing":
                board.Elements.Add(BuildElement("hero", 70, 70, 1180, 260, "첫 화면 히어로", "대표 문구, 큰 버튼, 대표 이미지를 배치하는 핵심 영역입니다."));
                board.Elements.Add(BuildElement("section", 70, 370, 380, 220, "강점 소개", "서비스 장점을 카드 형식으로 보여 줍니다."));
                board.Elements.Add(BuildElement("section", 490, 370, 380, 220, "기능 소개", "주요 기능을 짧게 설명하는 카드 영역입니다."));
                board.Elements.Add(BuildElement("section", 910, 370, 380, 220, "후기/사례", "사용 후기나 적용 사례를 보여 주는 영역입니다."));
                board.Elements.Add(BuildElement("buttons", 430, 640, 500, 130, "마지막 호출 버튼", "문의하기, 시작하기 같은 최종 버튼 영역입니다."));
                break;
            case "mobile":
                board.Elements.Add(BuildElement("dialog", 500, 40, 320, 90, "상단 상태/제목", "앱 제목과 뒤로가기, 설정 버튼이 있는 상단 바입니다."));
                board.Elements.Add(BuildElement("form", 500, 170, 320, 220, "입력 카드", "로그인이나 설정 입력칸을 세로로 배치합니다."));
                board.Elements.Add(BuildElement("section", 500, 430, 320, 180, "콘텐츠 카드", "핵심 내용이나 카드 목록을 표시합니다."));
                board.Elements.Add(BuildElement("buttons", 500, 650, 320, 90, "하단 버튼", "확인, 다음, 저장 같은 버튼 줄입니다."));
                board.Elements.Add(BuildElement("tabs", 500, 770, 320, 70, "하단 탭", "홈, 검색, 설정 같은 하단 탭 구조입니다."));
                break;
            default:
                break;
        }

        AssignMarkerTexts(board.Elements);
        return board;
    }

    private string GetDefaultBoardTitle(string kind)
    {
        return kind switch
        {
            "desktop" => "데스크탑 앱 설계",
            "landing" => "렌딩 페이지 설계",
            "mobile" => "휴대폰 앱 설계",
            _ => "새 설계 보드",
        };
    }

    private DesignElement BuildElement(string kind, double left, double top, double width, double height, string title, string description)
    {
        var element = CreateStyledElement(kind);
        element.Left = left;
        element.Top = top;
        element.Width = width;
        element.Height = height;
        element.Title = title;
        element.Description = description;
        return element;
    }
    private DesignElement CreateStyledElement(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "dialog" => new DesignElement
            {
                KindKey = "dialog",
                ElementType = "대화창",
                PreviewText = "[제목]\n[내용][버튼]",
                Title = "새 대화창",
                Description = "설정창이나 확인창처럼 크게 띄우는 영역입니다.",
                Width = 380,
                Height = 210,
                FillBrush = CreateBrush("#FFF4F7FB"),
                AccentBrush = CreateBrush("#305F8C"),
                BorderBrush = CreateBrush("#B8D0E5"),
            },
            "popup" => new DesignElement
            {
                KindKey = "popup",
                ElementType = "작은 알림창",
                PreviewText = "[안내]\n[확인]",
                Title = "새 알림창",
                Description = "도움말, 알림, 경고처럼 짧게 띄우는 작은 창입니다.",
                Width = 300,
                Height = 170,
                FillBrush = CreateBrush("#FFFFF5E8"),
                AccentBrush = CreateBrush("#D27A2C"),
                BorderBrush = CreateBrush("#F0C78F"),
            },
            "buttons" => new DesignElement
            {
                KindKey = "buttons",
                ElementType = "버튼 묶음",
                PreviewText = "[저장][취소]",
                Title = "버튼 영역",
                Description = "확인, 취소, 삭제, 실행 버튼을 묶어 놓는 영역입니다.",
                Width = 360,
                Height = 140,
                FillBrush = CreateBrush("#FFF1F7EF"),
                AccentBrush = CreateBrush("#2E7D5B"),
                BorderBrush = CreateBrush("#B7DDCD"),
            },
            "tabs" => new DesignElement
            {
                KindKey = "tabs",
                ElementType = "탭 화면",
                PreviewText = "[탭][탭]\n[내용]",
                Title = "탭 구조",
                Description = "탭을 눌러 여러 화면 묶음을 전환하는 구조입니다.",
                Width = 460,
                Height = 220,
                FillBrush = CreateBrush("#FFF9F4EE"),
                AccentBrush = CreateBrush("#A14D3D"),
                BorderBrush = CreateBrush("#E5B8AF"),
            },
            "ribbontabs" => new DesignElement
            {
                KindKey = "ribbontabs",
                ElementType = "리본 탭줄",
                PreviewText = "[파일][홈][삽입]",
                Title = "리본 탭줄",
                Description = "엑셀처럼 상단에서 탭 이름이 가로로 나열되는 줄입니다.",
                Width = 760,
                Height = 58,
                FillBrush = CreateBrush("#FFF8F4EF"),
                AccentBrush = CreateBrush("#7D6450"),
                BorderBrush = CreateBrush("#D9C9B4"),
            },
            "ribbonarea" => new DesignElement
            {
                KindKey = "ribbonarea",
                ElementType = "리본 명령영역",
                PreviewText = "[클립보드][글꼴][맞춤]",
                Title = "리본 명령영역",
                Description = "탭 아래에 붙어 있는 큰 명령 버튼 모음 영역입니다.",
                Width = 980,
                Height = 170,
                FillBrush = CreateBrush("#FFF7F7F4"),
                AccentBrush = CreateBrush("#5F7A9A"),
                BorderBrush = CreateBrush("#CCD6E1"),
            },
            "ribbongroup" => new DesignElement
            {
                KindKey = "ribbongroup",
                ElementType = "리본 그룹",
                PreviewText = "[붙여넣기][복사]",
                Title = "리본 그룹",
                Description = "리본 안에서 클립보드, 글꼴, 맞춤처럼 한 묶음씩 나누는 그룹입니다.",
                Width = 220,
                Height = 118,
                FillBrush = CreateBrush("#FFFDFBF8"),
                AccentBrush = CreateBrush("#3E6C87"),
                BorderBrush = CreateBrush("#D8E1E8"),
            },
            "quickbar" => new DesignElement
            {
                KindKey = "quickbar",
                ElementType = "빠른 실행줄",
                PreviewText = "[저장][실행취소]",
                Title = "빠른 실행줄",
                Description = "창 맨 위에서 자주 쓰는 아이콘 버튼들을 모아두는 작은 줄입니다.",
                Width = 280,
                Height = 54,
                FillBrush = CreateBrush("#FFF4F8FB"),
                AccentBrush = CreateBrush("#3A5F7E"),
                BorderBrush = CreateBrush("#C9D7E4"),
            },
            "form" => new DesignElement
            {
                KindKey = "form",
                ElementType = "입력 폼",
                PreviewText = "[라벨]\n[입력칸]",
                Title = "입력 폼",
                Description = "레이블과 입력칸, 체크박스를 정리하는 입력 영역입니다.",
                Width = 380,
                Height = 230,
                FillBrush = CreateBrush("#FFF0F7F6"),
                AccentBrush = CreateBrush("#2C6F78"),
                BorderBrush = CreateBrush("#AFD7D8"),
            },
            "section" => new DesignElement
            {
                KindKey = "section",
                ElementType = "화면 구역",
                PreviewText = "[제목]\n[카드][목록]",
                Title = "콘텐츠 구역",
                Description = "목록, 카드, 상태 패널 같은 큰 화면 구역을 설명합니다.",
                Width = 420,
                Height = 220,
                FillBrush = CreateBrush("#FFF6F5F1"),
                AccentBrush = CreateBrush("#56606C"),
                BorderBrush = CreateBrush("#CDD2D8"),
            },
            "hero" => new DesignElement
            {
                KindKey = "hero",
                ElementType = "랜딩 첫 화면",
                PreviewText = "[문구]\n[버튼][이미지]",
                Title = "랜딩 첫 화면",
                Description = "서비스 문구와 대표 이미지를 크게 보여 주는 첫 화면 영역입니다.",
                Width = 500,
                Height = 250,
                FillBrush = CreateBrush("#FFF9F0EF"),
                AccentBrush = CreateBrush("#B44D3E"),
                BorderBrush = CreateBrush("#E7B8AE"),
            },
            _ => new DesignElement
            {
                KindKey = "note",
                ElementType = "메모",
                PreviewText = "[지시]\n[메모]",
                Title = "메모",
                Description = "구현 지시사항이나 주의사항을 적는 메모입니다.",
                Width = 280,
                Height = 160,
                FillBrush = CreateBrush("#FFFFF8DB"),
                AccentBrush = CreateBrush("#8E6B13"),
                BorderBrush = CreateBrush("#E7D37F"),
            },
        };
    }

    private DesignElement CreateElementForCurrentBoard(string kind)
    {
        var element = CreateStyledElement(kind);
        var offset = CurrentElements.Count * 28;
        element.Left = 80 + (offset % 360);
        element.Top = 80 + (offset % 220);
        element.ZIndex = CurrentElements.Count == 0 ? 1 : CurrentElements.Max(item => item.ZIndex) + 1;
        return element;
    }

    private void CreateBoardTemplate_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string kind)
        {
            return;
        }

        var board = CreateBoardTemplate(kind.ToLowerInvariant());
        Boards.Add(board);
        SelectedBoard = board;
        StatusText = $"{board.Title} 탭을 만들었습니다.";
    }

    private void AddBoardTab_OnClick(object sender, RoutedEventArgs e)
    {
        var board = CreateBoardTemplate("blank", $"새 탭 {Boards.Count + 1}");
        Boards.Add(board);
        SelectedBoard = board;
        StatusText = "새 탭을 추가했습니다.";
    }

    private void AddElementMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedBoard is null || sender is not FrameworkElement element || element.Tag is not string kind)
        {
            return;
        }

        var designElement = CreateElementForCurrentBoard(kind);
        SelectedBoard.Elements.Add(designElement);
        AssignMarkerTexts(SelectedBoard.Elements);
        SelectOnly(designElement, $"{designElement.MarkerText} {designElement.ElementType}을(를) 추가했습니다.");
    }

    private void BoardSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        SelectOnly(null, "선택을 해제했습니다.");
    }

    private void DesignItem_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not DesignElement item || SelectedBoard is null)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ToggleSelection(item);
            e.Handled = true;
            return;
        }

        if (!item.IsSelected)
        {
            SelectOnly(item, $"{item.MarkerText} 선택됨");
        }
        else
        {
            SelectedElement = item;
            UpdateSelectionProperties($"{item.MarkerText} 선택됨");
        }

        BeginInteraction(InteractionMode.Move, item, element);
        e.Handled = true;
    }

    private void DesignItem_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_interactionMode != InteractionMode.Move || _activeElement is null || _dragSource is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(BoardSurface);
        var deltaX = currentPoint.X - _interactionStartPoint.X;
        var deltaY = currentPoint.Y - _interactionStartPoint.Y;
        (deltaX, deltaY) = ClampMoveDelta(deltaX, deltaY);
        (deltaX, deltaY) = SnapMoveDelta(deltaX, deltaY);

        var guide = ComputeGuideSnap(GetMovedBounds(_activeElement, deltaX, deltaY));
        deltaX += guide.OffsetX;
        deltaY += guide.OffsetY;
        (deltaX, deltaY) = ClampMoveDelta(deltaX, deltaY);

        ApplyMove(deltaX, deltaY);
        ShowGuideLines(guide.VerticalGuideX, guide.HorizontalGuideY);
        StatusText = $"{SelectedCount}개 칸 이동 중";
    }

    private void DesignItem_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_interactionMode == InteractionMode.Move)
        {
            FinishInteraction($"{SelectedCount}개 칸 위치를 업데이트했습니다.");
            e.Handled = true;
        }
    }

    private void ResizeThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not DesignElement item || element.Tag is not string handleText)
        {
            return;
        }

        if (!item.IsSelected)
        {
            SelectOnly(item, $"{item.MarkerText} 선택됨");
        }
        else
        {
            SelectedElement = item;
            UpdateSelectionProperties($"{item.MarkerText} 크기 조절 준비");
        }

        _resizeHandle = ParseResizeHandle(handleText);
        BeginInteraction(InteractionMode.Resize, item);
    }
    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_interactionMode != InteractionMode.Resize || _activeElement is null)
        {
            return;
        }

        var currentPoint = Mouse.GetPosition(BoardSurface);
        var deltaX = currentPoint.X - _interactionStartPoint.X;
        var deltaY = currentPoint.Y - _interactionStartPoint.Y;
        (deltaX, deltaY) = ClampResizeDelta(deltaX, deltaY, _resizeHandle);
        (deltaX, deltaY) = SnapResizeDelta(deltaX, deltaY, _resizeHandle);

        var guide = ComputeGuideSnap(BuildResizedBounds(_startBounds[_activeElement], deltaX, deltaY, _resizeHandle));
        deltaX += guide.OffsetX;
        deltaY += guide.OffsetY;
        (deltaX, deltaY) = ClampResizeDelta(deltaX, deltaY, _resizeHandle);

        ApplyResize(deltaX, deltaY, _resizeHandle);
        ShowGuideLines(guide.VerticalGuideX, guide.HorizontalGuideY);
        StatusText = $"{SelectedCount}개 칸 크기 조절 중";
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_interactionMode == InteractionMode.Resize)
        {
            FinishInteraction($"{SelectedCount}개 칸 크기를 업데이트했습니다.");
        }
    }

    private void DuplicateSelected_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedBoard is null || SelectedCount == 0)
        {
            StatusText = "복제할 항목을 먼저 선택하세요.";
            return;
        }

        var clones = CurrentElements.Where(item => item.IsSelected).OrderBy(item => item.ZIndex).Select(item => item.CloneWithOffset(32)).ToList();
        var zIndex = CurrentElements.Count == 0 ? 1 : CurrentElements.Max(item => item.ZIndex) + 1;
        foreach (var clone in clones)
        {
            clone.ZIndex = zIndex++;
            SelectedBoard.Elements.Add(clone);
        }

        AssignMarkerTexts(SelectedBoard.Elements);
        foreach (var item in CurrentElements)
        {
            item.IsSelected = false;
        }
        foreach (var clone in clones)
        {
            clone.IsSelected = true;
        }

        SelectedElement = clones.LastOrDefault();
        UpdateSelectionProperties($"{clones.Count}개 항목을 복제했습니다.");
    }

    private void DeleteSelected_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedBoard is null || SelectedCount == 0)
        {
            StatusText = "삭제할 항목을 먼저 선택하세요.";
            return;
        }

        var targets = CurrentElements.Where(item => item.IsSelected).ToList();
        foreach (var item in targets)
        {
            SelectedBoard.Elements.Remove(item);
        }

        AssignMarkerTexts(SelectedBoard.Elements);
        SelectOnly(null, $"{targets.Count}개 항목을 삭제했습니다.");
    }

    private void BringToFront_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCount == 0)
        {
            StatusText = "정렬할 항목을 먼저 선택하세요.";
            return;
        }

        var zIndex = CurrentElements.Max(item => item.ZIndex) + 1;
        foreach (var item in CurrentElements.Where(item => item.IsSelected).OrderBy(item => item.ZIndex))
        {
            item.ZIndex = zIndex++;
        }

        StatusText = "선택 항목을 앞으로 보냈습니다.";
    }

    private void SendToBack_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCount == 0)
        {
            StatusText = "정렬할 항목을 먼저 선택하세요.";
            return;
        }

        var selected = CurrentElements.Where(item => item.IsSelected).OrderBy(item => item.ZIndex).ToList();
        var zIndex = CurrentElements.Min(item => item.ZIndex) - selected.Count;
        foreach (var item in selected)
        {
            item.ZIndex = zIndex++;
        }

        StatusText = "선택 항목을 뒤로 보냈습니다.";
    }

    private void BeginInteraction(InteractionMode mode, DesignElement item, FrameworkElement? source = null)
    {
        _interactionMode = mode;
        _activeElement = item;
        _dragSource = source;
        _interactionStartPoint = Mouse.GetPosition(BoardSurface);
        _startBounds.Clear();

        var selectedItems = CurrentElements.Where(element => element.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            selectedItems.Add(item);
        }

        foreach (var selectedItem in selectedItems)
        {
            _startBounds[selectedItem] = new ElementBounds(selectedItem.Left, selectedItem.Top, selectedItem.Width, selectedItem.Height);
        }

        if (source is not null)
        {
            Mouse.Capture(source);
        }
    }

    private void FinishInteraction(string statusMessage)
    {
        if (_dragSource is not null && ReferenceEquals(Mouse.Captured, _dragSource))
        {
            Mouse.Capture(null);
        }

        _dragSource = null;
        _activeElement = null;
        _interactionMode = InteractionMode.None;
        _resizeHandle = ResizeHandle.None;
        _startBounds.Clear();
        HideGuideLines();
        StatusText = statusMessage;
    }

    private void SelectOnly(DesignElement? element, string? statusMessage = null)
    {
        foreach (var item in CurrentElements)
        {
            item.IsSelected = ReferenceEquals(item, element);
        }

        SelectedElement = element;
        UpdateSelectionProperties(statusMessage);
    }

    private void ToggleSelection(DesignElement item)
    {
        item.IsSelected = !item.IsSelected;
        if (item.IsSelected)
        {
            SelectedElement = item;
        }

        UpdateSelectionProperties(item.IsSelected ? $"{SelectedCount}개 항목 선택" : $"{SelectedCount}개 항목 남음");
    }

    private void UpdateSelectionProperties(string? statusMessage = null)
    {
        if (SelectedElement is not null && !SelectedElement.IsSelected)
        {
            SelectedElement = CurrentElements.LastOrDefault(item => item.IsSelected);
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEditSingleElement));
        OnPropertyChanged(nameof(EditableElement));
        OnPropertyChanged(nameof(SelectedElementSummary));

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            StatusText = statusMessage;
        }
    }

    private void AssignMarkerTexts(IEnumerable<DesignElement> elements)
    {
        var index = 1;
        foreach (var element in elements)
        {
            element.MarkerText = CreateMarkerText(index++);
        }
    }

    private static string CreateMarkerText(int index)
    {
        string[] markers = ["①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩", "⑪", "⑫", "⑬", "⑭", "⑮", "⑯", "⑰", "⑱", "⑲", "⑳"];
        return index <= markers.Length ? markers[index - 1] : $"#{index}";
    }

    private (double DeltaX, double DeltaY) ClampMoveDelta(double deltaX, double deltaY)
    {
        if (_startBounds.Count == 0)
        {
            return (0, 0);
        }

        var minLeft = _startBounds.Values.Min(bounds => bounds.Left);
        var minTop = _startBounds.Values.Min(bounds => bounds.Top);
        var maxRight = _startBounds.Values.Max(bounds => bounds.Right);
        var maxBottom = _startBounds.Values.Max(bounds => bounds.Bottom);

        deltaX = Clamp(deltaX, -minLeft, BoardWidth - maxRight);
        deltaY = Clamp(deltaY, -minTop, BoardHeight - maxBottom);
        return (deltaX, deltaY);
    }

    private (double DeltaX, double DeltaY) SnapMoveDelta(double deltaX, double deltaY)
    {
        if (!SnapToGrid || _activeElement is null || !_startBounds.TryGetValue(_activeElement, out var bounds))
        {
            return (deltaX, deltaY);
        }

        var snappedLeft = Math.Round((bounds.Left + deltaX) / SnapUnit) * SnapUnit;
        var snappedTop = Math.Round((bounds.Top + deltaY) / SnapUnit) * SnapUnit;
        deltaX += snappedLeft - (bounds.Left + deltaX);
        deltaY += snappedTop - (bounds.Top + deltaY);
        return ClampMoveDelta(deltaX, deltaY);
    }

    private void ApplyMove(double deltaX, double deltaY)
    {
        foreach (var pair in _startBounds)
        {
            pair.Key.Left = pair.Value.Left + deltaX;
            pair.Key.Top = pair.Value.Top + deltaY;
        }
    }
    private (double DeltaX, double DeltaY) ClampResizeDelta(double deltaX, double deltaY, ResizeHandle handle)
    {
        double minX = double.NegativeInfinity;
        double maxX = double.PositiveInfinity;
        double minY = double.NegativeInfinity;
        double maxY = double.PositiveInfinity;

        foreach (var bounds in _startBounds.Values)
        {
            if (HasLeft(handle))
            {
                minX = Math.Max(minX, -bounds.Left);
                maxX = Math.Min(maxX, bounds.Width - MinElementWidth);
            }
            else if (HasRight(handle))
            {
                minX = Math.Max(minX, MinElementWidth - bounds.Width);
                maxX = Math.Min(maxX, BoardWidth - bounds.Right);
            }

            if (HasTop(handle))
            {
                minY = Math.Max(minY, -bounds.Top);
                maxY = Math.Min(maxY, bounds.Height - MinElementHeight);
            }
            else if (HasBottom(handle))
            {
                minY = Math.Max(minY, MinElementHeight - bounds.Height);
                maxY = Math.Min(maxY, BoardHeight - bounds.Bottom);
            }
        }

        deltaX = Clamp(deltaX, minX, maxX);
        deltaY = Clamp(deltaY, minY, maxY);
        return (deltaX, deltaY);
    }

    private (double DeltaX, double DeltaY) SnapResizeDelta(double deltaX, double deltaY, ResizeHandle handle)
    {
        if (!SnapToGrid || _activeElement is null || !_startBounds.TryGetValue(_activeElement, out var bounds))
        {
            return (deltaX, deltaY);
        }

        if (HasLeft(handle))
        {
            var snapped = Math.Round((bounds.Left + deltaX) / SnapUnit) * SnapUnit;
            deltaX += snapped - (bounds.Left + deltaX);
        }
        else if (HasRight(handle))
        {
            var snapped = Math.Round((bounds.Right + deltaX) / SnapUnit) * SnapUnit;
            deltaX += snapped - (bounds.Right + deltaX);
        }

        if (HasTop(handle))
        {
            var snapped = Math.Round((bounds.Top + deltaY) / SnapUnit) * SnapUnit;
            deltaY += snapped - (bounds.Top + deltaY);
        }
        else if (HasBottom(handle))
        {
            var snapped = Math.Round((bounds.Bottom + deltaY) / SnapUnit) * SnapUnit;
            deltaY += snapped - (bounds.Bottom + deltaY);
        }

        return ClampResizeDelta(deltaX, deltaY, handle);
    }

    private void ApplyResize(double deltaX, double deltaY, ResizeHandle handle)
    {
        foreach (var pair in _startBounds)
        {
            var resized = BuildResizedBounds(pair.Value, deltaX, deltaY, handle);
            pair.Key.Left = resized.Left;
            pair.Key.Top = resized.Top;
            pair.Key.Width = resized.Width;
            pair.Key.Height = resized.Height;
        }
    }

    private static ElementBounds BuildResizedBounds(ElementBounds bounds, double deltaX, double deltaY, ResizeHandle handle)
    {
        var left = bounds.Left;
        var top = bounds.Top;
        var width = bounds.Width;
        var height = bounds.Height;

        if (HasLeft(handle))
        {
            left += deltaX;
            width -= deltaX;
        }
        else if (HasRight(handle))
        {
            width += deltaX;
        }

        if (HasTop(handle))
        {
            top += deltaY;
            height -= deltaY;
        }
        else if (HasBottom(handle))
        {
            height += deltaY;
        }

        return new ElementBounds(left, top, width, height);
    }

    private ElementBounds GetMovedBounds(DesignElement element, double deltaX, double deltaY)
    {
        var bounds = _startBounds[element];
        return new ElementBounds(bounds.Left + deltaX, bounds.Top + deltaY, bounds.Width, bounds.Height);
    }

    private GuideResult ComputeGuideSnap(ElementBounds candidate)
    {
        if (!ShowGuides)
        {
            return GuideResult.Empty;
        }

        var ignore = _startBounds.Keys.ToHashSet();
        var xTargets = new List<double> { BoardWidth / 2 };
        var yTargets = new List<double> { BoardHeight / 2 };

        foreach (var item in CurrentElements)
        {
            if (ignore.Contains(item))
            {
                continue;
            }

            xTargets.Add(item.Left);
            xTargets.Add(item.Left + (item.Width / 2));
            xTargets.Add(item.Left + item.Width);
            yTargets.Add(item.Top);
            yTargets.Add(item.Top + (item.Height / 2));
            yTargets.Add(item.Top + item.Height);
        }

        var (offsetX, guideX) = FindBestGuide(candidate.Left, candidate.CenterX, candidate.Right, xTargets);
        var (offsetY, guideY) = FindBestGuide(candidate.Top, candidate.CenterY, candidate.Bottom, yTargets);
        return new GuideResult(offsetX, offsetY, guideX, guideY);
    }

    private static (double Offset, double? Guide) FindBestGuide(double start, double middle, double end, IEnumerable<double> targets)
    {
        var bestDistance = GuideTolerance + 1;
        var bestOffset = 0d;
        double? guide = null;

        foreach (var target in targets)
        {
            foreach (var candidate in new[] { target - start, target - middle, target - end })
            {
                var distance = Math.Abs(candidate);
                if (distance <= GuideTolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestOffset = candidate;
                    guide = target;
                }
            }
        }

        return (guide.HasValue ? bestOffset : 0d, guide);
    }

    private void ShowGuideLines(double? vertical, double? horizontal)
    {
        if (!ShowGuides)
        {
            HideGuideLines();
            return;
        }

        if (vertical is double x)
        {
            VerticalGuideLine.X1 = x;
            VerticalGuideLine.X2 = x;
            VerticalGuideLine.Y1 = 0;
            VerticalGuideLine.Y2 = BoardHeight;
            VerticalGuideLine.Visibility = Visibility.Visible;
        }
        else
        {
            VerticalGuideLine.Visibility = Visibility.Collapsed;
        }

        if (horizontal is double y)
        {
            HorizontalGuideLine.X1 = 0;
            HorizontalGuideLine.X2 = BoardWidth;
            HorizontalGuideLine.Y1 = y;
            HorizontalGuideLine.Y2 = y;
            HorizontalGuideLine.Visibility = Visibility.Visible;
        }
        else
        {
            HorizontalGuideLine.Visibility = Visibility.Collapsed;
        }
    }

    private void HideGuideLines()
    {
        VerticalGuideLine.Visibility = Visibility.Collapsed;
        HorizontalGuideLine.Visibility = Visibility.Collapsed;
    }

    private void SaveWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "AppDesigner Workspace|*.appdesigner.json|JSON|*.json", FileName = "appdesigner-workspace.appdesigner.json" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var file = new WorkspaceFile
        {
            SelectedIndex = SelectedBoard is null ? 0 : Boards.IndexOf(SelectedBoard),
            Boards = Boards.Select(board => new BoardFile
            {
                Title = board.Title,
                BoardKind = board.BoardKind,
                ReferenceImageBase64 = board.ReferenceImageBase64,
                Elements = board.Elements.Select(item => new ElementFile
                {
                    Kind = item.KindKey,
                    Title = item.Title,
                    Description = item.Description,
                    Left = item.Left,
                    Top = item.Top,
                    Width = item.Width,
                    Height = item.Height,
                    ZIndex = item.ZIndex,
                }).ToList(),
            }).ToList(),
        };

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(file, _jsonOptions), Encoding.UTF8);
        StatusText = $"작업 파일 저장 완료: {dialog.FileName}";
    }

    private void LoadWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "AppDesigner Workspace|*.appdesigner.json;*.json|JSON|*.json" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var file = JsonSerializer.Deserialize<WorkspaceFile>(File.ReadAllText(dialog.FileName), _jsonOptions);
        if (file is null || file.Boards.Count == 0)
        {
            StatusText = "불러올 수 있는 보드가 없습니다.";
            return;
        }

        Boards.Clear();
        foreach (var boardFile in file.Boards)
        {
            var board = new BoardDocument { Title = boardFile.Title, BoardKind = boardFile.BoardKind, ReferenceImageBase64 = boardFile.ReferenceImageBase64 };
            foreach (var elementFile in boardFile.Elements)
            {
                var item = CreateStyledElement(elementFile.Kind);
                item.Title = elementFile.Title;
                item.Description = elementFile.Description;
                item.Left = elementFile.Left;
                item.Top = elementFile.Top;
                item.Width = elementFile.Width;
                item.Height = elementFile.Height;
                item.ZIndex = elementFile.ZIndex;
                board.Elements.Add(item);
            }
            AssignMarkerTexts(board.Elements);
            Boards.Add(board);
        }

        SelectedBoard = Boards[Math.Clamp(file.SelectedIndex, 0, Boards.Count - 1)];
        StatusText = $"작업 파일을 불러왔습니다: {dialog.FileName}";
    }

    private void PasteReferenceImage_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedBoard is null)
        {
            return;
        }

        if (!Clipboard.ContainsImage())
        {
            StatusText = "클립보드에 이미지가 없습니다.";
            return;
        }

        var clipboardImage = Clipboard.GetImage(); if (clipboardImage is null) { StatusText = "클립보드 이미지 읽기에 실패했습니다."; return; } SelectedBoard.ReferenceImageBase64 = EncodeImageToBase64(clipboardImage);
        RefreshReferenceImage();
        StatusText = "참고 이미지를 현재 탭에 붙여 넣었습니다.";
    }

    private void GenerateDraftFromClipboard_OnClick(object sender, RoutedEventArgs e)
    {
        BitmapSource? image = null;
        if (Clipboard.ContainsImage())
        {
            image = Clipboard.GetImage();
        }
        else if (SelectedBoard?.ReferenceImageBase64 is { Length: > 0 } base64)
        {
            image = DecodeBase64ToImage(base64);
        }

        if (image is null)
        {
            StatusText = "클립보드나 현재 탭에 참고 이미지가 없습니다.";
            return;
        }

        var kind = image.PixelHeight > image.PixelWidth * 1.2 ? "mobile" : image.PixelWidth > image.PixelHeight * 1.4 ? "landing" : "desktop";
        var board = CreateBoardTemplate(kind, "참고 이미지 기초 도안", EncodeImageToBase64(image));
        Boards.Add(board);
        SelectedBoard = board;
        StatusText = "이미지 비율을 기준으로 기초 도안을 새 탭에 만들었습니다.";
    }

    private void CopyDesignText_OnClick(object sender, RoutedEventArgs e)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"보드 제목: {SelectedBoard?.Title}");
        builder.AppendLine("캔버스 크기: 1600 x 900");
        builder.AppendLine("설계 요소:");
        foreach (var item in CurrentElements.OrderBy(item => item.Top).ThenBy(item => item.Left))
        {
            builder.AppendLine($"- {item.MarkerText} {item.ElementType} / {item.Title}");
            builder.AppendLine($"  설명: {item.Description}");
            builder.AppendLine($"  위치: ({item.Left:0}, {item.Top:0})  크기: {item.Width:0} x {item.Height:0}");
        }
        Clipboard.SetText(builder.ToString());
        StatusText = "설계 설명 텍스트를 클립보드에 복사했습니다.";
    }

    private void CopyBoardImage_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetImage(RenderBoardBitmap());
        StatusText = "보드 이미지를 클립보드에 복사했습니다.";
    }

    private void SaveBoardPng_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = $"{MakeSafeFileName(SelectedBoard?.Title ?? "board")}.png", AddExtension = true, DefaultExt = ".png" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(RenderBoardBitmap()));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
        StatusText = $"PNG 저장 완료: {dialog.FileName}";
    }

    private RenderTargetBitmap RenderBoardBitmap()
    {
        BoardSurface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(BoardSurface.ActualWidth), (int)Math.Ceiling(BoardSurface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(BoardSurface);
        return bitmap;
    }

    private void SelectedBoard_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoardDocument.ReferenceImageBase64))
        {
            RefreshReferenceImage();
        }
    }

    private void SelectedElement_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedElementSummary));
    }

    private void RefreshReferenceImage()
    {
        _currentReferenceImage = string.IsNullOrWhiteSpace(SelectedBoard?.ReferenceImageBase64) ? null : DecodeBase64ToImage(SelectedBoard.ReferenceImageBase64!);
        OnPropertyChanged(nameof(CurrentReferenceImage));
        OnPropertyChanged(nameof(HasReferenceImage));
    }

    private void UpdateBoardBackground()
    {
        if (BoardBackground is null)
        {
            return;
        }

        BoardBackground.Background = ShowGrid ? (Brush)Resources["BoardGridBrush"] : (Brush)Resources["PlainBoardBrush"];
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e) => Close();

    private void About_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "앱디자이너\n\n새로 만들기 템플릿, 탭형 보드, 참고 이미지 붙여넣기, 저장/불러오기, 다중 선택 이동/크기조절을 지원합니다.", "앱디자이너", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static ResizeHandle ParseResizeHandle(string text) => Enum.Parse<ResizeHandle>(text);
    private static bool HasLeft(ResizeHandle handle) => handle is ResizeHandle.TopLeft or ResizeHandle.BottomLeft;
    private static bool HasRight(ResizeHandle handle) => handle is ResizeHandle.TopRight or ResizeHandle.BottomRight;
    private static bool HasTop(ResizeHandle handle) => handle is ResizeHandle.TopLeft or ResizeHandle.TopRight;
    private static bool HasBottom(ResizeHandle handle) => handle is ResizeHandle.BottomLeft or ResizeHandle.BottomRight;
    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    private static SolidColorBrush CreateBrush(string hex) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze(); return brush; }
    private static string MakeSafeFileName(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    private static string EncodeImageToBase64(BitmapSource bitmap) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); return Convert.ToBase64String(stream.ToArray()); }
    private static BitmapImage? DecodeBase64ToImage(string base64) { var bytes = Convert.FromBase64String(base64); using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = new MemoryStream(bytes); image.EndInit(); image.Freeze(); return image; }
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) { if (Equals(field, value)) return false; field = value; OnPropertyChanged(propertyName); return true; }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private enum InteractionMode { None, Move, Resize }
    private enum ResizeHandle { None, TopLeft, TopRight, BottomLeft, BottomRight }
    private readonly record struct ElementBounds(double Left, double Top, double Width, double Height) { public double Right => Left + Width; public double Bottom => Top + Height; public double CenterX => Left + (Width / 2); public double CenterY => Top + (Height / 2); }
    private readonly record struct GuideResult(double OffsetX, double OffsetY, double? VerticalGuideX, double? HorizontalGuideY) { public static GuideResult Empty => new(0, 0, null, null); }
    private sealed class WorkspaceFile { public List<BoardFile> Boards { get; set; } = []; public int SelectedIndex { get; set; } }
    private sealed class BoardFile { public string Title { get; set; } = string.Empty; public string BoardKind { get; set; } = "blank"; public string? ReferenceImageBase64 { get; set; } public List<ElementFile> Elements { get; set; } = []; }
    private sealed class ElementFile { public string Kind { get; set; } = "note"; public string Title { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public double Left { get; set; } public double Top { get; set; } public double Width { get; set; } public double Height { get; set; } public int ZIndex { get; set; } }
}



