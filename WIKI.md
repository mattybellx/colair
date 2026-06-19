# 🎨 COLAIR — Self-Healing AI Shader Matte Painter

> **Version 2.0 ULTRA** | GPU-accelerated procedural art driven by LLM code generation

---

## 📋 Table of Contents

1. [What is COLAIR?](#-what-is-colair)
2. [How It Works](#-how-it-works)
3. [Architecture Overview](#-architecture-overview)
4. [Project Structure](#-project-structure)
5. [Key Concepts](#-key-concepts)
6. [File-by-File Guide](#-file-by-file-guide)
7. [The Self-Healing Loop](#-the-self-healing-loop)
8. [Data Flow](#-data-flow)
9. [Technologies Used](#-technologies-used)

---

## 🎯 What is COLAIR?

COLAIR is a **desktop application** that lets you create stunning real-time GPU graphics by simply **describing what you want in words**.

You type something like:

> *"A cosmic black hole devouring a fractal galaxy with neon purple and cyan energy tendrils"*

And COLAIR:
1. Sends your description to an AI (like OpenAI's GPT or Anthropic's Claude)
2. The AI writes a **GLSL shader** — a small program that runs on your graphics card
3. COLAIR compiles the shader on your GPU in real time
4. If the shader has errors, the AI **fixes itself** automatically
5. You can tweak the visuals with sliders and toggles — no coding required

### What's a "Shader"?

A **shader** is a tiny program that runs on your **GPU** (Graphics Processing Unit). It tells your graphics card what color to make every single pixel on the screen. Shaders are written in **GLSL** (OpenGL Shading Language), which looks like C code.

COLAIR specializes in **fragment shaders** — shaders that create stunning procedural art, 3D scenes, and visual effects entirely on the GPU.

---

## 🔄 How It Works

```
┌─────────────────────────────────────────────────────────────────────┐
│  User types: "a neon cyberpunk city"                               │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│  AI Provider (OpenAI / Anthropic / DeepSeek / etc.)                │
│  • Receives system prompt + user description                       │
│  • Generates GLSL shader code with 3D raymarching, lighting, etc.  │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│  GPU Compilation                                                   │
│  • Shader source sent to OpenGL driver                             │
│  • GPU compiles it in microseconds                                 │
│  • Returns: SUCCESS (live on screen) or FAIL (error log)           │
└──────┬──────────────────────────────────────────────────────────────┘
       │                        │
       ▼                        ▼
   ✅ SUCCESS                ❌ FAIL
   ┌──────────────┐          ┌──────────────────────────────────┐
   │ Rendered on  │          │ Error log sent BACK to AI with   │
   │ screen live! │          │ "Fix this" prompt                │
   └──────────────┘          │ AI rewrites the shader           │
                             │ Re-compile on GPU                │
                             │ Loop up to 5 times               │
                             └──────────────────────────────────┘
```

---

## 🏗 Architecture Overview

COLAIR follows the **MVVM** (Model-View-ViewModel) design pattern:

```
┌─────────────────────────────────────────────────────────────────────┐
│  VIEW (XAML) — What you see                                        │
│  • MainWindow.axaml — The entire UI layout                         │
│  • ColairTheme.axaml — Dark neon theme                             │
│  • GlShaderViewport — Custom OpenGL control                        │
│                                                                     │
│  The View "binds" to the ViewModel. It doesn't contain logic —     │
│  it just describes the UI layout and which properties to display.  │
└──────────────────────┬──────────────────────────────────────────────┘
                       │  Data Binding ({Binding PropertyName})
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│  VIEWMODEL (C#) — The brain                                        │
│  • MainWindowViewModel — Orchestrates everything                   │
│  • SettingsViewModel — Settings management                         │
│  • ProviderCardViewModel — AI provider config cards                │
│  • UniformParameterViewModel — Shader slider controls              │
│                                                                     │
│  The ViewModel holds the state and logic. When properties change,  │
│  the UI updates automatically via INotifyPropertyChanged.          │
└──────────────────────┬──────────────────────────────────────────────┘
                       │  Method calls / Events
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│  MODEL (C#) — Data & Services                                     │
│  • AppSettings, ProviderConfig — Settings data models              │
│  • LlmOrchestrationService — AI self-healing loop                  │
│  • AnthropicProvider / OpenAiCompatibleProvider — AI API clients   │
│  • SettingsService — File I/O for settings persistence             │
│  • ShaderUniformParser — Parses GLSL uniforms with regex           │
│  • ConnectionTestService — Tests API connectivity                  │
│  • ShaderProgram — Wraps compiled OpenGL shader programs           │
│  • GlShaderViewport — OpenGL rendering engine                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Directory Structure

```
ColairShaderPainter/
│
├── Program.cs                    # Entry point — where the app starts
├── App.axaml                     # Application resources and theme loading
├── App.axaml.cs                  # App setup: DI container, config, main window
├── appsettings.json              # Boot configuration (active provider, quality)
├── ColairShaderPainter.csproj    # Project file (NuGet packages, build settings)
│
├── Models/                       # Data classes (the "M" in MVVM)
│   ├── AppSettings.cs            # Root settings + default provider configs
│   ├── LlmModels.cs              # Chat API request/response DTOs
│   ├── ProviderConfig.cs         # AI provider configuration + enums
│   ├── ShaderCompilationResult.cs # GPU compilation result wrapper
│   └── UniformParameter.cs       # GLSL uniform metadata record
│
├── Exceptions/
│   └── ShaderCompilationException.cs  # Custom exception for GPU errors
│
├── Services/                     # Business logic
│   ├── ILlmService.cs            # Interface for AI orchestration
│   ├── ILlmProvider.cs           # Interface for AI providers
│   ├── LlmOrchestrationService.cs # THE self-healing loop (core AI logic)
│   ├── LlmProviderFactory.cs     # Creates the right AI provider
│   ├── AnthropicProvider.cs      # Claude API client
│   ├── OpenAiCompatibleProvider.cs # OpenAI-compatible API client
│   ├── SettingsService.cs        # Load/save settings to JSON file
│   ├── ConnectionTestService.cs  # Tests API connectivity
│   └── ShaderUniformParser.cs    # Regex-based GLSL uniform parser
│
├── Graphics/                     # GPU / OpenGL code
│   ├── GlShaderViewport.cs       # THE GPU renderer (OpenGL viewport)
│   └── ShaderProgram.cs          # Compiled GPU shader wrapper
│
├── ViewModels/                   # UI logic (the "VM" in MVVM)
│   ├── ViewModelBase.cs          # Base class with property change notification
│   ├── MainWindowViewModel.cs    # Root ViewModel (orchestrates everything)
│   ├── SettingsViewModel.cs      # Settings overlay ViewModel
│   ├── ProviderCardViewModel.cs  # AI provider card ViewModel
│   └── UniformParameterViewModel.cs # Shader uniform control ViewModel
│
├── Views/                        # UI layout (the "V" in MVVM)
│   ├── MainWindow.axaml          # Main window XAML layout
│   └── MainWindow.axaml.cs       # Code-behind (window management, shortcuts)
│
├── Assets/Styles/
│   └── ColairTheme.axaml         # Dark neon theme (colors, brushes, styles)
│
└── WIKI.md                       # This file!
```

---

## 💡 Key Concepts

### MVVM (Model-View-ViewModel)

A design pattern that separates code into three layers:

| Layer | What it does | Example |
|-------|-------------|---------|
| **View** | The UI — what users see and click | `MainWindow.axaml` |
| **ViewModel** | The logic — state, commands, data processing | `MainWindowViewModel.cs` |
| **Model** | The data — pure information, no UI | `AppSettings.cs`, `ProviderConfig.cs` |

**Why MVVM?** It makes code easier to test, maintain, and modify. You can change the UI without touching the logic, and vice versa.

### Data Binding

The XAML View binds to ViewModel properties:
```xml
<TextBlock Text="{Binding StatusMessage}" />
```

When `StatusMessage` changes in the ViewModel, the UI updates automatically. This is powered by `INotifyPropertyChanged`.

### Dependency Injection (DI)

Services are created centrally and "injected" into classes that need them. Instead of:
```csharp
// BAD: class creates its own dependencies (tight coupling)
var service = new SettingsService();
```

We do:
```csharp
// GOOD: dependencies are provided (loose coupling)
public MainWindowViewModel(ILlmService llm, SettingsService settings) { ... }
```

The DI container in `App.axaml.cs` manages all this.

### The Self-Healing Loop

COLAIR's superpower: when the AI-generated shader has errors (which happens often — AI isn't perfect at GLSL), the app automatically:

1. Captures the GPU error log (with line numbers)
2. Sends it back to the AI with: *"Fix every error above — output ONLY the corrected GLSL"*
3. The AI rewrites the shader
4. The new version is compiled again
5. Repeat up to 5 times until it works

This means the **user never sees compilation errors** — the AI fixes them silently.

### GLSL Fragment Shaders

A fragment shader is a program that runs on EVERY pixel on screen, 60+ times per second. It calculates the color of each pixel independently. This is incredibly parallel — modern GPUs can run thousands of pixel shaders simultaneously.

COLAIR uses technique called **raymarching** with **Signed Distance Fields (SDFs)** to create complex 3D scenes entirely in a fragment shader, without any 3D models or textures.

### SSAA (Super-Sampling Anti-Aliasing)

A technique for smoother edges:

- **1x (Native)**: Render once at screen resolution (fastest)
- **1.5x (HD)**: Render at 1.5x size, then scale down
- **2x (Ultra HD)**: Render at 2x size (4x pixels!), then scale down with a box filter

The downsample step averages 4 pixels into 1, giving smooth, anti-aliased edges.

---

## 📁 File-by-File Guide

### Entry Point

| File | Purpose |
|------|---------|
| `Program.cs` | The `Main()` method — where the app boots up. Sets up global crash logging and starts Avalonia. |
| `App.axaml` | Declares application resources: dark theme, Fluent theme base, custom styles. |
| `App.axaml.cs` | Builds the DI container, loads config, creates the main window and its ViewModel. |

### Models (Data)

| File | Purpose |
|------|---------|
| `AppSettings.cs` | Root settings class. Stores all provider configs, active provider, SSAA factor, retry count. Includes default provider configs for OpenAI, Anthropic, and DeepSeek. |
| `LlmModels.cs` | DTOs for the Chat Completions API. `LlmMessage` (role + content), `LlmChatRequest` (model + messages), `LlmChatResponse` (choices with messages). |
| `ProviderConfig.cs` | Defines `ProviderType` enum (OpenAI, Anthropic, OpenAiCompatible) and `ProviderConfig` class (URL, key, model, etc.). Also `ConnectionStatus` enum. |
| `ShaderCompilationResult.cs` | Result wrapper for GPU compilation — either `Ok(program)` or `Fail(errorLog)`. Used by the self-healing loop. |
| `UniformParameter.cs` | Immutable record describing one GLSL uniform variable (name, type, default values, slider range). |

### Exceptions

| File | Purpose |
|------|---------|
| `ShaderCompilationException.cs` | Thrown when GLSL compilation/linking fails. Carries the raw GPU driver error log for AI self-healing. |

### Services (Logic)

| File | Purpose |
|------|---------|
| `ILlmService.cs` | Interface for the AI orchestration service. Defines `GenerateAndCompileAsync()`. |
| `ILlmProvider.cs` | Interface for AI providers. Defines `CompleteChatAsync()`. |
| `LlmOrchestrationService.cs` | **THE CORE**. Implements the self-healing loop: call AI → extract code → compile on GPU → if fail, send error back to AI → repeat. |
| `LlmProviderFactory.cs` | Factory that creates the right provider (Anthropic or OpenAI-compatible) based on settings. |
| `AnthropicProvider.cs` | Anthropic Claude API client. Uses `/v1/messages` endpoint with `x-api-key` auth. |
| `OpenAiCompatibleProvider.cs` | Universal adapter for any OpenAI-compatible API (OpenAI, DeepSeek, Groq, Ollama, Azure OpenAI, etc.). |
| `SettingsService.cs` | Loads/saves settings to `%APPDATA%\Colair\settings.json`. |
| `ConnectionTestService.cs` | Tests API connectivity with lightweight requests. |
| `ShaderUniformParser.cs` | Uses regex to find `uniform` declarations in GLSL source and create `UniformParameter` records. |

### Graphics (GPU)

| File | Purpose |
|------|---------|
| `GlShaderViewport.cs` | The OpenGL viewport control. Drives the render loop, compiles shaders, handles SSAA, cross-fades between shaders, and accepts real-time uniform updates. This is the most complex file in the project. |
| `ShaderProgram.cs` | Wraps a compiled GPU shader program. Compiles GLSL source, links programs, caches uniform locations, and provides typed `SetUniform()` methods. |

### ViewModels (UI Logic)

| File | Purpose |
|------|---------|
| `ViewModelBase.cs` | Base class with `SetProperty<T>()` and `OnPropertyChanged()` — the foundation of MVVM data binding. |
| `MainWindowViewModel.cs` | Root ViewModel. Owns the prompt text, generation state, status messages, zoom, SSAA quality, uniform parameters, and settings overlay. Also contains `RelayCommand` implementations. |
| `SettingsViewModel.cs` | Settings overlay ViewModel. Manages provider cards, active provider, SSAA factor, and tab navigation. |
| `ProviderCardViewModel.cs` | ViewModel for one AI provider card. Manages API key field, model selector, connection test, and status display. |
| `UniformParameterViewModel.cs` | ViewModel for one shader uniform control. Maps GLSL types to UI controls (slider, RGB sliders, toggle). |

### Views (UI Layout)

| File | Purpose |
|------|---------|
| `MainWindow.axaml` | The entire main window layout in XAML. 4-row grid with title bar, toolbar, GPU viewport + parameter panel, and prompt bar. Also includes settings overlay and AI-tuning progress overlay. |
| `MainWindow.axaml.cs` | Code-behind: window controls (min/close/fullscreen), keyboard shortcuts (Space to generate), title bar dragging, settings tab wiring. |

### Styles

| File | Purpose |
|------|---------|
| `ColairTheme.axaml` | Dark neon "glassmorphism" theme. Defines the color palette (deep navy, violet accent, cyan secondary), gradient brushes, and control styles (buttons, text boxes, sliders, etc.). |

---

## 🔁 The Self-Healing Loop (Detailed)

This is the most important concept in COLAIR. Here's exactly how it works:

```
START: User clicks "GENERATE"
  │
  ▼
1. Sync settings (latest API key, model, provider)
  │
  ▼
2. Build system prompt (inject user description into engine template)
  │
  ▼
3. Send to AI provider (OpenAI/Anthropic/DeepSeek via HTTP)
  │
  ▼
4. Receive AI response (raw text with GLSL code)
  │
  ▼
5. Extract GLSL code (strip markdown fences like ```glsl)
  │
  ▼
6. Send to GPU for compilation
  │
  ├── ✅ SUCCESS → Display shader, parse uniforms, populate sliders → DONE
  │
  └── ❌ FAIL → 
       │
       ▼
      7. Read GPU error log (line numbers + error messages)
       │
       ▼
      8. Build "fix prompt" containing the error log
       │
       ▼
      9. Send fix prompt back to AI (same conversation)
       │
       ▼
      10. AI rewrites the shader with corrections
       │
       ▼
      11. Go back to step 5 (extract → compile)
       │
       Loop up to 5 times (configurable in settings)
       │
       If ALL attempts fail → show error message to user
```

This loop is implemented in `LlmOrchestrationService.cs`.

---

## 📊 Data Flow

### Generation Flow

```
User prompt text
    │
    ▼
MainWindowViewModel.OnGenerateOrCancelAsync()
    │
    ▼
LlmOrchestrationService.GenerateAndCompileAsync()
    │
    ├──► LlmProviderFactory.GetCurrentProvider()
    │       │
    │       ├──► AnthropicProvider.CompleteChatAsync()  (if Anthropic selected)
    │       └──► OpenAiCompatibleProvider.CompleteChatAsync()  (everything else)
    │
    ▼
Extract GLSL code
    │
    ▼
GlShaderViewport.CompileShaderAsync(source)  ← goes to GPU
    │
    ├──► SUCCESS → ShaderCompiled event fires
    │                │
    │                ▼
    │           ShaderUniformParser.Parse(source)
    │                │
    │                ▼
    │           UniformParameterViewModel instances created
    │                │
    │                ▼
    │           UI displays sliders/toggles for each uniform
    │
    └──► FAIL → error log sent back to AI
```

### Settings Flow

```
User opens settings overlay
    │
    ▼
SettingsViewModel.RefreshFromDisk()  ← reads %APPDATA%\Colair\settings.json
    │
    ▼
ProviderCardViewModels created (one per provider)
    │
    ▼
User edits API keys, selects models, tests connections
    │
    ▼
User clicks "Save & Close"
    │
    ▼
SettingsViewModel.SyncNow()  ← writes all changes to disk
    │
    ▼
SettingsService.Save()  ← writes JSON to %APPDATA%\Colair\settings.json
```

### GPU Render Flow (every frame ≈ 16ms at 60fps)

```
GlShaderViewport.OnOpenGlRender()
    │
    ▼
Check viewport size (recreate FBOs if window was resized)
    │
    ▼
Process pending shader compilation (if any)
    │
    ▼
Decide render path:
    │
    ├──► SSAA enabled (factor > 1.0):
    │       Render to high-res FBO → downsample with box filter → display
    │
    ├──► Cross-fade active:
    │       Render old shader to FBO A
    │       Render new shader to FBO B
    │       Composite A + B with alpha blend → display
    │
    └──► Normal:
        Render current shader directly to screen framebuffer
    │
    ▼
Request next frame (RequestNextFrameRendering)
```

---

## 🛠 Technologies Used

| Technology | Purpose | Why |
|-----------|---------|-----|
| **.NET 9** | Runtime & framework | Latest .NET — fast, cross-platform, modern C# features |
| **Avalonia UI 11** | Desktop UI framework | Cross-platform (Windows/Mac/Linux), XAML-based, open-source WPF-like |
| **Silk.NET OpenGL 2.22** | GPU bindings | Modern .NET OpenGL bindings — lets C# talk directly to the GPU |
| **OpenGL 3.3 Core** | Graphics API | Cross-platform GPU programming for real-time rendering |
| **Microsoft.Extensions.DI** | Dependency Injection | Standard .NET DI container for loose coupling |
| **System.Text.Json** | JSON serialization | Built-in .NET JSON — used for settings persistence |
| **OpenAI API** | AI provider | `gpt-4o`, `gpt-4o-mini`, etc. |
| **Anthropic API** | AI provider | `claude-opus-4-5`, `claude-sonnet-4-5`, etc. |
| **DeepSeek API** | AI provider | `deepseek-chat`, `deepseek-reasoner` |

### GLSL Techniques Used in Generated Shaders

- **Raymarching**: A technique for rendering 3D scenes using Signed Distance Fields
- **Signed Distance Fields (SDFs)**: Mathematical functions that define 3D shapes
- **fBm Noise**: Fractal Brownian Motion — layered noise for organic textures
- **Domain Warping**: Distorting space with noise for complex, organic forms
- **Gyroid Surfaces**: Triply periodic minimal surfaces — beautiful mathematical shapes
- **Smooth Minimum/Maximum**: Blending SDFs together smoothly
- **Ambient Occlusion**: Depth-based shadowing for realism
- **Soft Shadows**: Approximate shadow calculations for SDF scenes
- **Chromatic Aberration**: Lens-like color fringing effect
- **Vignette**: Darker corners of the screen
- **ACES Filmic Tone Mapping**: Cinematic color grading
- **Gamma Correction**: Standard 2.2 gamma for proper color display

---

## 🚀 Getting Started

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- An API key from one of: OpenAI, Anthropic, or DeepSeek

### Running
```bash
cd ColairShaderPainter
dotnet run
```

### First Use
1. Launch the app — you'll see the default "Wake" shader (a beautiful animated gyroid)
2. Click the ⚙ Settings button (top-right of toolbar)
3. Enter your API key for your preferred AI provider
4. Click "Test" to verify the connection
5. Click "Save & Close"
6. Type a visual concept in the prompt bar (e.g., "a neon cyberpunk city with volumetric fog")
7. Click "⚡ GENERATE" or press Space bar

---

## 🤝 Contributing

The code is fully documented with beginner-friendly comments. Start with `Program.cs` to understand the entry point, then follow the data flow through `App.axaml.cs` → `MainWindowViewModel.cs` → `LlmOrchestrationService.cs` → `GlShaderViewport.cs`.

---

*Created with 💜 by Colair AI*
