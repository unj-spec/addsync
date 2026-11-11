# 플랜

목표: **이 PC의 애드온 + 설정을 다른 PC에 자동으로 설치.**
사용법은 [README.md](README.md).

---

## 방식 선택 — 왜 "복사"인가

| 방식 | 내용 | 판단 |
|---|---|---|
| **A. 폴더 복사** (채택) | 애드온 폴더와 WTF를 zip으로 떠서 다른 PC에 전개 | **API 키·계정·인터넷 전부 불필요. 설정까지 100% 동일하게 재현.** 지금 바로 동작 |
| B. 목록 + 최신버전 다운로드 | manifest에 애드온 ID만 적고, 대상 PC에서 각 소스에서 내려받아 설치 | CurseForge API 키 심사 필요(거부 선례 있음), Wago 읽기 키 유료 조건, `allowModDistribution=false`면 다운로드 자체 불가. 설정은 별도 복사 필요 |

B의 유일한 장점은 "최신 버전"인데, 그건 대상 PC에서 CurseForge 앱으로 업데이트하면 해결된다.
**A로 옮긴 뒤 평소처럼 업데이트하면 된다.**

---

## 형태

**단일 실행 파일 `WowAddonManager.exe` (WinForms).** .NET Framework 4의 `csc.exe`로 빌드하므로
SDK·런타임 설치가 필요 없고, 다른 PC에 exe만 들고 가면 바로 돈다.
`scripts\`에 같은 기능의 PowerShell 버전을 남겨 자동화에 쓴다.

## 구현 상태

- [x] WoW 설치 경로 자동 탐지 (레지스트리 → Battle.net `product.db` → 흔한 경로)
- [x] flavor 열거 (`_retail_` / `_forever_` / `_anniversary_` …), flavor별 분리 처리
- [x] `.toc` 파싱 → 애드온 이름·버전·소스ID를 `manifest.json`에 기록
- [x] AddOns / WTF zip 내보내기 (`*.old`, `*.bak`, `cache.md5` 제외)
- [x] 설치 시 기존 상태 zip 자동 백업 → `backup\<날짜-시간>\`
- [x] WoW 실행 중이면 설치 거부 (파일 잠금 방지)
- [x] zip-slip 방지, `-WhatIf`, `-Clean`, `-NoSettings`, `-Flavor`

### 조사해서 확인한 이 PC 환경

| 항목 | 값 |
|---|---|
| WoW | `C:\Program Files (x86)\World of Warcraft` |
| flavor | `_retail_`(애드온 69) / `_forever_`(56) / `_anniversary_`(0) |
| 용량 | AddOns 250MB + 329MB, WTF 181MB + 0.9MB |
| CurseForge | Overwolf 경유. 로컬 `app.db`에 **WoW 애드온 인벤토리 없음**(설치 이력만) → 참고용으로도 불필요 |
| `.toc` 소스ID | `X-Curse-Project-ID` 93, `X-Wago-ID` 81, `X-WoWI-ID` 38 → 기록은 되지만 복사 방식에선 쓰지 않음 |

---

## 남은 선택지 (필요해지면)

1. **클라우드 폴더 자동화** — `-Output`을 OneDrive/구글드라이브로 두고 작업 스케줄러에 등록 → 매일 자동 내보내기. 다른 PC에선 설치만.
2. **최신 버전 갱신** — WoWInterface(`api.mmoui.com`, 키 불필요) + GitHub Releases(키 불필요)만으로 `update` 명령 추가. 커버 안 되는 CurseForge 전용 애드온은 링크만 제공.
3. **계정ID 매핑** — 다른 배틀넷 계정으로 옮길 때 `WTF\Account\<번호>` 폴더명을 바꿔주는 옵션.

지금은 1번이 가장 값이 싸고 효과가 크다.
