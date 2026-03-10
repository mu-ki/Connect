# Connect (TeamApp)

This repository contains the ASP.NET Core API (`TeamApp.API`) and the Angular frontend (`team-app-client`).

---

## ✅ Keep the repository clean
We intentionally **do not** commit build output or publish artifacts. The following are ignored by `.gitignore`:

- `TeamApp.API/bin/`, `TeamApp.API/obj/`
- `TeamApp.API/wwwroot/` (Angular build output)
- `team-app-client/dist/`, `team-app-client/node_modules/`

---

## 🚀 Build & publish for IIS (subsite: `/Connect`)
This project is hosted under an IIS virtual application (e.g., `https://yourhost/Connect/`).

### 1) Build and publish (from repo root)
Run:

```bat
run_app.bat
```

This does three things:
1. Builds the Angular app production bundle.
2. Copies the Angular output into `TeamApp.API/wwwroot/`.
3. Publishes the ASP.NET Core app to `C:\Websites\Home\Connect`.

> If you want a different publish folder, pass it as an argument:
>
> ```bat
> run_app.bat "D:\Some\Other\Path"
> ```

### 2) Configure IIS virtual app path
To make the app work under `/Connect`, the app needs to know its base path.

#### Option A (recommended): Set in `web.config` (IIS):
Add this under the `<aspNetCore ... />` section:

```xml
<environmentVariables>
  <environmentVariable name="ASPNETCORE_APPL_PATH" value="/Connect" />
</environmentVariables>
```

#### Option B: Use `appsettings.json`
Set `BasePath` in `TeamApp.API/appsettings.json`:

```json
"BasePath": "/Connect"
```

Either option ensures ASP.NET Core uses `app.UsePathBase("/Connect")` at runtime.

---

## 🔧 Adjusting the base path later
If you move the app to a different virtual directory (e.g., `/MyApp`), update **either**:

- `ASPNETCORE_APPL_PATH` in IIS/web.config (preferred)
- or `BasePath` in `TeamApp.API/appsettings.json`

---

## 🧹 Publishing notes
When publishing to the IIS folder, stop the app pool (or site) first, because files can be locked while the app is running. Restart after publish.
