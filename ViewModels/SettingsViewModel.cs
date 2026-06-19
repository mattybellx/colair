using System.Collections.ObjectModel;
using System.Windows.Input;
using ColairShaderPainter.Models;
using ColairShaderPainter.Services;

namespace ColairShaderPainter.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════════
// SettingsViewModel.cs
//
// ViewModel for the full settings overlay dialog.
//
// The settings overlay has tabs:
//   - Providers: Manage AI provider configs (API keys, models, test connections)
//   - Rendering: SSAA quality settings, max retries
//   - About: Version info and credits
//
// This ViewModel owns the collection of ProviderCardViewModels and coordinates
// saving/loading settings through the SettingsService.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ViewModel for the settings overlay. Manages provider cards, active provider
/// selection, rendering quality, and tab navigation.
///
/// This is a singleton — one instance shared across the app. The MainWindowViewModel
/// references it as the "Settings" property and binds it to the overlay UI.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService       _settingsService;
    private readonly ConnectionTestService _tester;

    // ── Provider cards ────────────────────────────────────────────────────────

    /// <summary>
    /// Observable collection of provider card ViewModels.
    /// "Observable" means the UI automatically updates when items are added/removed.
    /// Each card represents one AI provider (OpenAI, Anthropic, etc.).
    /// </summary>
    public ObservableCollection<ProviderCardViewModel> ProviderCards { get; } = [];

    private string _activeProviderKey;

    /// <summary>Which provider is currently selected as the active one for generation</summary>
    public string ActiveProviderKey
    {
        get => _activeProviderKey;
        set
        {
            if (!SetProperty(ref _activeProviderKey, value)) return;
            _settingsService.SetActiveProvider(value);  // Persist immediately
        }
    }

    /// <summary>List of provider key names for the "Active Provider" dropdown</summary>
    public List<string> ProviderKeys { get; }

    // ── Rendering quality ─────────────────────────────────────────────────────
    private float _ssaaFactor;

    /// <summary>SSAA factor setting: 1.0 = native, 1.5 = balanced, 2.0 = ultra</summary>
    public float SsaaFactor
    {
        get => _ssaaFactor;
        set => SetProperty(ref _ssaaFactor, value);
    }

    // ── Tab navigation ────────────────────────────────────────────────────────
    private int _selectedTab;

    /// <summary>Which settings tab is currently selected (0 = Providers, 1 = Rendering, 2 = About)</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Save all settings and close the overlay</summary>
    public ICommand SaveAndCloseCommand { get; }

    /// <summary>Switch to a specific settings tab</summary>
    public ICommand SelectTabCommand    { get; }

    /// <summary>Event fired when the user clicks Save & Close — the MainWindowViewModel listens for this</summary>
    public event Action? CloseRequested;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the SettingsViewModel, populating provider cards from the
    /// currently saved settings.
    /// </summary>
    public SettingsViewModel(SettingsService settingsService, ConnectionTestService tester)
    {
        _settingsService = settingsService;
        _tester          = tester;

        var cfg = settingsService.Current;

        _activeProviderKey = cfg.ActiveProvider;
        _ssaaFactor        = cfg.SsaaFactor;

        // Create a card ViewModel for each configured provider
        foreach (var (k, v) in cfg.Providers)
            ProviderCards.Add(new ProviderCardViewModel(k, v, tester));

        ProviderKeys = [.. cfg.Providers.Keys];

        SaveAndCloseCommand = new RelayCommand(SaveAndClose, () => true);
        SelectTabCommand    = new RelayCommand<int>(t => SelectedTab = t);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes ViewModel state from the persisted settings on disk.
    /// Called when the settings overlay opens, to pick up any changes
    /// that might have been made externally.
    /// </summary>
    public void RefreshFromDisk()
    {
        var cfg = _settingsService.Load();
        _activeProviderKey = cfg.ActiveProvider;
        _ssaaFactor = cfg.SsaaFactor;
        OnPropertyChanged(nameof(ActiveProviderKey));
        OnPropertyChanged(nameof(SsaaFactor));
    }
    
    /// <summary>
    /// Persists all current ViewModel state to the SettingsService without closing.
    /// This ensures the latest provider configs are used even if the user
    /// doesn't click "Save & Close" before generating.
    /// </summary>
    public void SyncNow()
    {
        var cfg = _settingsService.Current;
        cfg.SsaaFactor     = _ssaaFactor;
        cfg.ActiveProvider = _activeProviderKey;
        foreach (var card in ProviderCards)
            cfg.Providers[card.Key] = card.BuildCurrentConfig();
        _settingsService.Save(cfg);
    }

    /// <summary>Saves all settings and fires the CloseRequested event</summary>
    private void SaveAndClose()
    {
        SyncNow();
        CloseRequested?.Invoke();
    }
}
