# 🤖 Telegram 2FA & Bot Architecture

This document explains how the Telegram verification system and bot service are designed and integrated within **AUB Digital Academy**.

---

## 1. ⚙️ Component Architecture

```
┌─────────────────────────┐          ┌──────────────────────────┐
│   Telegram Bot Client   │ ◄──────► │  Telegram Bot API (HTTP) │
│     (@AUBLinkBot)       │          │ api.telegram.org/bot<...│
└─────────────────────────┘          └─────────────▲────────────┘
                                                   │
                         ┌─────────────────────────┴─────────────┐
                         │                                       │
                         │                                       │
            ┌────────────┴─────────────┐           ┌─────────────┴────────────┐
            │     ITelegramService     │           │  TelegramPollingService  │
            │   (Outbound Messages)    │           │ (Background Long-Poll)   │
            │  - SendVerificationCode  │           │ - Processes /start       │
            │  - SendMessageAsync      │           │ - Processes /link <ID>   │
            └────────────▲─────────────┘           └─────────────┬────────────┘
                         │                                       │
                         │                                       │
            ┌────────────┴───────────────────────────────────────▼────────────┐
            │                     ApplicationDbContext                        │
            │           (AspNetUsers & TelegramVerificationRequests)          │
            └─────────────────────────────────────────────────────────────────┘
```

---

## 2. 🔑 Configuration & Secrets

The Telegram Bot token is resolved through configuration priority in `TelegramService.cs`:
1. `TelegramBot:Token` (.NET User Secrets — recommended for local dev)
2. `Telegram:BotToken` (`appsettings.json` or `appsettings.Production.json`)
3. `TELEGRAM_BOT_TOKEN` (Environment variable — recommended for Docker/Cloud)

### Viewing / Setting Bot Secrets Locally
```powershell
# Set token in .NET User Secrets
dotnet user-secrets set "TelegramBot:Token" "<YOUR_TELEGRAM_BOT_TOKEN>" --project "AUB-Digital-Academy-Backend\AUBDigitalAcademy.csproj"

# List configured secrets
dotnet user-secrets list --project "AUB-Digital-Academy-Backend\AUBDigitalAcademy.csproj"
```

---

## 3. 💬 Bot Commands Supported ([@AUBLinkBot](https://t.me/AUBLinkBot))

| Command | Usage | Behavior |
|---|---|---|
| **`/start`** | `/start` | Displays institutional introduction and instructions. |
| **`/link`** | `/link <StudentID>` | Binds the user's Telegram `ChatId` to their Student ID in the database (`user.TelegramChatId = chatId`). |
| **`/status`** | `/status` | Shows the user's Chat ID, username, and active connection status. |
| **`/help`** | `/help` | Explains available commands and IT support email. |
| **`/about`** | `/about` | Overview of ACLEDA University of Business Digital Academy. |

---

## 4. ⏳ Life-Cycle of a Verification Request

1. **Trigger**:
   * A new student registers at `/Account/Register`, or an unverified student attempts to log in at `/Account/Login`.
2. **Generation**:
   * A 6-digit code is created using `RandomNumberGenerator.GetInt32(100000, 1000000)`.
   * A record is inserted into `TelegramVerificationRequests`:
     * `ExpiresAt`: Set to 10 minutes from creation.
     * `MaxAttempts`: Set to 5 attempts.
     * `IsUsed`: `false`.
3. **Dispatch**:
   * `_telegramService.SendVerificationCodeAsync(...)` formats an HTML-styled message with student details.
   * If `user.TelegramChatId` exists (linked via `/link`), the message is sent directly to the student's personal chat.
   * If not linked, it falls back to the institutional channel or logs the code with `[DEV SIMULATION]` in local development.
4. **Validation**:
   * Student enters the 6-digit code on `/Account/VerifyTelegram`.
   * If incorrect, `AttemptsCount` increments and shows remaining attempts.
   * If correct, `IsUsed` becomes `true`, `user.IsTelegramVerified` becomes `true`, and the account is activated.

---

## 5. ⚠️ Private Chats vs. Supergroups (Important Rule)

Telegram's Bot API rejects messages with `HTTP 400 Bad Request` if you send `message_thread_id` to a private user chat:
* Private chats (student personal DMs) have positive numeric IDs (`6293912194`).
* Supergroup forums with topics have negative IDs starting with `-100` (`-100123456789`).
* **Rule**: Never pass `message_thread_id` when delivering to personal student chats (`customChatId`).
