using System.Windows.Input;
using ColairShaderPainter.Models;
using ColairShaderPainter.Services;

namespace ColairShaderPainter.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════════
// ProviderCardViewModel.cs
//
// ViewModel for a single AI provider card in the Settings panel.
//
// Each provider (OpenAI, Anthropic, DeepSeek, etc.) has a "card" in the
// settings UI that shows:
//   - An icon and name
//   - An API key text field (with show/hide toggle)
//   - A model selector dropdown
//   - A "Test Connection" button
//   - Connection status indicator
//   - (Optional) Base URL field for custom endpoints
//
// This ViewModel manages the state for ONE card. The SettingsViewModel
// creates one instance per provider configured in AppSettings.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ViewModel for one provider configuration card in the settings overlay.
///
/// Manages:
///   - API key entry and visibility toggle
///   - Model selection from available options
///   - Live connection testing (async with cancellation)
///   - Status display (idle, testing, connected, failed)
///   - Building a ProviderConfig snapshot for persistence
/// </summary>
public sealed class ProviderCardViewModel : ViewModelBase
{
    /// <summary>Service for testing connections to AI APIs</summary>
    private readonly ConnectionTestService _tester;

    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>The dictionary key in AppSettings.Providers (e.g. "OpenAi", "Anthropic")</summary>
    public string       Key  { get; }

    /// <summary>What API format does this provider use?</summary>
    public ProviderType Type { get; }

    /// <summary>Display name (e.g. "OpenAI", "Anthropic Claude", "DeepSeek")</summary>
    public string       Name { get; }

    /// <summary>Emoji icon shown in the card header</summary>
    public string       Icon { get; }

    // ── API Key field ─────────────────────────────────────────────────────────
    private string _apiKey = string.Empty;

    /// <summary>The API key the user entered (masked in the UI unless ShowKey is true)</summary>
    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (!SetProperty(ref _apiKey, value)) return;
            // When the key changes, reset the connection status
            Status = ConnectionStatus.Idle;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(CanTest));
        }
    }

    private bool _showKey;

    /// <summary>True = show the API key in plain text, False = mask it</summary>
    public bool ShowKey
    {
        get => _showKey;
        set => SetProperty(ref _showKey, value);
    }

    /// <summary>
    /// True when there's no test currently running AND a key has been entered.
    /// Used to enable/disable the "Test" button.
    /// </summary>
    public bool CanTest => !IsTesting && !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>Command toggles API key visibility (eye icon)</summary>
    public ICommand ToggleShowKeyCommand { get; }

    // ── Model selector ────────────────────────────────────────────────────────

    /// <summary>List of available models the user can choose from</summary>
    public List<string> AvailableModels { get; }

    private string _selectedModel;

    /// <summary>The currently selected model for this provider</summary>
    public string SelectedModel
    {
        get => _selectedModel;
        set => SetProperty(ref _selectedModel, value);
    }

    // ── Base URL (advanced — shown for Ollama / Azure / custom endpoints) ──────
    private string _baseUrl;

    /// <summary>Custom base URL for the API endpoint</summary>
    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    /// <summary>True if the base URL field should be visible (OpenAI-compatible providers)</summary>
    public bool ShowBaseUrl => Type == ProviderType.OpenAiCompatible;

    // ── Connection test ───────────────────────────────────────────────────────
    private ConnectionStatus _status = ConnectionStatus.Idle;

    /// <summary>Current status of the connection test (Idle/Testing/Connected/Failed)</summary>
    public ConnectionStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            // Refresh all computed properties that depend on Status
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(IsTesting));
            OnPropertyChanged(nameof(CanTest));
        }
    }

    private string _statusDetail = string.Empty;

    /// <summary>Detailed status message (e.g. error text from a failed test)</summary>
    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    /// <summary>True while a connection test is in progress</summary>
    public bool   IsTesting    => Status == ConnectionStatus.Testing;

    /// <summary>Human-readable status text for display in the card</summary>
    public string StatusText   => Status switch
    {
        ConnectionStatus.Idle      => string.IsNullOrWhiteSpace(ApiKey) ? "No key configured" : "Not tested",
        ConnectionStatus.Testing   => "Testing connection...",
        ConnectionStatus.Connected => $"● Connected",
        ConnectionStatus.Failed    => $"✕ {StatusDetail}",
        _                          => string.Empty
    };

    /// <summary>Hex color string for the status indicator (used in UI binding)</summary>
    public string StatusColor  => Status switch
    {
        ConnectionStatus.Connected => "#22D3EE",  // Cyan
        ConnectionStatus.Failed    => "#F43F5E",  // Red
        ConnectionStatus.Testing   => "#FBBF24",  // Amber
        _                          => "#4A4570"   // Dim gray
    };

    /// <summary>Command that triggers the connection test</summary>
    public ICommand TestConnectionCommand { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a ViewModel for one provider card from its config data.
    /// </summary>
    public ProviderCardViewModel(
        string key,
        ProviderConfig config,
        ConnectionTestService tester)
    {
        Key             = key;
        Type            = config.Type;
        Name            = config.Name;
        Icon            = config.Icon;
        _apiKey         = config.ApiKey;    // Start with saved key (might be empty)
        _selectedModel  = config.Model;
        _baseUrl        = config.BaseUrl;
        AvailableModels = [.. config.Models];
        _tester         = tester;

        // Wire commands
        ToggleShowKeyCommand  = new RelayCommand(() => ShowKey = !ShowKey, () => true);
        TestConnectionCommand = new RelayCommand(async () => await RunTestAsync(), () => !IsTesting);
    }

    // ── Test logic ─────────────────────────────────────────────────────────── 

    private CancellationTokenSource? _testCts;

    /// <summary>
    /// Runs an async connection test against the provider's API.
    /// Updates Status to reflect the result.
    ///
    /// Has a 15-second timeout — if the API doesn't respond by then,
    /// the test is cancelled and shown as failed.
    /// </summary>
    private async Task RunTestAsync()
    {
        try
        {
            // Cancel any previous test still running
            _testCts?.Cancel();
            _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            Status = ConnectionStatus.Testing;

            // Build the current config from our fields and test it
            var config = BuildCurrentConfig();
            var (success, message) = await _tester.TestAsync(config, _testCts.Token);

            StatusDetail = message;
            Status       = success ? ConnectionStatus.Connected : ConnectionStatus.Failed;
        }
        catch (OperationCanceledException)
        {
            Status = ConnectionStatus.Idle;
            StatusDetail = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusDetail = ex.Message;
            Status       = ConnectionStatus.Failed;
        }
    }

    // ── Snapshot ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a ProviderConfig from the current state of this ViewModel.
    /// Used when saving settings — captures what the user entered.
    /// </summary>
    public ProviderConfig BuildCurrentConfig() => new()
    {
        Type    = Type,
        Name    = Name,
        Icon    = Icon,
        BaseUrl = _baseUrl,
        ApiKey  = _apiKey,
        Model   = _selectedModel,
        Models  = AvailableModels
    };
}
