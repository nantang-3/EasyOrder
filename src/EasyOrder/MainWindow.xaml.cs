using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyOrder.Models;
using EasyOrder.Services;

namespace EasyOrder;

/// <summary>
/// 키오스크 메인 화면. QR 화면 → 시작 화면 → 포장/매장 선택 화면 → 메뉴 화면 흐름을 관리한다.
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxVoiceFailures = 3;

    private readonly MenuCatalog _menu;

    /// <summary>현재 질문 단계에서 음성 인식에 실패한 횟수 (FR-09)</summary>
    private int _voiceFailCount;

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
        CategoryList.ItemsSource = _menu.Categories;

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
        CategoryList.SelectedIndex = _menu.Categories.Count > 0 ? 0 : -1;
        HomeConfirmOverlay.Visibility = Visibility.Collapsed;
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
        HomeConfirmOverlay.Visibility = Visibility.Collapsed;
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
                // 주문(메뉴·수량) 음성 분석은 10/19 작업 (FR-07)
                break;
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

    // 시작페이지로 → 확인 알림 (FR-11)
    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        HomeConfirmOverlay.Visibility = Visibility.Visible;
    }

    private void HomeConfirmYes_Click(object sender, RoutedEventArgs e) => ResetSession();

    private void HomeConfirmNo_Click(object sender, RoutedEventArgs e)
    {
        HomeConfirmOverlay.Visibility = Visibility.Collapsed;
    }
}
