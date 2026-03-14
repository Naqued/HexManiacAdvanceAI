# HexManiacAdvance - Claude Code Guide

## Build & Test

```bash
# .NET 6 SDK is installed at ~/.dotnet (add to PATH if needed)
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

# Build Core (works on Linux)
dotnet build src/HexManiac.Core/HexManiac.Core.csproj

# Build WPF (requires Windows, or use EnableWindowsTargeting on Linux)
dotnet build src/HexManiac.WPF/HexManiac.WPF.csproj -p:EnableWindowsTargeting=true

# Build entire solution (Windows)
dotnet build HexManiacAdvance.sln

# Run tests
dotnet test HexManiacAdvance.sln

# Run WPF app (Windows only)
dotnet run --project src/HexManiac.WPF/HexManiac.WPF.csproj
```

## Architecture

**Hexagonal (Ports & Adapters) pattern:**

- **Core** (`src/HexManiac.Core/`) — All business logic, ViewModels, interfaces (ports). Zero platform/UI dependencies.
- **WPF** (`src/HexManiac.WPF/`) — XAML views, platform adapters (e.g. `WindowsFileSystem`). Depends on Core.
- **Tests** (`src/HexManiac.Tests/`) — Unit tests for Core logic.
- **Integration** (`src/HexManiac.Integration/`) — Integration tests.

**Key interfaces (ports):**
- `IFileSystem` — file I/O abstraction (Core defines, WPF implements)
- `IWorkDispatcher` — thread dispatch abstraction
- `IDataModel` — ROM data access
- `IEditableViewPort` — editable tab with selection, tools, history

**ViewModel pattern:**
- All ViewModels extend `ViewModelCore` (provides `Set()`, `TryUpdate()`, `NotifyPropertyChanged()`, `StubCommand()`)
- Commands use `StubCommand` (auto-generated ICommand impl with `CanExecute`/`Execute` Func/Action)
- Editor-level tools (PythonTool, AiTool) are owned by `EditorViewModel`, not per-tab ToolTray
- Per-tab tools (CodeTool, TableTool, SpriteTool) live in `IToolTrayViewModel`

## Project Layout

```
src/HexManiac.Core/
  Models/           — Data model, runs, ROM structure
  Models/Code/      — Script parsers, reference files (.txt, .toml)
  ViewModels/       — All ViewModels (EditorViewModel, ViewPort, etc.)
  ViewModels/Tools/ — Tool ViewModels (CodeTool, TableTool, PythonTool)
  ViewModels/AI/    — AI assistant feature (ILlmProvider, AiToolViewModel, etc.)

src/HexManiac.WPF/
  Windows/          — MainWindow.xaml
  Controls/         — PythonPanel, AiPanel, TextEditor, etc.
  Implementations/  — WindowsFileSystem and other adapters
  Resources/        — Icons, markup extensions
```

## Key Conventions

- Target: .NET 6.0, C# nullable annotations enabled
- No new NuGet packages without strong justification (uses only Crc32.NET, IronPython, DynamicLanguageRuntime)
- All data modifications go through `ModelDelta` via `ChangeHistory.CurrentChange` for undo/redo support
- Settings stored via `IFileSystem.MetadataFor()` / `IFileSystem.SaveMetadata()`, not direct file I/O
- WPF bindings use `{hmar:MethodCommand MethodName}` markup extension for void method binding
- Panel toggling pattern: `bool ShowXPanel` + `ICommand ToggleShowXPanelCommand` on EditorViewModel, visibility managed in MainWindow.xaml.cs `ViewModelPropertyChanged`
