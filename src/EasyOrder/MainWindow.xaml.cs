using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyOrder.Models;
using EasyOrder.Services;
using MenuItem = EasyOrder.Models.MenuItem;

namespace EasyOrder;

/// <summary>
/// 키오스크 메인 화면. QR 화면 → 시작 화면 → 포장/매장 선택 화면 → 메뉴 화면 흐름을 관리한다.
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxVoiceFailures = 3;
    private const int CompleteScreenSeconds = 30;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>담기 전에 반드시 골라야 하는 옵션 그룹 (온도·사이즈, FR-15)</summary>
    private static readonly string[] RequiredOptionGroups = ["temp", "size"];

    private readonly MenuCatalog _menu;
    private readonly OrderParser _orderParser;
    private readonly SpeechSynthesisService _speech = new();

    /// <summary>바로 전에 음성으로 담은 메뉴 묶음 ("취소" 대상, FR-17)</summary>
    private Guid? _lastVoiceBatchId;

    /// <summary>현재 질문 단계에서 음성 인식에 실패한 횟수 (FR-09)</summary>
    private int _voiceFailCount;

    /// <summary>주문 목록 (FR-08, FR-12)</summary>
    private readonly ObservableCollection<OrderItem> _orderItems = [];

    // 옵션 팝업 상태
    private MenuCategory? _popupCategory;
    private MenuItem? _popupMenu;
    private readonly Dictionary<string, OptionChoice> _popupSelections = [];
    private int _popupQuantity = 1;

    // 확인 알림·키패드·완료 화면
    private Action? _confirmAction;
    private Action<int>? _keypadCallback;
    private string _keypadInput = "";
    private readonly DispatcherTimer _completeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _completeRemainingSeconds;

    // 무응답 타임아웃 (FR-10): 세션 중 마지막 터치·말 이후 60초
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    /// <summary>현재 화면 단계</summary>
    public KioskScreen CurrentScreen { get; private set; }

    /// <summary>현재 세션의 식사 방식 (FR-04). 세션 시작 전·초기화 후에는 null</summary>
    public DineMode? CurrentDineMode { get; private set; }

    /// <summary>휴대폰 "음성주문활성화"로 시작한 음성 주문 세션인지 여부 (FR-14)</summary>
    public bool IsVoiceSession { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        _menu = MenuRepository.Load();
        _orderParser = new OrderParser(_menu);
        CategoryList.ItemsSource = _menu.Categories;

        OrderItemsList.ItemsSource = _orderItems;
        _orderItems.CollectionChanged += OnOrderItemsChanged;
        BuildKeypad();
        _completeTimer.Tick += CompleteTimer_Tick;

        // 화면 어디든 터치(마우스)·키 입력을 활동으로 기록
        PreviewMouseDown += (_, _) => MarkActivity();
        PreviewTouchDown += (_, _) => MarkActivity();
        PreviewKeyDown += (_, _) => MarkActivity();
        _idleTimer.Tick += IdleTimer_Tick;
        _idleTimer.Start();

        ResetSession();

        App.Tunnel.StatusChanged += OnTunnelStatusChanged;
        UpdateQrCode();

        App.WebServer.MessageReceived += OnPhoneMessageReceived;
    }

    protected override void OnClosed(EventArgs e)
    {
        App.Tunnel.StatusChanged -= OnTunnelStatusChanged;
        App.WebServer.MessageReceived -= OnPhoneMessageReceived;
        base.OnClosed(e);
    }

    private void OnTunnelStatusChanged() => Dispatcher.BeginInvoke(new Action(UpdateQrCode));

    /// <summary>터널 주소를 받으면 QR 코드로 표시한다 (FR-13)</summary>
    private void UpdateQrCode()
    {
        var url = App.Tunnel.PublicUrl;
        if (url is not null)
        {
            QrImage.Source = QrCodeImage.Create(url);
            QrPlaceholderText.Visibility = Visibility.Collapsed;
            return;
        }

        QrImage.Source = null;
        QrPlaceholderText.Text = App.Tunnel.Error ?? "QR 준비 중";
        QrPlaceholderText.Visibility = Visibility.Visible;
    }

    private static Visibility VisibleIf(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    private void ShowScreen(KioskScreen screen)
    {
        CurrentScreen = screen;
        MarkActivity();
        QrScreen.Visibility = VisibleIf(screen == KioskScreen.Qr);
        StartScreen.Visibility = VisibleIf(screen == KioskScreen.Start);
        SessionLayout.Visibility = VisibleIf(screen is KioskScreen.DineSelect or KioskScreen.Menu);
        DineSelectContent.Visibility = VisibleIf(screen == KioskScreen.DineSelect);
        MenuContent.Visibility = VisibleIf(screen == KioskScreen.Menu);

        UpdateHeader();
        RecognizedText.Text = "말씀하시면 여기에 표시돼요";
        UpdateVoiceBar();
    }

    /// <summary>머리글의 식사 방식 표시와 단계 표시를 갱신한다 (화면 설계서)</summary>
    private void UpdateHeader()
    {
        DineBadge.Visibility = VisibleIf(CurrentDineMode is not null);
        DineBadgeText.Text = CurrentDineMode == DineMode.Takeout ? "포장" : "매장";

        var dineStep = CurrentScreen == KioskScreen.DineSelect;
        SetStepPill(StepDinePill, StepDineText, dineStep);
        SetStepPill(StepMenuPill, StepMenuText, !dineStep);
    }

    private void SetStepPill(Border pill, TextBlock text, bool active)
    {
        pill.Background = active ? (Brush)FindResource("PrimaryBrush") : (Brush)FindResource("LightGrayBrush");
        text.Foreground = active ? Brushes.White : (Brush)FindResource("SubTextBrush");
        text.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
    }

    /// <summary>화면 아래 음성 안내 칸: 음성 세션에서만 표시 (FR-01, FR-05, FR-08)</summary>
    private void UpdateVoiceBar()
    {
        if (IsVoiceSession && CurrentScreen is KioskScreen.DineSelect or KioskScreen.Menu)
        {
            // 휴대폰 버튼 문구: 포장/매장 단계 "대답하기", 메뉴 단계 "주문하기" (FR-06)
            var label = CurrentScreen == KioskScreen.Menu ? "주문하기" : "대답하기";
            _ = App.WebServer.SendToActiveAsync(new { type = "answer-label", label });
        }

        VoiceBar.Visibility = VisibleIf(IsVoiceSession && CurrentScreen is (KioskScreen.DineSelect or KioskScreen.Menu));
        if (CurrentScreen == KioskScreen.DineSelect)
        {
            VoiceBarTitle.Text = "듣고 있어요";
            VoiceBarSubtitle.Text = "‘포장’ 또는 ‘매장’이라고 말씀해 주세요";
        }
        else
        {
            VoiceBarTitle.Text = "무엇을 주문하시겠습니까?";
            VoiceBarSubtitle.Text = "메뉴와 수량을 말씀해 주세요";
        }
    }

    /// <summary>세션을 초기화하고 QR 화면(초기 화면)으로 돌아간다.</summary>
    private void ResetSession()
    {
        CurrentDineMode = null;
        IsVoiceSession = false;
        _voiceFailCount = 0;
        _lastVoiceBatchId = null;
        _orderItems.Clear();
        _completeTimer.Stop();
        CompleteScreen.Visibility = Visibility.Collapsed;
        OptionPopup.Visibility = Visibility.Collapsed;
        KeypadOverlay.Visibility = Visibility.Collapsed;
        CategoryList.SelectedIndex = _menu.Categories.Count > 0 ? 0 : -1;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        ShowScreen(KioskScreen.Qr);
    }

    // QR 화면 터치 → 시작 화면 (FR-01, FR-13)
    private void QrScreen_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowScreen(KioskScreen.Start);
    }

    // 시작 화면 터치 → 세션 시작, 포장/매장 선택 화면 (FR-01)
    private void StartScreen_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CurrentDineMode = null;
        IsVoiceSession = false;
        ShowScreen(KioskScreen.DineSelect);
    }

    // 포장/매장 터치 선택 (FR-04, FR-15)
    private void TakeoutButton_Click(object sender, RoutedEventArgs e) => SelectDineMode(DineMode.Takeout);

    private void DineInButton_Click(object sender, RoutedEventArgs e) => SelectDineMode(DineMode.DineIn);

    private void SelectDineMode(DineMode mode)
    {
        CurrentDineMode = mode;
        ShowScreen(KioskScreen.Menu);
    }

    private void OnPhoneMessageReceived(PhoneMessage message)
    {
        Dispatcher.BeginInvoke(new Action(() => HandlePhoneMessage(message)));
    }

    private void HandlePhoneMessage(PhoneMessage message)
    {
        // 휴대폰에서 온 말(인식 텍스트)·활성화도 활동으로 기록 (FR-10)
        MarkActivity();

        switch (message.Type)
        {
            case "voice-activated":
                StartVoiceSession();
                break;
            case "recognized":
                HandleRecognized(message.Text);
                break;
        }
    }

    // 휴대폰 "음성주문활성화" → 시작 화면을 거치지 않고 포장/매장 선택 화면 + 인사 음성 (FR-01, FR-02, FR-14)
    // 세션 진행 중(포장/매장 선택·메뉴 화면)에 눌린 경우의 동작은 확인 필요이므로 처리하지 않는다.
    private void StartVoiceSession()
    {
        if (CurrentScreen is not (KioskScreen.Qr or KioskScreen.Start))
        {
            return;
        }

        CurrentDineMode = null;
        IsVoiceSession = true;
        _voiceFailCount = 0;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        ShowScreen(KioskScreen.DineSelect);
        _ = App.WebServer.PlayAsync("greeting.wav");
    }

    // 휴대폰 대답하기로 받은 인식 텍스트 처리 (FR-03, FR-05, FR-06, FR-09)
    private void HandleRecognized(string? text)
    {
        if (!IsVoiceSession)
        {
            return;
        }

        // 인식된 말 표시 (FR-08)
        RecognizedText.Text = string.IsNullOrWhiteSpace(text) ? "(인식된 말 없음)" : $"“{text}”";

        switch (CurrentScreen)
        {
            case KioskScreen.DineSelect:
                var mode = DineModeParser.Parse(text);
                if (mode is null)
                {
                    OnVoiceRecognitionFailed();
                    return;
                }

                _voiceFailCount = 0;
                SelectDineMode(mode.Value);
                _ = App.WebServer.PlayAsync("ask-order.wav");
                break;

            case KioskScreen.Menu:
                HandleOrderSpeech(text);
                break;
        }
    }

    // 메뉴 화면에서 받은 주문 음성 처리: 메뉴·수량 분석 → 주문 목록 → 확인 멘트, "취소" 처리 (FR-07, FR-08, FR-17)
    private void HandleOrderSpeech(string? text)
    {
        var result = _orderParser.Parse(text);

        if (result.IsCancel)
        {
            var removed = _lastVoiceBatchId is { } batchId
                ? _orderItems.Where(item => item.VoiceBatchId == batchId).ToList()
                : [];
            if (removed.Count == 0)
            {
                OnVoiceRecognitionFailed();
                return;
            }

            foreach (var item in removed)
            {
                _orderItems.Remove(item);
            }

            _lastVoiceBatchId = null;
            _voiceFailCount = 0;
            _ = App.WebServer.PlayAsync("cancel.wav");
            return;
        }

        if (result.Lines.Count == 0)
        {
            OnVoiceRecognitionFailed();
            return;
        }

        _voiceFailCount = 0;
        var batch = Guid.NewGuid();
        _lastVoiceBatchId = batch;
        foreach (var line in result.Lines)
        {
            _orderItems.Add(new OrderItem
            {
                Menu = line.Menu,
                OptionSummary = BuildOptionSummary(line.Category, GetVoiceDefaultSelections(line.Category)) + " (음성 기본값)",
                IsVoice = true,
                VoiceBatchId = batch,
                Quantity = line.Quantity,
            });
        }

        var spoken = string.Join(", ", result.Lines.Select(l => $"{l.Menu.Name} {KoreanCount.ToSpoken(l.Quantity)} 잔"));
        _ = PlayConfirmationAsync($"{spoken} 맞습니까? 아니라면 취소라고 주문하기 버튼을 누르고 말해주세요. 다른메뉴 추가 주문하시면 주문하기버튼을 누르고 주문해주세요.");
    }

    /// <summary>음성 주문 옵션 기본값: HOT · Normal · 샷 기본 · 우유 (FR-07)</summary>
    private Dictionary<string, OptionChoice> GetVoiceDefaultSelections(MenuCategory category)
    {
        var selections = new Dictionary<string, OptionChoice>();
        foreach (var groupId in category.OptionGroups)
        {
            var group = _menu.OptionGroups.FirstOrDefault(g => g.Id == groupId);
            var choice = groupId switch
            {
                "temp" => group?.Choices.FirstOrDefault(c => c.Label == "HOT"),
                "size" => group?.Choices.FirstOrDefault(c => c.Label == "Normal"),
                _ => group?.Choices.FirstOrDefault(c => IsDefaultChoice(group, c)),
            };
            if (choice is not null)
            {
                selections[groupId] = choice;
            }
        }

        return selections;
    }

    // 확인 멘트를 실행 중 Azure TTS로 만들어 휴대폰에서 재생 (FR-17)
    private async Task PlayConfirmationAsync(string sentence)
    {
        var file = await _speech.SynthesizeAsync(sentence);
        if (file is not null)
        {
            await App.WebServer.PlayAsync(file);
        }
    }

    // 인식 실패: "천천히 말해주세요." / 3회 실패 시 터치 주문 안내 후 음성 입력 중단, 화면 유지 (FR-09)
    private void OnVoiceRecognitionFailed()
    {
        _voiceFailCount++;
        if (_voiceFailCount < MaxVoiceFailures)
        {
            _ = App.WebServer.PlayAsync("retry.wav");
            return;
        }

        IsVoiceSession = false;
        UpdateVoiceBar();
        _ = StopVoiceInputAsync();
    }

    private static async Task StopVoiceInputAsync()
    {
        await App.WebServer.PlayAsync("touch-guide.wav");
        await App.WebServer.SendToActiveAsync(new { type = "voice-stopped" });
    }

    // 대분류 선택 → 가운데에 소속 메뉴 표시 (FR-12)
    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var category = CategoryList.SelectedItem as MenuCategory;
        MenuGrid.ItemsSource = category?.Menus;
        CategoryTitle.Text = category?.Name ?? "";
        CategoryCount.Text = category is null ? "" : $"{category.Menus.Count}개 메뉴";
    }

    // ── 무응답 타임아웃 (FR-10) ──
    private void MarkActivity()
    {
        _lastActivityUtc = DateTime.UtcNow;
        UpdateIdleCountdown();
    }

    /// <summary>머리글 가운데에 무응답 타임아웃 남은 시간 표시</summary>
    private void UpdateIdleCountdown()
    {
        var remaining = IdleTimeout - (DateTime.UtcNow - _lastActivityUtc);
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        IdleCountdownText.Text = $"남은 시간 {seconds}초";
    }

    // 포장/매장 선택·메뉴 화면에서 60초간 터치와 말이 모두 없으면 세션을 초기화하고 QR 화면으로
    // (주문 완료 화면은 자체 30초 타이머를 따른다)
    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        var inSession = CurrentScreen is KioskScreen.DineSelect or KioskScreen.Menu;
        if (!inSession || CompleteScreen.Visibility == Visibility.Visible)
        {
            return;
        }

        if (DateTime.UtcNow - _lastActivityUtc >= IdleTimeout)
        {
            ResetSession();
            return;
        }

        UpdateIdleCountdown();
    }

    // ── 확인 알림 (FR-11, FR-18) ──
    private void ShowConfirm(string message, Action onYes)
    {
        ConfirmMessage.Text = message;
        _confirmAction = onYes;
        ConfirmOverlay.Visibility = Visibility.Visible;
    }

    private void ConfirmYes_Click(object sender, RoutedEventArgs e)
    {
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        var action = _confirmAction;
        _confirmAction = null;
        action?.Invoke();
    }

    private void ConfirmNo_Click(object sender, RoutedEventArgs e)
    {
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        _confirmAction = null;
    }

    // 시작페이지로 → 확인 알림 (FR-11)
    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        ShowConfirm("처음 화면으로 돌아가시겠습니까?", ResetSession);
    }

    // ── 옵션 팝업 (FR-15) ──
    private void MenuCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MenuItem menu } && CategoryList.SelectedItem is MenuCategory category)
        {
            OpenOptionPopup(category, menu);
        }
    }

    private void OpenOptionPopup(MenuCategory category, MenuItem menu)
    {
        _popupCategory = category;
        _popupMenu = menu;
        _popupSelections.Clear();
        _popupQuantity = 1;

        PopupCategoryText.Text = category.Name;
        PopupMenuName.Text = menu.Name;
        PopupMenuImage.Source = null;
        PopupImagePlaceholder.Visibility = Visibility.Visible;
        PopupQuantityButton.Content = "1";

        OptionGroupsPanel.Children.Clear();
        foreach (var groupId in category.OptionGroups)
        {
            var group = _menu.OptionGroups.FirstOrDefault(g => g.Id == groupId);
            if (group is not null)
            {
                OptionGroupsPanel.Children.Add(CreateOptionGroupView(menu, group));
            }
        }

        UpdatePopupAddButton();
        OptionPopup.Visibility = Visibility.Visible;
    }

    private UIElement CreateOptionGroupView(MenuItem menu, OptionGroup group)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 18) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock
        {
            Text = group.Name,
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextBrush"),
        });
        if (group.Id == "shot")
        {
            header.Children.Add(new TextBlock
            {
                Text = "샷추가는 최대 4회",
                FontSize = 24,
                Foreground = (Brush)FindResource("SubTextBrush"),
                Margin = new Thickness(18, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
        }

        panel.Children.Add(header);

        var grid = new UniformGrid { Columns = group.Choices.Count <= 2 ? 2 : 4, Margin = new Thickness(-8, 0, -8, 0) };
        var groupName = $"opt-{menu.Id}-{group.Id}";
        foreach (var choice in group.Choices)
        {
            var chip = new RadioButton
            {
                GroupName = groupName,
                Style = (Style)FindResource("OptionChip"),
                Content = CreateChipContent(group, choice),
                Tag = (group.Id, choice),
                IsChecked = IsDefaultChoice(group, choice),
            };
            if (chip.IsChecked == true)
            {
                _popupSelections[group.Id] = choice;
            }

            chip.Checked += OptionChip_Checked;
            grid.Children.Add(chip);
        }

        panel.Children.Add(grid);
        return panel;
    }

    private static UIElement CreateChipContent(OptionGroup group, OptionChoice choice)
    {
        if (choice.Value is not { } value)
        {
            return new TextBlock { Text = choice.Label, FontSize = group.Choices.Count <= 2 ? 34 : 28, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        }

        // 샷 조절: 위 줄 이름, 아래 줄 숫자 (화면 설계서)
        var title = value > 0 ? "샷추가" : choice.Label;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock
        {
            Text = value > 0 ? $"+{value}" : value < 0 ? $"−{-value}" : "0",
            FontSize = 24,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
        return stack;
    }

    /// <summary>menu.json의 기본값(샷 0, 우유 "우유")과 일치하는 선택지인지. 온도·사이즈는 기본값 없음</summary>
    private static bool IsDefaultChoice(OptionGroup group, OptionChoice choice)
    {
        if (group.Default is not { } def)
        {
            return false;
        }

        return def.ValueKind switch
        {
            JsonValueKind.Number => choice.Value == def.GetInt32(),
            JsonValueKind.String => choice.Label == def.GetString(),
            _ => false,
        };
    }

    private void OptionChip_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: ValueTuple<string, OptionChoice> tag })
        {
            _popupSelections[tag.Item1] = tag.Item2;
            UpdatePopupAddButton();
        }
    }

    // 온도·사이즈를 고르지 않으면 담기 비활성 (FR-15)
    private void UpdatePopupAddButton()
    {
        PopupAddButton.IsEnabled = _popupCategory is not null && RequiredOptionGroups
            .Where(id => _popupCategory.OptionGroups.Contains(id))
            .All(_popupSelections.ContainsKey);
    }

    private void SetPopupQuantity(int quantity)
    {
        _popupQuantity = Math.Clamp(quantity, 1, OrderItem.MaxQuantity);
        PopupQuantityButton.Content = _popupQuantity.ToString();
    }

    private void PopupMinus_Click(object sender, RoutedEventArgs e) => SetPopupQuantity(_popupQuantity - 1);

    private void PopupPlus_Click(object sender, RoutedEventArgs e) => SetPopupQuantity(_popupQuantity + 1);

    private void PopupQuantity_Click(object sender, RoutedEventArgs e) => OpenKeypad(SetPopupQuantity);

    private void PopupCancel_Click(object sender, RoutedEventArgs e)
    {
        OptionPopup.Visibility = Visibility.Collapsed;
    }

    private void PopupAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_popupCategory is null || _popupMenu is null || !PopupAddButton.IsEnabled)
        {
            return;
        }

        _orderItems.Add(new OrderItem
        {
            Menu = _popupMenu,
            OptionSummary = BuildOptionSummary(_popupCategory, _popupSelections),
            IsVoice = false,
            Quantity = _popupQuantity,
        });
        OptionPopup.Visibility = Visibility.Collapsed;
    }

    /// <summary>옵션 요약 문구 (예: "ICE · Normal · 샷 기본", "ICE · Normal · 두유")</summary>
    private static string BuildOptionSummary(MenuCategory category, IReadOnlyDictionary<string, OptionChoice> selections)
    {
        var parts = new List<string>();
        foreach (var groupId in category.OptionGroups)
        {
            if (!selections.TryGetValue(groupId, out var choice))
            {
                continue;
            }

            parts.Add(groupId == "shot" && choice.Value == 0 ? "샷 기본" : choice.Label);
        }

        return string.Join(" · ", parts);
    }

    // ── 주문 목록 (FR-12) ──
    private void OnOrderItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (OrderItem item in e.NewItems)
            {
                item.PropertyChanged += OnOrderItemPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (OrderItem item in e.OldItems)
            {
                item.PropertyChanged -= OnOrderItemPropertyChanged;
            }
        }

        UpdateOrderSummary();
    }

    private void OnOrderItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateOrderSummary();

    private void UpdateOrderSummary()
    {
        OrderCountText.Text = $"{_orderItems.Count}개 항목";
        TotalQuantityText.Text = $"{_orderItems.Sum(i => i.Quantity)}잔";
        CompleteOrderButton.IsEnabled = _orderItems.Count > 0;
        ClearOrderButton.IsEnabled = _orderItems.Count > 0;
    }

    private void OrderItemMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OrderItem item })
        {
            item.Quantity--;
        }
    }

    private void OrderItemPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OrderItem item })
        {
            item.Quantity++;
        }
    }

    private void OrderItemQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OrderItem item })
        {
            OpenKeypad(value => item.Quantity = value);
        }
    }

    private void OrderItemDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OrderItem item })
        {
            _orderItems.Remove(item);
        }
    }

    // 전체 삭제 → 확인 알림 (FR-18)
    private void ClearOrderButton_Click(object sender, RoutedEventArgs e)
    {
        ShowConfirm("메뉴 전체를 삭제시킵니다. 정말로 하시겠습니까?", () => _orderItems.Clear());
    }

    // 주문 완료 → 완료 화면, 30초 후 QR 화면 (FR-19)
    private void CompleteOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_orderItems.Count == 0)
        {
            return;
        }

        _completeRemainingSeconds = CompleteScreenSeconds;
        UpdateCompleteCountdown();
        CompleteScreen.Visibility = Visibility.Visible;
        _completeTimer.Stop();
        _completeTimer.Start();
    }

    // 남은 초를 1초마다 줄여 표시, 0초가 되면 QR 화면으로 (FR-19)
    private void CompleteTimer_Tick(object? sender, EventArgs e)
    {
        _completeRemainingSeconds--;
        if (_completeRemainingSeconds <= 0)
        {
            ResetSession();
            return;
        }

        UpdateCompleteCountdown();
    }

    private void UpdateCompleteCountdown()
    {
        CompleteCountdownText.Text = $"{_completeRemainingSeconds}초 후 처음 화면으로 돌아갑니다";
    }

    private void CompleteHomeButton_Click(object sender, RoutedEventArgs e) => ResetSession();

    // ── 앱 내 숫자 키패드 (FR-15) ──
    private void BuildKeypad()
    {
        string[] keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "지움", "0", "←"];
        foreach (var key in keys)
        {
            var button = new Button
            {
                Content = key,
                Tag = key,
                Style = (Style)FindResource("OutlineButton"),
                FontSize = key.Length == 1 ? 44 : 32,
                Margin = new Thickness(8),
            };
            button.Click += KeypadKey_Click;
            KeypadGrid.Children.Add(button);
        }
    }

    private void OpenKeypad(Action<int> callback)
    {
        _keypadCallback = callback;
        _keypadInput = "";
        KeypadDisplay.Text = "";
        KeypadOverlay.Visibility = Visibility.Visible;
    }

    private void KeypadKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        switch (key)
        {
            case "지움":
                _keypadInput = "";
                break;
            case "←":
                if (_keypadInput.Length > 0)
                {
                    _keypadInput = _keypadInput[..^1];
                }

                break;
            default:
                if (_keypadInput.Length < 3 && !(_keypadInput.Length == 0 && key == "0"))
                {
                    _keypadInput += key;
                }

                break;
        }

        KeypadDisplay.Text = _keypadInput;
    }

    private void KeypadOk_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(_keypadInput, out var value) && value is >= 1 and <= OrderItem.MaxQuantity)
        {
            _keypadCallback?.Invoke(value);
        }

        KeypadOverlay.Visibility = Visibility.Collapsed;
        _keypadCallback = null;
    }

    private void KeypadClose_Click(object sender, RoutedEventArgs e)
    {
        KeypadOverlay.Visibility = Visibility.Collapsed;
        _keypadCallback = null;
    }
}
