# 📚 AUB Digital Academy — Developer Documentation Hub

Welcome to the development team of **AUB Digital Academy**! This documentation hub contains the core architectural standards, coding principles, and routing conventions used across the project.

---

## 📑 Documentation Index

| Guide | Description |
|---|---|
| [1. Coding Principles & Clean Code](CODING_PRINCIPLES.md) | C# standards, SOLID principles, async/await rules, Dependency Injection practices, and error handling. |
| [2. Routing & URL Conventions](ROUTING_CONVENTIONS.md) | Standard URL structures, casing rules, role prefixes, query vs route parameters, and AJAX vs View routes. |
| [3. MVC Architecture Guide](MVC_ARCHITECTURE_GUIDE.md) | How Models, ViewModels, Services, Controllers, and Views interact; layer boundaries and responsibilities. |
| [4. Telegram 2FA & Bot Architecture](TELEGRAM_2FA_WORKFLOW.md) | Telegram bot integration, verification code lifecycles, student account linking (`/link`), and polling services. |

---

## ⚡ Quick Architectural Mental Model

```
Request ➔ Middleware (Auth/Cookie/CORS)
        ➔ Controller (Route matching & Model Validation)
        ➔ Service Layer (Business logic & calculations)
        ➔ Data Layer / EF Core (ApplicationDbContext & SQLite/SQL Server)
        ➔ ViewModel Construction
        ➔ Razor View Rendering (Pre-rendered HTML sent to client)
```

---

## 🤝 Team Rules of Engagement

1. **Never query `ApplicationDbContext` directly inside Views**: Always query via the Controller/Service layer and pass data through strongly-typed ViewModels.
2. **Never expose Domain Entities directly to Views**: Use ViewModels (`Models/ViewModels/`) to protect sensitive domain properties and prevent over-posting vulnerabilities.
3. **One Controller, One Primary Constructor**: Always ensure Dependency Injection knows which constructor to call. Use `[ActivatorUtilitiesConstructor]` if test constructors exist.
4. **Always validate CSRF Tokens**: Include `@Html.AntiForgeryToken()` in forms and `[ValidateAntiForgeryToken]` on HTTP POST/PUT/DELETE actions.
5. **Keep Controllers Thin**: Controllers coordinate requests and responses. Business calculations, Telegram delivery, and curriculum logic belong in `Services/`.
