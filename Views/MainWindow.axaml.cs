using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ColairShaderPainter.Graphics;
using ColairShaderPainter.ViewModels;

namespace ColairShaderPainter.Views;

public partial class MainWindow : Window
{
    private Grid? _mainContent;
    private bool _wasMaximized;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        KeyDown += OnGlobalKeyDown;
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.Escape && WindowState == WindowState.FullScreen))
        { ToggleFullscreen(); e.Handled = true; }
        if (e.Key == Key.Space && !e.Handled)
        {
            if (this.FocusManager?.GetFocusedElement() is TextBox) return;
            if (DataContext is MainWindowViewModel vm && !vm.IsGenerating)
            { vm.GenerateCommand.Execute(null); e.Handled = true; }
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _mainContent = this.FindControl<Grid>("MainContent");
        if (DataContext is MainWindowViewModel vm &&
            this.FindControl<GlShaderViewport>("ShaderViewport") is { } vp)
            vm.AttachViewport(vp);

        WireBtn("CloseBtn", () => Close());
        WireBtn("MinBtn", () => WindowState = WindowState.Minimized);
        WireBtn("FullscreenBtn", () => ToggleFullscreen());

        WireSettingsTabs("ProvidersTabBtn","ProvidersPanel","RenderingTabBtn","RenderingPanel","AboutTabBtn","AboutPanel");
        if (this.FindControl<Border>("TitleBar") is { } tb)
            tb.PointerPressed += (_, e2) => { if (e2.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e2); };
        else
            PointerPressed += (_, e2) => { if (e2.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e2); };
    }

    private void WireBtn(string name, Action action)
    {
        if (this.FindControl<Button>(name) is { } btn)
            btn.Click += (_, _) => action();
    }

    private void WireSettingsTabs(string a, string b, string c, string d, string e, string f)
    {
        var b1 = this.FindControl<Border>(a); var b2 = this.FindControl<Border>(c); var b3 = this.FindControl<Border>(e);
        var p1 = this.FindControl<StackPanel>(b); var p2 = this.FindControl<StackPanel>(d); var p3 = this.FindControl<StackPanel>(f);
        if (b1 is null || b2 is null || b3 is null || p1 is null || p2 is null || p3 is null) return;
        void Act(Border ba, StackPanel pa) { foreach (var x in new[] { (b: b1, p: p1), (b: b2, p: p2), (b: b3, p: p3) }) x.p.IsVisible = x.p == pa; }
        b1.PointerPressed += (_, _) => Act(b1, p1); b2.PointerPressed += (_, _) => Act(b2, p2); b3.PointerPressed += (_, _) => Act(b3, p3);
    }

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _wasMaximized ? WindowState.Maximized : WindowState.Normal;
            if (_mainContent != null) _mainContent.ColumnDefinitions[1].Width = new GridLength(300);
        }
        else
        {
            _wasMaximized = WindowState == WindowState.Maximized;
            if (_mainContent != null) _mainContent.ColumnDefinitions[1].Width = new GridLength(0);
            WindowState = WindowState.FullScreen;
        }
    }
}
