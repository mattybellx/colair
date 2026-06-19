using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ColairShaderPainter.Services;
using ColairShaderPainter.ViewModels;
using ColairShaderPainter.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ColairShaderPainter;

// ═══════════════════════════════════════════════════════════════════════════════
// App.axaml.cs — Application class
//
// This is the root of the entire application. It's responsible for:
//   1. Loading the XAML resources (styles, themes)
//   2. Setting up Dependency Injection (DI)
//   3. Creating the main window and connecting it to the ViewModel
//
// Dependency Injection (DI) is a pattern where services are created and
// managed centrally, then "injected" into classes that need them. This
// makes the code more modular, testable, and easier to change.
//
// Think of DI like a restaurant: instead of each table growing its own
// vegetables (creating its own dependencies), the kitchen (DI container)
// manages all ingredients and delivers them to where they're needed.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// The root Application class. This is the "main" object that represents
/// the entire running app in the Avalonia framework.
///
/// Services is a public static field that provides a simple way to access
/// the DI container from anywhere. This is called a "service locator" —
/// it's convenient but should be used sparingly (constructor injection
/// via DI is the preferred pattern).
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Global service locator. Provides access to any registered service
    /// from anywhere in the app. Used sparingly — most classes receive
    /// their dependencies through constructor injection (cleaner pattern).
    ///
    /// Usage: var service = App.Services.GetRequiredService<ISomeService>();
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Called by Avalonia to load the XAML-defined styles and resources.
    /// This is where App.axaml is parsed and applied.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Called after the framework has initialised. This is where we set up
    /// the application: load configuration, build the DI container,
    /// and create the main window.
    ///
    /// Avalonia calls this once at startup, after Initialize().
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // ── Step 1: Load configuration ─────────────────────────────────────
        // Reads appsettings.json from the same folder as the .exe file.
        // This file contains the active provider and other base settings.
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        // ── Step 2: Build the DI container ─────────────────────────────────
        var services = new ServiceCollection();
        ConfigureServices(services, config);
        Services = services.BuildServiceProvider();

        // ── Step 3: Create the main window ─────────────────────────────────
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                // The ViewModel is created by the DI container, which
                // automatically injects all its dependencies
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Registers all services with the DI container.
    ///
    /// This is like creating a "menu" of available services:
    ///   - Singleton: One instance shared across the whole app (like a single
    ///     coffee machine everyone uses)
    ///   - Transient: A new instance every time it's requested (like disposable
    ///     cups — fresh one each time)
    ///
    /// When a class needs a service, the DI container looks at its constructor
    /// parameters and automatically provides the right instances.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        // ── Persistence + Settings ─────────────────────────────────────────
        services.AddSingleton<SettingsService>();           // Settings file I/O
        services.AddSingleton<ConnectionTestService>();     // API connection testing

        // ── LLM provider factory ───────────────────────────────────────────
        services.AddSingleton<LlmProviderFactory>();         // Creates the right AI provider

        // ── AI orchestration ───────────────────────────────────────────────
        services.AddSingleton<ILlmService, LlmOrchestrationService>();  // Self-healing loop

        // ── Shader utilities ───────────────────────────────────────────────
        services.AddSingleton<ShaderUniformParser>();        // Parses GLSL uniforms

        // ── ViewModels ─────────────────────────────────────────────────────
        services.AddSingleton<SettingsViewModel>();          // Settings overlay (shared)
        services.AddTransient<MainWindowViewModel>();        // Main window VM (fresh per window)
    }
}
