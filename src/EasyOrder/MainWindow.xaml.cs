using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyOrder.Models;
using EasyOrder.Services;

namespace EasyOrder;

/// <summary>
/// 키오스크 메인 화면. QR 화면 → 시작 화면 → 포장/매장 선택 화면 → 메뉴 화면 흐름을 관리한다.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MenuCatalog _menu;

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

    private void ShowScreen(KioskScreen screen)
    {
        CurrentScreen = screen;
        QrScreen.Visibility = screen == KioskScreen.Qr ? Visibility.Visible : Visibility.Collapsed;
        StartScreen.Visibility = screen == KioskScreen.Start ? Visibility.Visible : Visibility.Collapsed;
        DineSelectScreen.Visibility = screen == KioskScreen.DineSelect ? Visibility.Visible : Visibility.Collapsed;
        MenuScreen.Visibility = screen == KioskScreen.Menu ? Visibility.Visible : Visibility.Collapsed;

        var inSession = screen is KioskScreen.DineSelect or KioskScreen.Menu;
        HomeButton.Visibility = inSession ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>세션을 초기화하고 QR 화면(초기 화면)으로 돌아간다.</summary>
    private void ResetSession()
    {
        CurrentDineMode = null;
        IsVoiceSession = false;
        OrderList.Items.Clear();
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
        if (message.Type == "voice-activated")
        {
            StartVoiceSession();
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
        HomeConfirmOverlay.Visibility = Visibility.Collapsed;
        ShowScreen(KioskScreen.DineSelect);
        _ = App.WebServer.PlayAsync("greeting.wav");
    }

    // 대분류 선택 → 가운데에 소속 메뉴 표시 (FR-12)
    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MenuList.ItemsSource = (CategoryList.SelectedItem as MenuCategory)?.Menus;
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
