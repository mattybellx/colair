<div align="center">

# 🎨 COLAIR — Self-Healing AI Shader Painter

### *"The world's simplest way to paint with AI on a GPU."*

[![C#](https://img.shields.io/badge/C%23-12.0-%23512BD4?logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![.NET](https://img.shields.io/badge/.NET-9.0-%23512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.2.7-%238B5CF6?logo=avaloniaui&logoColor=white)](https://www.avaloniaui.net/)
[![OpenGL](https://img.shields.io/badge/OpenGL-4.0-%235586A4?logo=opengl&logoColor=white)](https://www.opengl.org/)
[![License](https://img.shields.io/badge/license-MIT-%23F59E0B)](LICENSE)

*Built with ❤️ as a C# learning project — contributions, ideas, and help are always welcome!*

---

</div>

## ✨ What Is This?

**COLAIR** is a desktop app where you **describe a visual concept in plain English**, and an AI writes a GLSL shader (a tiny GPU program) that brings it to life in real-time — right on your graphics card.

> **Think of it like this:** You say "a glowing nebula with floating crystal shapes," and COLAIR sends that to an AI, which writes the GPU code, compiles it, and starts rendering it on screen — all in one click. If the AI's code has bugs (which it often does — shaders are hard!), COLAIR automatically sends the error back to the AI to **heal itself** and try again.

### 🎯 The Big Idea (Simply Put)

**You don't need to know shader programming to make GPU art.** COLAIR is the bridge between human creativity and GPU performance. Type what you see in your imagination, and let the AI + GPU partnership do the rest.

---

## 🚀 Features

| Feature | What It Does |
|---------|-------------|
| **🧠 Self-Healing AI** | AI writes shader code → GPU compiles → if it fails, AI fixes itself and retries (up to 5 times) |
| **🎮 Live GPU Rendering** | 60 FPS OpenGL viewport — the shader runs directly on your graphics card |
| **🔧 Real-Time Controls** | Sliders and toggles appear automatically for any AI-generated parameters |
| **🔄 Smooth Cross-Fades** | Shaders transition with a 0.55s GPU blend — no jarring cuts |
| **✨ SSAA Anti-Aliasing** | Super-sampling (1x/1.5x/2x) for crisp, smooth edges |
| **🔌 Multi-LLM Support** | Works with **OpenAI**, **Anthropic Claude**, **DeepSeek**, or any OpenAI-compatible API (local models too!) |
| **🌙 Glassmorphism UI** | Dark-themed, modern Avalonia interface with custom chrome |
| **🔧 Self-Healing Loop** | Up to 5 automatic retries when the AI generates buggy shader code |

---

## 🖼️ How It Works

```
┌─────────────┐     ┌──────────────┐     ┌─────────────┐     ┌───────────┐
│  You type:   │ ──→ │   AI sends   │ ──→ │   GPU       │ ──→ │  Live on  │
│  "cosmic     │     │   GLSL code  │     │  compiles   │     │  screen!  │
│  black hole" │     │              │     │  & renders  │     │           │
└─────────────┘     └──────────────┘     └─────────────┘     └───────────┘
                            │                    │
                            ↓                    ↓
                      ┌──────────────┐     ┌─────────────┐
                      │  ❌ Error?   │ ──→ │  AI reads   │
                      │  GPU says no │     │  error log  │
                      └──────────────┘     │  & rewrites │
                            │              └─────────────┘
                            ↓  (up to 5x)
                      ┌──────────────┐
                      │  ✅ Success! │
                      │  Auto-healed │
                      └──────────────┘
```

---

## 🛠️ Tech Stack

| Technology | Why? |
|-----------|------|
| **C# 12 / .NET 9** | Modern, fast, cross-platform desktop development |
| **Avalonia UI 11** | Cross-platform UI framework (Windows, macOS, Linux) — like WPF but better |
| **Silk.NET OpenGL** | High-performance OpenGL bindings for .NET — talks directly to your GPU |
| **Microsoft.Extensions.DI** | Dependency injection for clean, testable architecture |
| **OpenAI / Anthropic APIs** | AI code generation with self-healing feedback loop |

---

## 🏗️ Project Structure

```
ColairShaderPainter/
├── App.axaml / .cs        # App entry point, DI setup, theme loading
├── Program.cs              # Main() — crash handler, Avalonia bootstrap
├── Assets/
│   └── Styles/
│       └── ColairTheme.axaml  # Full dark neon glassmorphism theme
├── Graphics/
│   ├── GlShaderViewport.cs   # THE CORE — OpenGL render loop, SSAA, cross-fades
│   └── ShaderProgram.cs      # GPU shader compilation wrapper
├── Models/
│   ├── AppSettings.cs        # JSON-serialized app config
│   ├── ShaderCompilationResult.cs  # Success/failure envelope
│   └── UniformParameter.cs   # GLSL uniform descriptor
├── Services/
│   ├── LlmOrchestrationService.cs  # Self-healing AI loop ⭐
│   ├── AnthropicProvider.cs        # Claude API adapter
│   ├── OpenAiCompatibleProvider.cs # Universal OpenAI-like API adapter
│   └── SettingsService.cs          # Persistence layer
├── ViewModels/              # MVVM — the brains behind the UI
│   ├── MainWindowViewModel.cs
│   ├── SettingsViewModel.cs
│   └── UniformParameterViewModel.cs
└── Views/
    ├── MainWindow.axaml     # Full UI layout (XAML)
    └── MainWindow.axaml.cs  # Code-behind (window chrome, keyboard shortcuts)
```

---

## 🚦 Getting Started

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- A GPU with OpenGL 3.3+ support (basically anything from the last 15 years)
- An API key from OpenAI, Anthropic, or DeepSeek

### Quick Start
```bash
# Clone
git clone https://github.com/mattybellx/colair.git
cd colair

# Build
dotnet build

# Run
dotnet run
```

### First Launch
1. Click the **⚙ Settings** button in the toolbar
2. Paste your **API key** into one of the provider cards
3. Click **🔗 Test** to verify the connection
4. Type a visual concept in the prompt bar (e.g. *"a neon cityscape with floating geometric islands"*)
5. Click **⚡ GENERATE** and watch the AI create your shader!

---

## ⚠️ Honest State of the Project

> **This is a learning project!** I'm building this to explore C#, Avalonia UI, OpenGL interop, and AI code generation. It's functional and pretty (IMHO), but it's not production-grade.

### ✅ What Works Well
- Self-healing AI loop (compiles, catches errors, retries)
- Live GPU rendering at 60 FPS with smooth cross-fades
- Real-time uniform control panel (sliders auto-generated from GLSL)
- SSAA anti-aliasing (1x/1.5x/2x)
- Multi-provider support (OpenAI, Anthropic, DeepSeek, local models)
- Connection testing for each provider
- Settings persistence across restarts

### 🔄 What's in Progress
- More robust error recovery edge cases
- Shader history / gallery
- Export rendered shaders as videos

### 🤝 How You Can Help
- **Try it!** — Launch the app, generate shaders, and [open an issue](https://github.com/mattybellx/colair/issues) if something breaks
- **Suggest features** — What would make this more useful or fun?
- **Fix bugs** — PRs are warmly welcomed
- **Improve the AI prompts** — The system prompt that tells the AI how to write shaders could always be better
- **Add more AI providers** — Google Gemini, Groq, Together AI, etc.

---

## 📸 Default "Wake" Shader

When you first launch COLAIR, you're greeted by a procedural 3D raymarched gyroid sculpture — mathematically beautiful organic surfaces with volumetric lighting, nebula background, soft shadows, and cinematic ACES tonemapping. It's the app's way of saying *"Your GPU is ready. Let's make art."*

The default shader has 5 adjustable parameters that appear in the side panel:
- **Mutation Speed** — Animation speed
- **Primary Glow** — Main color (RGB)
- **Secondary Glow** — Rim/edge color (RGB)
- **Bloom Intensity** — Glow brightness
- **Noise Scale** — Texture detail level

---

## 📜 License

MIT — do what you want with it. If you build something cool, I'd love to hear about it!

---

<div align="center">

### 🌟 Made with curiosity, C#, and caffeine

*"The best way to learn is to build something that feels just out of reach."*

⭐ Star if you like it! PRs, issues, and ideas always welcome.

</div>
