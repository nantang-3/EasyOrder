namespace EasyOrder.Models;

/// <summary>키오스크 화면 단계 (FR-01)</summary>
public enum KioskScreen
{
    /// <summary>QR 화면 (초기 화면)</summary>
    Qr,

    /// <summary>시작 화면 ("화면을 터치해 주세요")</summary>
    Start,

    /// <summary>포장/매장 선택 화면</summary>
    DineSelect,

    /// <summary>메뉴 화면 (좌: 대분류 / 중: 소속 메뉴 / 우: 주문 목록)</summary>
    Menu,
}
