# EasyOrder

## 목차
- [1. 프로젝트 개요](#1-프로젝트-개요)
- [2. 기술 스택](#2-기술-스택)
- [3. 프로젝트 구조](#3-프로젝트-구조)
- [4. 프로젝트 진행상황](#4-프로젝트-진행상황)
- [5. 기능 개발 및 수정 이력](#5-기능-개발-및-수정-이력)

---

## 1. 프로젝트 개요
* **프로젝트 설명**: 카페(폴리텍점) 키오스크에서 화면 터치로 시작해 음성 안내와 음성 인식으로 식사 방식(포장/매장)과 주문(메뉴·수량)을 인식하는 WPF 프로토타입
* **배포 URL**: 해당 없음 (운영 배포 제외)

## 2. 기술 스택
* **Frontend**: WPF (C# 14, .NET 10, net10.0-windows)
* **Backend**: WPF 앱 내장 로컬 웹서버 (포트 6703) + WebSocket
* **음성**: TTS - Azure SunHi로 미리 만든 WAV / STT - 휴대폰 Chrome Web Speech API
* **Database**: JSON 파일
* **Infrastructure**: 해당 없음 (HTTPS 접속용 Cloudflare Quick Tunnel만 사용)

## 3. 프로젝트 구조
```text
EasyOrder/
├─ .gitignore
├─ README.md
├─ EasyOrder.slnx
├─ docs/
│  ├─ 수행계획서.md
│  └─ 요구사항명세서.md
└─ src/
   └─ EasyOrder/
      ├─ EasyOrder.csproj
      ├─ App.xaml
      ├─ App.xaml.cs
      ├─ AssemblyInfo.cs
      ├─ MainWindow.xaml
      └─ MainWindow.xaml.cs
```

## 4. 프로젝트 진행상황

| 구분 | 기능명 | 세부 내용 | 상태 | 완료일/수정일 | 비고 |
| --- | --- | --- | --- | --- | --- |
| 문서 | 기준 문서 추가 | 수행계획서·요구사항명세서를 docs/에 저장 | 완료 | 2026-10-16 | - |
| 환경 설정 | .gitignore 비밀 정보 제외 | Visual Studio용 .gitignore에 `.env`, `*.local.json`, `secrets.json` 제외 규칙 추가 | 완료 | 2026-10-16 | - |
| 환경 설정 | WPF 프로젝트 생성 | net10.0-windows WPF 프로젝트 및 솔루션(EasyOrder.slnx) 생성, `dotnet build` 성공 확인 | 완료 | 2026-10-16 | - |
| 문서 | README 초기 작성 | 프로젝트 개요, 기술 스택, 구조, 진행상황, 이력 작성 | 완료 | 2026-10-16 | - |

* **상태 구분**: `대기`, `진행 중`, `완료`, `보류`

## 5. 기능 개발 및 수정 이력

### 5.1 신규 개발 이력

* **2026-10-16 기준 문서 추가**
  * **개발 내용**: 수행계획서.md, 요구사항명세서.md를 docs/ 폴더에 저장
  * **관련 요구사항**: 해당 없음 (산출물 문서)
* **2026-10-16 .gitignore 비밀 정보 제외**
  * **개발 내용**: 기존 Visual Studio용 .gitignore에 `.env`, `*.local.json`, `secrets.json` 제외 규칙 추가
  * **관련 요구사항**: 해당 없음 (작업 지침 5장 API 키 및 비밀 정보 보호)
* **2026-10-16 WPF 프로젝트 생성**
  * **개발 내용**: `dotnet new wpf`로 src/EasyOrder 프로젝트(TargetFramework net10.0-windows) 생성, EasyOrder.slnx 솔루션에 추가, `dotnet build` 성공 확인
  * **관련 요구사항**: NFR-01
* **2026-10-16 README 초기 작성**
  * **개발 내용**: 작업 지침 7장 템플릿으로 README.md 작성
  * **관련 요구사항**: 해당 없음 (산출물 문서)

### 5.2 기능 수정 이력

* 해당 없음 (아직 수정 이력 없음)
