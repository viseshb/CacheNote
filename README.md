# CacheNote

A premium, offline-first notes & reminders app for Windows 11 — Apple Notes simplicity with Windows Sticky Notes convenience, built with native Windows 11 design language. **Dark mode, always.**

![Platform](https://img.shields.io/badge/platform-Windows%2011%2B-blue)
![Stack](https://img.shields.io/badge/.NET%209-WinUI%203-purple)
![Release](https://img.shields.io/github/v/release/viseshb/CacheNote)

## Features

### Notes
- Rich-text editor (RichEditBox): **bold**, *italic*, underline, headings (H1–H3), font family & size
- **Font color**: instant quick-color swatches + a full color-spectrum picker; picked colors persist per character run
- Three list types — bullets (•), numbered (1.), and **circle lists** (○, Apple-style) that start right at the caret and continue on Enter; the three are mutually exclusive (conflicting list buttons disable automatically), and every new list keeps a plain line below it so you can always click out
- Title color per note (the left-hand notes list always stays in the default theme color)
- Markdown blocks (`{}` tool): write Markdown, render it into the note at the caret
- Tags + tag filter, full-text search (Ctrl+F), pin / favorite / duplicate / archive / soft-delete
- Image attachments: file picker, clipboard paste, drag & drop
- Debounced autosave — no save button, ever

### Reminders & Tasks
- Reminders attached to notes (or standalone) with once / daily / weekly / monthly repeats
- Native Windows toasts, with an in-app fallback when toasts are unavailable
- Tasks section with complete/delete; convert any note to a task

### Calendar
- Month / week / day / year views, Ctrl+scroll zoom, multi-day events
- **Two-way Google Calendar sync** (DST-safe recurrence handling)

### AI & Voice
- Floating AI assistant ball: agentic plan → apply actions across your notes, summarize, rephrase
- Dictation (speech-to-text) via Deepgram or AssemblyAI — streamed live into the note

### Shell & system
- Dark mode only — one polished theme, Mica title bar
- System tray: hide-to-tray, quick actions, global hotkey for a new note
- Compact mode, dock left/right, always-on-top pin, responsive single-pane layout at narrow widths
- Automatic daily database backups
- In-app auto-update from GitHub Releases

## Data & privacy

Offline-first. Everything lives **inside the application folder** (SQLite database, attachments, settings) — moving the folder moves your data. A `.portable` marker file next to the exe enables portable mode. Cloud features (Google sync, AI, dictation) are opt-in and only active when you provide API keys.

## Install

Grab `CacheNoteSetup.exe` from the [latest release](https://github.com/viseshb/CacheNote/releases/latest) and run it. The app checks for updates on launch and updates itself in one click.

## Building from source

Prereqs: Windows 11, .NET 9 SDK.

```powershell
git clone https://github.com/viseshb/CacheNote.git
cd CacheNote
dotnet build CacheNote.App\CacheNote.App.csproj -c Debug
# run the unpackaged exe:
.\CacheNote.App\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CacheNote.App.exe
```

Optional cloud features read keys from a `.env` file next to the exe (or environment variables):

| Key | Used for |
|-----|----------|
| `DEEPGRAM_API_KEY` / `ASSEMBLYAI_API_KEY` | Dictation (pick the provider in Settings) |
| `GEMINI_API_KEY` or `VERTEX_AI_API_KEY` | AI assistant (summarize / rephrase / agentic actions) |
| Google OAuth client (see Settings → Google Sync) | Two-way calendar sync |

### Tests

```powershell
dotnet test CacheNote.Tests          # unit tests (fast)
dotnet test CacheNote.UiTests        # FlaUI end-to-end tests (drives the real app)
```

UI tests launch the actual exe against an isolated temp database (`CacheNote_DATA_DIR`) — they never touch your real data.

## Architecture

| Project | What it is |
|---------|------------|
| `CacheNote.App` | WinUI 3 (unpackaged) shell, pages, tray, updater |
| `CacheNote.Core` | MVVM view models, services, SQLite data layer, cloud/AI/speech integrations |
| `CacheNote.Tests` | xUnit unit tests for Core |
| `CacheNote.UiTests` | FlaUI UIA3 end-to-end smoke + regression tests |
| `installer/` | Inno Setup script for `CacheNoteSetup.exe` |

C# 13 / .NET 9 / WinUI 3 (Windows App SDK 2.1) / CommunityToolkit.Mvvm / Microsoft.Data.Sqlite.

## Releases

Pushes to `master` run [semantic-release](.github/workflows/release.yml): Conventional Commit messages decide the version (`feat:` → minor, `fix:` → patch, `BREAKING` → major), CI builds the self-contained installer, tags, and publishes the GitHub Release that the in-app updater consumes.
