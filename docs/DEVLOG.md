# Mutiblox 開發日誌

新的紀錄寫在最上面。

---

## 2026-11-03 — 對應 Roblox 0.745 的 tray 背景程序

### 背景
距上次執行（2026-04-25）約半年，確認專案在 Roblox `0.745.0.7451137` 上能否正常多開。

### 檢查結果
| 項目 | 結果 |
|---|---|
| 建置 / 測試 | 通過（0 警告 0 錯誤） |
| `ROBLOX_singletonMutex` / `ROBLOX_singletonEvent` 名稱 | 未變（直接比對 `RobloxPlayerBeta.exe` 內的字串） |
| `RobloxCookies.dat` 路徑與 `FileShare.None` 鎖定 | 仍有效 |

### 問題 1：Roblox tray 背景程序搶先持有 mutex
- **現象**：Roblox 在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 註冊了
  `RobloxPlayerBeta.exe --launch-to-tray`，Windows 登入時就自動啟動。
- **Log 證據**（`%LOCALAPPDATA%\Roblox\logs\*_Player_*_last.log`）：
  ```
  [FLog::Systray] In Tray ELT Treatment group          ← A/B 實驗分組
  [FLog::WndProcessCheck] waitForNewPlayerProcess new waiting for mutex result is 0X000000
  ```
  tray 程序啟動即取得 singleton mutex。2026-04 的 `mutex_debug.log` 仍顯示 `No existing Roblox processes found`，代表這是之後才出現的變化。
- **修正**：`MutexService.TerminateTrayProcesses()` — 取得 mutex 前，透過 WMI（`System.Management`）讀取 `RobloxPlayerBeta.exe` 的命令列，結束帶 `--launch-to-tray` 的程序。它不是遊戲客戶端，下次登入會自動重啟。
- **附帶好處**：不再需要對 tray 程序做 `OpenProcess` + `DuplicateHandle(DUPLICATE_CLOSE_SOURCE)`，減少對 Roblox 程序的操作。
- **社群參考**：[ethanstoner/Roblox-Account-Manager#7](https://github.com/ethanstoner/Roblox-Account-Manager/issues/7) 回報相同現象（GitHub issue，中等可信度）。

### 問題 2：效能監控的 OOM 會讓整個 app 崩潰
- **現象**：`%LOCALAPPDATA%\Mutiblox\crash.log` 有 3 次 `OutOfMemoryException`（03-16、03-18、04-25），都在 `PerformanceService` 的 timer callback。
- **影響**：背景執行緒未處理的例外會終止程序 → mutex owner 執行緒消失 → Roblox `WndProcessCheck` 取得 mutex → 關閉所有其他實例（與 2026-03-25 的 WAIT_ABANDONED 同一機制）。
- **修正**：`UpdateMetricsAsync` 加 catch-all，取數據失敗不影響 app 存活。

### 驗證
- 重新 publish 至 `publish\`，使用者實測多開正常。

### 待觀察風險
- **Bound Auth Token**（`x-bound-auth-token`）：Roblox 登入頁已啟用 secure authentication intent。若開始強制簽章，透過內建 WebView2 登入的帳號可能無法取得 auth ticket（2026-09 回報時尚未強制）。參考 [ethanstoner/Roblox-Account-Manager#10](https://github.com/ethanstoner/Roblox-Account-Manager/issues/10)。
- 超過 4 個帳號同時啟動的穩定性仍未測試。

---

## 2026-03-25 — 多開穩定（回顧）

達成 4 帳號同時在線、無 Reason 285 斷線。兩個關鍵修正：
1. **Cookie 交叉污染**：停止注入 cookie 到共用的 `RobloxCookies.dat`，改為只用 auth ticket 啟動，並在 app 啟動時永久鎖住 cookie 檔。
2. **Mutex WAIT_ABANDONED**：mutex 原本在 `Task.Run` 的 thread pool 執行緒上取得，執行緒被回收後 mutex 變成 abandoned。改為在專屬的 `MutexOwner` 執行緒上持有直到 app 關閉。

已知無害問題：啟動時 SignalR 401（cookie 檔被鎖，僅影響好友/通知等社交功能）。
