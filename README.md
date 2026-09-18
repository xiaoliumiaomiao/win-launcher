# WinLauncher

**English** | [中文](README.zh-CN.md)

> One icon on the desktop. Click it, and your apps are there — sorted by what they're for.

![WinLauncher](docs/screenshot-main.png)

Windows has a habit of filling the desktop and Start Menu with shortcuts, one folder per vendor, with names like `Balena Ltd` and `CC Switch`. Finding anything means reading a list somebody else organized.

WinLauncher replaces all of that with a single icon: a small panel that finds your apps, sorts them by purpose, and lets you write a one-line note under each one.

---

## What it does

- **Finds your apps by itself.** First launch scans the Start Menu and Desktop and sorts whatever it finds. No folder to pick, no list to maintain.
- **Categories by purpose, not by vendor.** Eleven fixed categories that don't grow every time you install something.
- **An app can sit in several categories.** PowerShell appears under both `开发工具` (dev tools) and `系统工具` (system tools).
- **A one-line description under every card** — editable, and pre-filled from the program's own metadata as a starting point.
- **Nothing is ever deleted.** Move a shortcut away and the card greys out; move it back and it returns with its description and categories intact.

## Quick start

**Just want to use it** — download **[WinLauncher.exe](https://github.com/xiaoliumiaomiao/win-launcher/releases/latest)** and run it. Nothing to install; the executable is self-contained and does not need the .NET runtime.

To get it onto the desktop too: open it and use **设置 → 创建桌面快捷方式**. That gives the shortcut the right name and icon; the Windows "Send to → Desktop" route does not.

**Building from source** needs the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```powershell
git clone https://github.com/xiaoliumiaomiao/win-launcher.git
cd win-launcher
pwsh -File tools\install.ps1
```

That produces `publish\WinLauncher.exe` and a desktop shortcut in one step.

## Using it

| Do this | To get |
|---|---|
| Double-click the desktop icon | Open it |
| **Alt + .** | Summon it from anywhere |
| `F5` | Re-scan |
| Drag a shortcut into the window | Add an app |
| Right-click a card → 编辑简介 | Write the one-line description |
| Right-click a card → 归类 | Tick the categories it belongs to |
| Right-click a card → 从列表中移除 | Drop it from the list (touches no file on disk) |
| Type in the search box | Filter by name, description or path |
| Tick 开机时自动启动 in Settings | Have it start with Windows, silently in the tray |
| `✕` | Hide to the system tray, so the hotkey keeps working; quit from the tray menu |

The interface is in Chinese, and the category set is Chinese by design. The classifier matches both Chinese and English app names — several built-in Windows tools are only recognizable by their English names.

## How apps get categorized

Apps are discovered in both Start Menus (per-user and all-users) and both Desktops, then deduplicated by target path so a program listed twice appears once.

**Categories do not come from the Start Menu's folder names.** Those are vendor directories — `Balena Ltd`, `CodeBuddy CN`, `Logi` — and copying them yields 32 "categories", 18 of which hold exactly one app. Instead each app is matched against eleven fixed purpose categories using its name, its Start Menu folder, and its target path.

Two details that took real testing to get right:

- **Latin keywords match on word boundaries; Chinese and symbol keywords match as substrings.** Substring matching for Latin is a trap — the keyword `msi` (the hardware vendor) matches `msinfo32.exe`, filing System Information under hardware drivers.
- **Noise is filtered by target, not by name.** A shortcut pointing at `.html`, `.pdf`, `.txt` or `.chm` is documentation, not an application. That one rule catches `Git Release Notes` (→ `releasenotes.html`) and `Python Manuals` (→ `doc\html\index.html`), which no amount of keyword tuning handles cleanly. Names are still checked for uninstallers, help pages and website links — about 23% of the shortcuts on a typical machine.

## Design decisions

**Annotations live on the target path, not on the entry.**
Descriptions, custom names and category overrides are keyed by the resolved executable path. Delete a shortcut and restore it a month later, rename it, rename the whole category — none of it is lost. It also removes any "remember to carry this field across" step from the reconciler, which is where that kind of data usually goes missing.

**Sync marks things missing; it never deletes them.**
An unplugged USB drive, a dropped network share, a program temporarily moved — all of them look like "everything just disappeared". Deleting on that signal would take your descriptions with it. Missing entries get flagged and greyed out instead. A few dozen bytes each is not worth the risk.

**An entry knows its categories; a category does not own its entries.**
The obvious model — each category holds a list of apps — makes "this app belongs in two places" impossible. Inverting it means multi-category membership and re-categorization are both just edits to an array.

**The app pins itself to normal privileges.**
Windows' UIPI stops a non-elevated Explorer from sending drag-and-drop messages to an elevated process. Run WinLauncher as administrator and dropping a shortcut onto it fails **silently** — no error, no log, nothing to search for. The manifest asks for `asInvoker`, and the app checks its own token at startup so it can offer a one-click relaunch if it ever ends up elevated.

**The autostart entry never points at a stale path.**
Moving the `.exe` would normally leave the registry pointing at the old location, and autostart would stop working with nothing to show for it. So the entry is rewritten on every launch if it no longer matches the running executable — the failure surfaces weeks later and gets blamed on something else.

**Drag-and-drop failures leave evidence.**
For the same reason. The failure mode is invisible, so every `DragOver` and `Drop` is written to `diagnostics.log`. Otherwise there is nothing to go on.

## Five bugs worth reading about

Around twenty more are in the source comments.

| Bug | What it taught |
|---|---|
| Declared `IPersist::GetClassID` as the first method of `IShellLinkW` | The interface derives from `IUnknown`, so every method was off by one vtable slot. `GetPath` was really calling `GetIDList`, handing back pointer bytes as a string — **and still returning `S_OK`**. No exception, no error code, just garbage. Comparing against a known-good reference (`WScript.Shell`, in a throwaway script) beat reading the header. |
| Running the app elevated broke drag-and-drop | Not a code bug. UIPI silently blocks cross-integrity drag messages. The fix was to detect the situation and say so — the failure gives you nothing to debug with. |
| The keyword `msi` matched `msinfo32.exe` | Filed System Information under hardware drivers. Substring matching on short Latin keywords is always wrong. |
| A PowerShell test harness reported every app as uncategorized | `$array.SomeMissingProperty` returns an array of `$null`s — and a non-empty array is truthy in PowerShell, so my guard took the wrong branch. **The instrument was broken, not the thing being measured.** Worth checking before rewriting working code. |
| A 2560×1440 display measured as 2048×1152 | A DPI-unaware process gets virtualized coordinates, so window positions and screenshots were off by 25%. Real hardware, real settings, or you are guessing. |

## License

[MIT](LICENSE)
