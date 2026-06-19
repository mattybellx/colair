using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows.Input;
using ColairShaderPainter.Graphics;
using ColairShaderPainter.Models;
using ColairShaderPainter.Services;

namespace ColairShaderPainter.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════════
// MainWindowViewModel.cs
//
// THE ROOT VIEWMODEL — the nerve centre of the entire application.
//
// This ViewModel is the data provider for the main window. It owns:
//
//   🎨 AI Pipeline:
//     - Takes the user's text prompt
//     - Sends it to LlmOrchestrationService for AI generation
//     - Hands the compiled shader to the GPU viewport
//     - Handles self-healing retries and cancellation
//
//   🎛️ UI State:
//     - UserPrompt (the text the user types)
//     - IsGenerating (show/hide the progress overlay)
//     - StatusMessage (what's happening right now)
//     - ZoomFactor, QualityIndex (viewport controls)
//     - IsSettingsOpen (show/hide settings overlay)
//
//   🎚️ Uniform Parameters:
//     - Dynamically populates UniformParameters from parsed shader uniforms
//     - Forwards slider/toggle changes to the GPU viewport in real time
//
// This class also contains the RelayCommand implementations at the bottom
// of the file (minimal ICommand implementations to avoid external dependencies).
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Root ViewModel for the main window — the "brain" behind the UI.
///
/// Coordinates the entire AI → GPU pipeline:
///   1. User types a prompt → UserPrompt property
///   2. User clicks Generate → GenerateCommand executes
///   3. LlmOrchestrationService generates + self-heals the shader
///   4. Compiled shader goes to GlShaderViewport for rendering
///   5. Shader uniforms are parsed → UniformParameters populated
///   6. User adjusts sliders → values pushed to GPU in real time
///
/// This is a transient ViewModel (created once per window), but most of
/// its dependencies (SettingsService, LlmOrchestrationService) are singletons.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    // ── Injected services ─────────────────────────────────────────────────────
    private readonly ILlmService          _llm;             // AI orchestration
    private readonly ShaderUniformParser  _parser;          // Parses GLSL uniforms
    private readonly SettingsService      _settingsService; // App settings persistence
    private readonly SettingsViewModel    _settingsVm;      // Settings overlay VM
    private CancellationTokenSource?     _generationCts;    // For cancelling in-flight generation

    // ── Viewport reference (set from View code-behind after DataContext is wired) ──
    private GlShaderViewport? _viewport;

    // ── Bindable state ────────────────────────────────────────────────────────

    private string _userPrompt = string.Empty;

    /// <summary>The text the user types in the prompt box — their visual concept description</summary>
    public string UserPrompt
    {
        get => _userPrompt;
        set => SetProperty(ref _userPrompt, value);
    }

    private bool _isGenerating;

    /// <summary>True while the AI is generating or compiling a shader</summary>
    public bool IsGenerating
    {
        get => _isGenerating;
        set
        {
            if (!SetProperty(ref _isGenerating, value)) return;
            OnPropertyChanged(nameof(GenerateButtonText));
            OnPropertyChanged(nameof(CanGenerate));
        }
    }

    private string _statusMessage = "● Ready";

    /// <summary>Status text shown in the bottom bar (e.g. "Compiling on GPU...")</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _hasError;

    /// <summary>True if the last generation resulted in an error</summary>
    public bool HasError
    {
        get => _hasError;
        set
        {
            if (!SetProperty(ref _hasError, value)) return;
            OnPropertyChanged(nameof(HasNoError));
        }
    }

    /// <summary>Inverse of HasError — used for XAML binding (green dot visible when no error)</summary>
    public bool HasNoError => !_hasError;

    /// <summary>Text for the Generate/Cancel button (changes based on state)</summary>
    public string GenerateButtonText => IsGenerating ? "CANCEL" : "⚡  GENERATE";

    /// <summary>
    /// Always true — the button is always clickable.
    /// Clicking during idle starts generation; clicking during generation cancels it.
    /// This is intentional: a single button handles both actions.
    /// </summary>
    public bool   CanGenerate        => true;

    /// <summary>
    /// Dynamically populated collection of uniform controls.
    /// Each item represents one AI-declared GLSL uniform and drives a slider/toggle/color picker.
    /// </summary>
    public ObservableCollection<UniformParameterViewModel> UniformParameters { get; } = [];

    /// <summary>
    /// True when there are no parameters to show (first launch, before generating).
    /// Used by the XAML empty-state hint visibility binding.
    /// Updated whenever UniformParameters collection changes.
    /// </summary>
    public bool HasNoParameters => UniformParameters.Count == 0;

    // ── Settings overlay ──────────────────────────────────────────────────────

    private bool _isSettingsOpen;

    /// <summary>True when the settings overlay is visible</summary>
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (!SetProperty(ref _isSettingsOpen, value)) return;
            OnPropertyChanged(nameof(IsSettingsOpen));
        }
    }

    /// <summary>Exposes the SettingsViewModel for data binding (nested property access in XAML)</summary>
    public SettingsViewModel Settings => _settingsVm;

    // ── SSAA quality ──────────────────────────────────────────────────────────

    private float _ssaaFactor = 1.0f;

    /// <summary>SSAA factor — changes take effect immediately on the GPU</summary>
    public float SsaaFactor
    {
        get => _ssaaFactor;
        set
        {
            if (!SetProperty(ref _ssaaFactor, value)) return;
            _viewport?.SetSsaaFactor(value);
            _settingsService.Current.SsaaFactor = value;  // Keep in sync
        }
    }

    /// <summary>Human-readable SSAA quality label</summary>
    public string SsaaLabel => _ssaaFactor switch
    {
        1.0f => "Native",
        1.5f => "HD (1.5x)",
        2.0f => "Ultra HD (2x)",
        _    => $"{_ssaaFactor:F1}x"
    };

    /// <summary>
    /// Quality combo box selected index: 0 = Native (1x), 1 = HD (1.5x), 2 = Ultra HD (2x)
    /// Maps between the combo index and the actual float SSAA factor.
    /// </summary>
    public int QualityIndex
    {
        get => _ssaaFactor switch { 1.0f => 0, 1.5f => 1, 2.0f => 2, _ => 1 };
        set
        {
            SsaaFactor = value switch { 0 => 1.0f, 1 => 1.5f, 2 => 2.0f, _ => 1.5f };
            OnPropertyChanged();
        }
    }

    // ── Zoom ─────────────────────────────────────────────────────────────────
    private float _zoomFactor = 1.0f;

    /// <summary>
    /// Zoom factor (0.5x to 5.0x).
    /// Lower bound is 0.5 to prevent the shader's raymarcher from producing
    /// NaN ray directions at wide angles (which causes black flickering
    /// pixels at viewport edges when zoomed out very far).
    /// Changes are pushed to the GPU in real time.
    /// </summary>
    public float ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            if (!SetProperty(ref _zoomFactor, Math.Clamp(value, 0.5f, 5.0f))) return;
            _viewport?.SetZoom(_zoomFactor);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Starts generation or cancels in-flight generation</summary>
    public ICommand GenerateCommand      { get; }

    /// <summary>Opens the settings overlay</summary>
    public ICommand OpenSettingsCommand  { get; }

    /// <summary>Closes the settings overlay</summary>
    public ICommand CloseSettingsCommand { get; }

    /// <summary>Sets the SSAA factor to a specific value</summary>
    public ICommand SetSsaaCommand       { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the main ViewModel with all its injected dependencies.
    ///
    /// Dependency Injection (DI) means the services are created elsewhere
    /// (in App.axaml.cs) and "injected" into this constructor. This keeps
    /// the ViewModel focused on orchestrating the UI, not on creating services.
    /// </summary>
    public MainWindowViewModel(
        ILlmService llm,                    // AI orchestration engine
        ShaderUniformParser parser,          // Parses GLSL uniforms
        SettingsService settingsService,     // Settings persistence
        SettingsViewModel settingsVm)        // Settings overlay VM
    {
        _llm             = llm;
        _parser          = parser;
        _settingsService = settingsService;
        _settingsVm      = settingsVm;

        _ssaaFactor = settingsService.Current.SsaaFactor;

        // When the settings save event fires, close the overlay
        _settingsVm.CloseRequested += () => IsSettingsOpen = false;

        // Wire commands to their handler methods
        GenerateCommand      = new RelayCommand(OnGenerateOrCancelAsync, () => true);
        OpenSettingsCommand  = new RelayCommand(OpenSettings, () => true);
        CloseSettingsCommand = new RelayCommand(CloseSettings, () => true);
        SetSsaaCommand       = new RelayCommand<float>(f => SsaaFactor = f);

        // Track collection changes so the empty-state hint visibility updates
        UniformParameters.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasNoParameters));
    }

    /// <summary>
    /// Opens the settings overlay and refreshes from disk (picks up any
    /// changes made externally).
    /// </summary>
    private void OpenSettings()
    {
        _settingsVm.RefreshFromDisk();
        IsSettingsOpen = true;
    }

    /// <summary>
    /// Closes the settings overlay and reloads settings from persisted state.
    /// If the user didn't click "Save & Close", any changes are discarded.
    /// </summary>
    private void CloseSettings()
    {
        IsSettingsOpen = false;
        // Reload settings from disk (discards any unsaved changes)
        _settingsService.Load();
        _ssaaFactor = _settingsService.Current.SsaaFactor;
        OnPropertyChanged(nameof(SsaaFactor));
        OnPropertyChanged(nameof(SsaaLabel));
        _viewport?.SetSsaaFactor(_ssaaFactor);
    }

    /// <summary>
    /// Syncs the latest provider configs from the SettingsViewModel into the
    /// SettingsService. This is called BEFORE generation starts to make sure
    /// the API call uses whatever the user typed in the settings fields —
    /// even if they didn't click "Save & Close".
    /// </summary>
    private void SyncSettingsFromVm()
    {
        _settingsVm.SyncNow();
    }

    // ── Viewport wiring (called from MainWindow.axaml.cs) ────────────────────

    /// <summary>
    /// Connects the ViewModel to the live GPU viewport.
    ///
    /// This can't be done in the constructor because the viewport control
    /// hasn't been created yet when the ViewModel is first constructed.
    /// Instead, MainWindow.axaml.cs calls this after the window loads.
    ///
    /// Also subscribes to the ShaderCompiled event so the uniform panel
    /// is automatically populated whenever a new shader compiles.
    /// </summary>
    public void AttachViewport(GlShaderViewport viewport)
    {
        _viewport = viewport;

        // When a shader compiles, parse its uniforms and create UI controls
        _viewport.ShaderCompiled += OnShaderCompiled;
    }

    // ── Private logic ─────────────────────────────────────────────────────────

    /// <summary>
    /// Main handler for the Generate/Cancel button.
    ///
    /// If a generation is already in progress, this cancels it.
    /// Otherwise, it starts a new generation:
    ///   1. Validates input (prompt not empty, viewport ready)
    ///   2. Syncs latest settings from the UI
    ///   3. Calls the AI orchestration service
    ///   4. The AI service handles the self-healing loop internally
    ///   5. Updates status messages throughout
    /// </summary>
    private async void OnGenerateOrCancelAsync()
    {
        // ── Cancel in-flight generation ──────────────────────────────────────
        if (IsGenerating)
        {
            _generationCts?.Cancel();
            return;
        }

        // ── Validate ─────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(UserPrompt))
        {
            StatusMessage = "⚠️ Enter a visual concept first.";
            return;
        }

        if (_viewport is null)
        {
            StatusMessage = "❌ Viewport not ready.";
            return;
        }

        // ── Start generation ─────────────────────────────────────────────────
        _generationCts?.Dispose();
        _generationCts = new CancellationTokenSource();
        var ct = _generationCts.Token;

        HasError      = false;
        IsGenerating  = true;
        StatusMessage = "⚡ Initialising AI Shader Engine...";

        try
        {
            // Sync any unsaved settings so the API call uses the latest key & provider
            SyncSettingsFromVm();

            // Capture for the lambda (prevents closure issues)
            var vp = _viewport;

            // This is the big async call — runs the self-healing loop
            string? compiledSource = await _llm.GenerateAndCompileAsync(
                userPrompt:    UserPrompt,
                compileShader: src => vp.CompileShaderAsync(src),
                onStatusUpdate: msg =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusMessage = msg),
                cancellationToken: ct);

            if (compiledSource is null)
            {
                HasError      = true;
                StatusMessage = "❌ All self-healing retries exhausted — check LLM settings.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⛔ Generation cancelled.";
        }
        catch (Exception ex)
        {
            HasError      = true;
            StatusMessage = $"❌ Unexpected error: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>
    /// Called when the GPU viewport finishes compiling a shader.
    /// Parses the GLSL source to find uniform declarations and populates
    /// the UI controls in the parameters panel.
    ///
    /// The actual parsing (regex work) happens on a background thread,
    /// then the UI update is dispatched to the UI thread.
    /// </summary>
    private void OnShaderCompiled(string shaderSource)
    {
        try
        {
            // Parse uniforms (regex is done here, on the background)
            var uniforms = _parser.Parse(shaderSource);
            // Update UI on the UI thread (required by Avalonia)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { UpdateUniformPanel(uniforms); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Uniform panel error: {ex.Message}"); }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shader parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuilds the uniform controls panel from a list of parsed uniforms.
    ///
    /// 1. Unsubscribes old change handlers (cleanup)
    /// 2. Clears the current collection
    /// 3. Creates a new UniformParameterViewModel for each uniform
    /// 4. Subscribes to value changes (to push to GPU)
    /// 5. Pushes the initial values to the GPU
    /// 6. Adds to the ObservableCollection (triggers UI update)
    /// </summary>
    private void UpdateUniformPanel(IReadOnlyList<UniformParameter> uniforms)
    {
        // Unsubscribe old uniform change handlers to prevent memory leaks
        foreach (var old in UniformParameters)
            old.ValueChanged -= OnUniformChanged;

        UniformParameters.Clear();

        foreach (var param in uniforms)
        {
            var vm = new UniformParameterViewModel(param);
            vm.ValueChanged += OnUniformChanged;  // React to slider changes
            PushUniform(vm);                       // Send initial value to GPU
            UniformParameters.Add(vm);             // Show in the UI
        }
    }

    /// <summary>
    /// Handles uniform value changes from the UI (when the user drags a slider,
    /// toggles a switch, or adjusts a color picker).
    ///
    /// Pushes the new value to the GPU viewport in real time.
    /// PushUniform only acquires _uniformLock briefly to write to the
    /// dictionary — the render thread's lock is equally brief, so there
    /// is minimal contention.
    /// </summary>
    private void OnUniformChanged(object? sender, EventArgs _)
    {
        try
        {
            if (sender is UniformParameterViewModel vm)
                PushUniform(vm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Uniform update error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a single uniform value from the ViewModel to the GPU viewport.
    ///
    /// Routes the value based on the uniform type:
    ///   - Float/Int → single float value
    ///   - Bool → 1.0 (true) or 0.0 (false)
    ///   - Vec3 → RGB color vector
    ///   - Vec2 → 2D coordinate vector
    /// </summary>
    private void PushUniform(UniformParameterViewModel vm)
    {
        if (_viewport is null) return;

        try
        {
            switch (vm.Type)
            {
                case Models.UniformType.Float:
                case Models.UniformType.Int:
                    _viewport.SetUniform(vm.Name, vm.FloatValue);
                    break;

                case Models.UniformType.Bool:
                    _viewport.SetUniform(vm.Name, vm.BoolValue ? 1f : 0f);
                    break;

                case Models.UniformType.Vec3:
                    _viewport.SetUniform(vm.Name, vm.Vec3Value);
                    break;

                case Models.UniformType.Vec2:
                    _viewport.SetUniform(vm.Name, new Vector2(vm.R, vm.G));
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PushUniform error ({vm.Name}): {ex.Message}");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// RelayCommand implementations
//
// ICommand is the standard .NET interface for binding button clicks and other
// actions from the View to the ViewModel. Avalonia's Button.Command property
// binds to an ICommand property on the ViewModel.
//
// Normally you'd use a library like CommunityToolkit.Mvvm or ReactiveUI for
// these, but we implement them manually to keep dependencies minimal.
//
// RelayCommand (no generic): For commands with no parameter
// RelayCommand<T> (generic): For commands with a typed parameter
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A simple ICommand implementation that delegates to a provided Action.
/// No parameter version (e.g., button click that doesn't need data).
///
/// Usage: new RelayCommand(() => DoSomething(), () => CanDoIt)
/// </summary>
/// <param name="execute">The action to run when the command is executed</param>
/// <param name="canExecute">Returns true if the command can run right now</param>
internal sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter)    => execute();
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A generic ICommand implementation with a typed parameter.
/// Used for commands that pass data (e.g., a slider value or selected item).
///
/// Usage: new RelayCommand<float>(f => SetValue(f))
/// </summary>
/// <typeparam name="T">The type of parameter the command accepts</typeparam>
/// <param name="execute">The action to run with the parameter</param>
/// <param name="canExecute">Optional function that returns true if the command can run</param>
internal sealed class RelayCommand<T>(Action<T> execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)
    {
        if (parameter is T value) execute(value);
    }
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
}
