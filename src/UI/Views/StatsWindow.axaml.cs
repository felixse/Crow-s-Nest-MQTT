using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform; // For IClipboard.SetTextAsync extension
using Avalonia.Interactivity;
using CrowsNestMqtt.UI.ViewModels;
using ReactiveUI;

namespace CrowsNestMqtt.UI.Views;

/// <summary>
/// Non-modal window that displays per-topic MQTT statistics for the <c>:stats</c> command.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class StatsWindow : Window
{
    /// <summary>Command used by the Escape key binding to close the window.</summary>
    public ICommand CloseCommand { get; }

    private IDisposable? _clipboardSubscription;
    private IDisposable? _closeSubscription;
    private StatsViewModel? _sortDefaultsAppliedFor;

    public StatsWindow()
    {
        CloseCommand = ReactiveCommand.Create(Close);
        InitializeComponent();

        AddHandler(KeyDownEvent, OnKeyDownForcedHandler,
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnKeyDownForcedHandler(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _clipboardSubscription?.Dispose();
        _closeSubscription = null;

        if (DataContext is not StatsViewModel vm)
        {
            return;
        }

        // Wire the clipboard interaction so "Copy as Markdown" actually reaches the OS clipboard.
        _clipboardSubscription = vm.CopyTextToClipboardInteraction.RegisterHandler(async interaction =>
        {
            try
            {
                var clipboard = GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(interaction.Input);
                }
                interaction.SetOutput(Unit.Default);
            }
            catch
            {
                // Swallow — VM surfaces failure via StatusText.
                interaction.SetOutput(Unit.Default);
                throw;
            }
        });

        // Bubble VM close requests to the window.
        vm.CloseRequested += VmOnCloseRequested;
        _closeSubscription = new AnonymousDisposable(() => vm.CloseRequested -= VmOnCloseRequested);

        // Apply default sort (# Messages descending) once per VM.
        if (!ReferenceEquals(_sortDefaultsAppliedFor, vm))
        {
            _sortDefaultsAppliedFor = vm;
            ApplyDefaultSort();
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _clipboardSubscription?.Dispose();
        _clipboardSubscription = null;
        _closeSubscription?.Dispose();
        _closeSubscription = null;

        if (DataContext is StatsViewModel vm)
        {
            vm.StopLiveRefresh();
        }

        base.OnClosed(e);
    }

    private void VmOnCloseRequested(object? sender, EventArgs e) => Close();

    private void ApplyDefaultSort()
    {
        var grid = this.FindControl<DataGrid>("StatsGrid");
        if (grid == null)
        {
            return;
        }

        // Sort by "# Messages" descending by default.
        var column = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "# Messages");
        if (column != null)
        {
            column.Sort(ListSortDirection.Descending);
        }
    }

    private sealed class AnonymousDisposable : IDisposable
    {
        private Action? _dispose;
        public AnonymousDisposable(Action dispose) { _dispose = dispose; }
        public void Dispose()
        {
            var d = _dispose;
            _dispose = null;
            d?.Invoke();
        }
    }
}
