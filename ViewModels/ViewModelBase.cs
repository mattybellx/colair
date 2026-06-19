using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ColairShaderPainter.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════════
// ViewModelBase.cs
//
// The base class for ALL ViewModels in the app.
//
// In MVVM (Model-View-ViewModel), the ViewModel is the bridge between the
// UI (View) and the data/logic (Model). It exposes properties that the
// XAML UI can "bind" to — when a property changes, the UI updates automatically.
//
// This base class implements INotifyPropertyChanged (INPC), which is the
// standard .NET interface for property change notifications. It provides
// two helper methods that make writing ViewModels much less repetitive.
//
// Without this base, every single property would need ~10 lines of boilerplate.
// With SetProperty<T>, each property is just a few lines.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Minimal base class for all ViewModels. Implements the property change
/// notification system that makes data binding work in Avalonia/WPF.
///
/// How it works:
///   1. The XAML view binds to a property (e.g., Text="{Binding UserName}")
///   2. When the ViewModel property changes, it calls SetProperty or OnPropertyChanged
///   3. INotifyPropertyChanged fires an event
///   4. Avalonia's binding engine hears the event and updates the UI
///
/// This is called "reactive UI" — the UI reacts to data changes automatically.
///
/// We don't use any external MVVM library (like ReactiveUI or CommunityToolkit.Mvvm)
/// to keep the dependency tree small. This minimal base is all we need.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// Event that fires when a property value changes.
    /// Avalonia's binding system subscribes to this event automatically.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets a property value and fires the change notification if the value
    /// actually changed. Returns false if the new value equals the old value
    /// (no notification needed — saves unnecessary UI updates).
    ///
    /// Usage: SetProperty(ref _myField, newValue);
    ///
    /// The [CallerMemberName] attribute automatically fills in the property
    /// name (e.g., if this is called from the "UserName" setter, propertyName
    /// is automatically "UserName"). This eliminates hardcoded strings.
    /// </summary>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        // Check if the value actually changed (avoid unnecessary updates)
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        // Notify the UI that this property changed
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>
    /// Manually fires a property change notification.
    /// Useful for notifying the UI about computed properties that don't have
    /// their own backing field (e.g., a FullName property that combines
    /// FirstName + LastName).
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
