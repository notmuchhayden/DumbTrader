using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using DumbTrader.Views;

namespace DumbTrader;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _isUpdatingViewMenu;

    // Cached references to layout elements
    private LayoutAnchorable? _sidebarAnchorable;
    private LayoutAnchorable? _summaryAnchorable;
    private LayoutAnchorable? _logAnchorable;

    private LayoutAnchorablePane? _sidebarPane;
    private LayoutAnchorablePane? _summaryPane;
    private LayoutAnchorablePane? _logPane;

    private LayoutPanel? _rootLayoutPanel;
    private LayoutPanel? _centerLayoutPanel;

    public MainWindow()
    {
        InitializeComponent();
        CacheLayoutReferences();
        InitializeViewMenu();
    }

    private void CacheLayoutReferences()
    {
        // Cache all layout element references at initialization
        _sidebarAnchorable = FindName("SidebarAnchorable") as LayoutAnchorable;
        _summaryAnchorable = FindName("SummaryAnchorable") as LayoutAnchorable;
        _logAnchorable = FindName("LogAnchorable") as LayoutAnchorable;

        _sidebarPane = FindName("SidebarPane") as LayoutAnchorablePane;
        _summaryPane = FindName("SummaryPane") as LayoutAnchorablePane;
        _logPane = FindName("LogPane") as LayoutAnchorablePane;

        _rootLayoutPanel = FindName("RootLayoutPanel") as LayoutPanel;
        _centerLayoutPanel = FindName("CenterLayoutPanel") as LayoutPanel;
    }

    private void InitializeViewMenu()
    {
        SubscribeAnchorable(_sidebarAnchorable);
        SubscribeAnchorable(_summaryAnchorable);
        SubscribeAnchorable(_logAnchorable);

        UpdateViewMenuChecks();
    }

    private void SubscribeAnchorable(LayoutAnchorable? anchorable)
    {
        if (anchorable == null)
            return;

        anchorable.IsVisibleChanged += Anchorable_IsVisibleChanged;
        anchorable.Closing += Anchorable_Closing;
    }

    private void Anchorable_IsVisibleChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateViewMenuChecks);
    }

    /// <summary>
    /// Intercept the Close (X button) action and convert it to Hide instead.
    /// This keeps the anchorable in the layout tree so it can be restored via Show().
    /// </summary>
    private void Anchorable_Closing(object? sender, CancelEventArgs e)
    {
        if (sender is LayoutAnchorable anchorable)
        {
            // Cancel the close operation
            e.Cancel = true;

            // Hide instead of close, so the anchorable stays in the layout tree
            Dispatcher.BeginInvoke(anchorable.Hide);
        }
    }

    private void UpdateViewMenuChecks()
    {
        _isUpdatingViewMenu = true;

        UpdateMenuCheck("SidebarMenuItem", _sidebarAnchorable);
        UpdateMenuCheck("SummaryMenuItem", _summaryAnchorable);
        UpdateMenuCheck("LogMenuItem", _logAnchorable);

        _isUpdatingViewMenu = false;
    }

    private void UpdateMenuCheck(string menuItemName, LayoutAnchorable? anchorable)
    {
        if (FindName(menuItemName) is MenuItem menu && anchorable != null)
            menu.IsChecked = anchorable.IsVisible && !anchorable.IsHidden;
    }

    private void ViewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingViewMenu)
            return;

        if (sender is not MenuItem menuItem)
            return;

        var name = menuItem.Name;

        switch (name)
        {
            case "SidebarMenuItem":
                SetAnchorableVisible(_sidebarAnchorable, _sidebarPane, _rootLayoutPanel, menuItem.IsChecked, insertBeforePanel: _centerLayoutPanel);
                break;
            case "SummaryMenuItem":
                SetAnchorableVisible(_summaryAnchorable, _summaryPane, _centerLayoutPanel, menuItem.IsChecked, insertAtIndex: 0);
                break;
            case "LogMenuItem":
                SetAnchorableVisible(_logAnchorable, _logPane, _centerLayoutPanel, menuItem.IsChecked, insertAtEnd: true);
                break;
        }
    }

    /// <summary>
    /// Show or hide an anchorable, restoring its pane to the correct parent panel if needed.
    /// </summary>
    private static void SetAnchorableVisible(
        LayoutAnchorable? anchorable,
        LayoutAnchorablePane? pane,
        LayoutPanel? parentPanel,
        bool isVisible,
        ILayoutPanelElement? insertBeforePanel = null,
        int insertAtIndex = -1,
        bool insertAtEnd = false)
    {
        if (anchorable == null)
            return;

        if (isVisible)
        {
            // Restore pane to parent panel if detached
            if (pane != null && parentPanel != null && pane.Parent == null)
            {
                if (insertBeforePanel != null)
                {
                    int index = parentPanel.Children.IndexOf(insertBeforePanel);
                    parentPanel.Children.Insert(index >= 0 ? index : 0, pane);
                }
                else if (insertAtEnd)
                {
                    parentPanel.Children.Add(pane);
                }
                else if (insertAtIndex >= 0)
                {
                    parentPanel.Children.Insert(insertAtIndex, pane);
                }
            }

            // Restore anchorable to pane if detached
            if (anchorable.Parent == null && pane != null)
            {
                pane.Children.Add(anchorable);
            }

            anchorable.Show();
        }
        else
        {
            anchorable.Hide();
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow { Owner = this };
        settings.ShowDialog();
    }

    private void OpenAbout_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }
}
